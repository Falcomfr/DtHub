using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Construit un <see cref="AndroidDevice"/> à partir de ce qu'ADB rapporte, de
/// ce que le téléphone déclare et de ce qui avait été mémorisé. Fonction pure,
/// donc vérifiable sur des sorties enregistrées.
/// </summary>
public static class DeviceFactory
{
    /// <summary>Préfixe des identités de repli, quand aucun numéro matériel n'est lisible.</summary>
    public const string FallbackIdPrefix = "adb:";

    /// <summary>
    /// Fusionne les trois sources. Les choix de l'utilisateur, portés par
    /// <paramref name="known"/>, ne sont jamais écrasés par une découverte.
    /// </summary>
    /// <param name="entry">Ligne renvoyée par <c>adb devices</c>.</param>
    /// <param name="properties">Sortie de <c>getprop</c>, éventuellement absente.</param>
    /// <param name="known">Appareil déjà mémorisé, s'il a été reconnu.</param>
    /// <param name="observedAtUtc">Instant de l'observation.</param>
    public static AndroidDevice Create(
        AdbDeviceEntry entry,
        IReadOnlyDictionary<string, string>? properties = null,
        AndroidDevice? known = null,
        DateTimeOffset? observedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var hardwareSerial = DeviceProperties.ReadHardwareSerial(properties);

        return new AndroidDevice
        {
            Id = ResolveId(entry, hardwareSerial, known),
            Serial = entry.Serial,
            State = entry.State,
            ConnectionKind = entry.ConnectionKind,

            // Une découverte sans getprop ne doit pas effacer ce qu'on savait
            // déjà : c'est le cas d'un appareil non autorisé ou endormi.
            Manufacturer = DeviceProperties.ReadManufacturer(properties) ?? known?.Manufacturer,
            Model = DeviceProperties.ReadModel(properties) ?? entry.Model?.Replace('_', ' ') ?? known?.Model,
            MarketName = DeviceProperties.ReadMarketName(properties) ?? known?.MarketName,
            DeviceCodename = DeviceProperties.ReadDeviceCodename(properties) ?? entry.Device ?? known?.DeviceCodename,
            AndroidVersion = DeviceProperties.ReadAndroidRelease(properties) ?? known?.AndroidVersion,
            SdkVersion = DeviceProperties.ReadSdkVersion(properties) ?? known?.SdkVersion,

            CustomName = known?.CustomName,
            IsPrimary = known?.IsPrimary ?? false,

            // Une connexion sans fil n'existe pas sans association préalable :
            // la constater suffit à savoir que l'appareil est appairé, et
            // c'est ce qui autorise la reconnexion automatique ensuite.
            IsPaired = (known?.IsPaired ?? false) || entry.ConnectionKind == AdbConnectionKind.Wireless,

            // L'adresse mémorisée ne se met à jour que sur une connexion sans
            // fil active, sinon un branchement USB effacerait le seul moyen de
            // retrouver le téléphone sans câble.
            LastKnownAddress = entry.ConnectionKind == AdbConnectionKind.Wireless
                ? entry.Host ?? known?.LastKnownAddress
                : known?.LastKnownAddress,
            LastKnownPort = entry.ConnectionKind == AdbConnectionKind.Wireless
                ? entry.Port ?? known?.LastKnownPort
                : known?.LastKnownPort,

            LastSeenUtc = observedAtUtc ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Détermine l'identité stable. Le numéro matériel prime ; à défaut, un
    /// numéro de série USB fait l'affaire ; en sans-fil sans numéro matériel,
    /// on conserve l'identité déjà connue plutôt que d'en créer une nouvelle à
    /// chaque changement d'adresse.
    /// </summary>
    public static string ResolveId(AdbDeviceEntry entry, string? hardwareSerial, AndroidDevice? known)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!string.IsNullOrWhiteSpace(hardwareSerial))
        {
            return hardwareSerial;
        }

        if (known is not null)
        {
            return known.Id;
        }

        // Un appareil injoignable ne répond pas à getprop, mais le nom mDNS
        // sous lequel il s'annonce porte son numéro de série. Sans cela, le
        // même téléphone figurait deux fois dans la liste : une fois sous son
        // adresse, une fois sous ce nom.
        if (MdnsDeviceName.HardwareSerialFrom(entry.Serial) is { } announced)
        {
            return announced;
        }

        return entry.ConnectionKind == AdbConnectionKind.Wireless
            ? FallbackIdPrefix + entry.Serial
            : entry.Serial;
    }

    /// <summary>
    /// Retrouve un appareil mémorisé correspondant à une ligne ADB. On teste
    /// l'identité, puis le numéro de série, puis l'adresse : c'est ce qui
    /// permet de reconnaître un téléphone qui vient de passer du câble au
    /// Wi-Fi.
    /// </summary>
    public static AndroidDevice? Match(IEnumerable<AndroidDevice> known, AdbDeviceEntry entry, string? hardwareSerial = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(entry);

        var candidates = known as IReadOnlyCollection<AndroidDevice> ?? [.. known];

        // Le numéro matériel prime, qu'il vienne de getprop ou du nom mDNS.
        var identity = string.IsNullOrWhiteSpace(hardwareSerial)
            ? MdnsDeviceName.HardwareSerialFrom(entry.Serial)
            : hardwareSerial;

        if (!string.IsNullOrWhiteSpace(identity))
        {
            var byHardware = candidates.FirstOrDefault(
                d => string.Equals(d.Id, identity, StringComparison.Ordinal));

            if (byHardware is not null)
            {
                return byHardware;
            }
        }

        var bySerial = candidates.FirstOrDefault(
            d => string.Equals(d.Serial, entry.Serial, StringComparison.Ordinal));

        if (bySerial is not null)
        {
            return bySerial;
        }

        if (entry.Host is { Length: > 0 } host)
        {
            return candidates.FirstOrDefault(
                d => string.Equals(d.LastKnownAddress, host, StringComparison.Ordinal));
        }

        return null;
    }
}
