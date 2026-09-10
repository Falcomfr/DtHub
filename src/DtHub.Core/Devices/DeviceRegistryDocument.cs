namespace DtHub.Core.Devices;

/// <summary>
/// Forme persistée de <c>devices.json</c>. Volontairement distincte du modèle
/// de domaine : le format de fichier peut évoluer sans contraindre le code, et
/// <see cref="SchemaVersion"/> permettra une migration explicite le jour venu.
/// </summary>
public sealed class DeviceRegistryDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<StoredDevice> Devices { get; set; } = [];

    /// <summary>
    /// Appareils dont l'association a été rompue, par identifiant matériel.
    ///
    /// Effacer un appareil ne suffisait pas à s'en défaire. Le téléphone
    /// continue de s'annoncer sur le réseau, ADB garde sa clé et s'y reconnecte,
    /// et le balayage suivant le réinscrit ici comme une découverte ordinaire.
    /// Oublier, c'était ne plus rien savoir, donc ne plus rien pouvoir refuser.
    ///
    /// Cette liste est la mémoire de la rupture. Un appareil qui y figure n'est
    /// ni réinscrit ni reconnecté, jusqu'à ce qu'on l'associe de nouveau depuis
    /// la fenêtre prévue pour cela.
    ///
    /// Des identifiants matériels et non des adresses : une adresse change à
    /// chaque bail réseau, et la rupture doit y survivre.
    /// </summary>
    public List<string> Discarded { get; set; } = [];

    /// <summary>
    /// Réunit les doublons laissés par la version 1, et rend vrai si le fichier
    /// a changé.
    ///
    /// Un téléphone joignable était retenu sous son numéro de série, et le même
    /// téléphone injoignable sous le nom mDNS de son débogage sans fil : deux
    /// entrées pour un seul appareil, dont l'une éternellement hors ligne. Le
    /// nom mDNS porte pourtant ce numéro de série, et il est désormais lu.
    ///
    /// La plus récemment vue l'emporte, mais ce que l'utilisateur a nommé ou
    /// appairé est repris de l'autre : ce sont des choix, pas des découvertes.
    /// </summary>
    public bool MergeDuplicates()
    {
        var byIdentity = new Dictionary<string, StoredDevice>(StringComparer.Ordinal);
        var merged = new List<StoredDevice>(Devices.Count);

        foreach (var device in Devices.OrderByDescending(d => d.LastSeenUtc ?? DateTimeOffset.MinValue))
        {
            var identity = MdnsDeviceName.HardwareSerialFrom(device.Serial)
                ?? MdnsDeviceName.HardwareSerialFrom(device.Id)
                ?? device.Id;

            if (byIdentity.TryGetValue(identity, out var kept))
            {
                kept.CustomName ??= device.CustomName;
                kept.IsPaired |= device.IsPaired;
                kept.IsPrimary |= device.IsPrimary;
                kept.LastKnownAddress ??= device.LastKnownAddress;
                kept.LastKnownPort ??= device.LastKnownPort;
                continue;
            }

            device.Id = identity;
            byIdentity[identity] = device;
            merged.Add(device);
        }

        if (merged.Count == Devices.Count)
        {
            return false;
        }

        Devices = merged;
        return true;
    }
}

/// <summary>Un appareil mémorisé entre deux lancements.</summary>
public sealed class StoredDevice
{
    public string Id { get; set; } = string.Empty;
    public string Serial { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? MarketName { get; set; }
    public string? DeviceCodename { get; set; }
    public string? AndroidVersion { get; set; }
    public int? SdkVersion { get; set; }

    /// <summary>Nom donné par l'utilisateur.</summary>
    public string? CustomName { get; set; }

    public string? LastKnownAddress { get; set; }
    public int? LastKnownPort { get; set; }
    public bool IsPaired { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }

    public static StoredDevice From(AndroidDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new StoredDevice
        {
            Id = device.Id,
            Serial = device.Serial,
            Manufacturer = device.Manufacturer,
            Model = device.Model,
            MarketName = device.MarketName,
            DeviceCodename = device.DeviceCodename,
            AndroidVersion = device.AndroidVersion,
            SdkVersion = device.SdkVersion,
            CustomName = device.CustomName,
            LastKnownAddress = device.LastKnownAddress,
            LastKnownPort = device.LastKnownPort,
            IsPaired = device.IsPaired,
            IsPrimary = device.IsPrimary,
            LastSeenUtc = device.LastSeenUtc,
        };
    }

    /// <summary>
    /// Reconstruit le modèle de domaine. L'état n'est pas persisté : un
    /// appareil mémorisé est hors ligne tant que la découverte ne l'a pas revu.
    /// </summary>
    public AndroidDevice ToDevice() => new()
    {
        Id = Id,
        Serial = Serial,
        State = Adb.AdbDeviceState.Offline,
        ConnectionKind = Adb.AdbConnectionKind.Unknown,
        Manufacturer = Manufacturer,
        Model = Model,
        MarketName = MarketName,
        DeviceCodename = DeviceCodename,
        AndroidVersion = AndroidVersion,
        SdkVersion = SdkVersion,
        CustomName = CustomName,
        LastKnownAddress = LastKnownAddress,
        LastKnownPort = LastKnownPort,
        IsPaired = IsPaired,
        IsPrimary = IsPrimary,
        LastSeenUtc = LastSeenUtc,
    };
}
