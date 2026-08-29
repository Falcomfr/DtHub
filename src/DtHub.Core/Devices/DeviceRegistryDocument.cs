namespace DtHub.Core.Devices;

/// <summary>
/// Forme persistée de <c>devices.json</c>. Volontairement distincte du modèle
/// de domaine : le format de fichier peut évoluer sans contraindre le code, et
/// <see cref="SchemaVersion"/> permettra une migration explicite le jour venu.
/// </summary>
public sealed class DeviceRegistryDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<StoredDevice> Devices { get; set; } = [];
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
