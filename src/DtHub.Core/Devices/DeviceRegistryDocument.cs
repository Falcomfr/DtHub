namespace DtHub.Core.Devices;

/// <summary>
/// Persisted shape of <c>devices.json</c>. Deliberately distinct from
/// the domain model: the file format can evolve without constraining
/// the code, and <see cref="SchemaVersion"/> will allow an explicit
/// migration on the day it is needed.
/// </summary>
public sealed class DeviceRegistryDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<StoredDevice> Devices { get; set; } = [];

    /// <summary>
    /// Devices whose pairing has been broken, by hardware identifier.
    ///
    /// Deleting a device was not enough to get rid of it. The phone
    /// keeps announcing itself on the network, ADB keeps its key and
    /// reconnects to it, and the next scan registers it here again as
    /// an ordinary discovery. Forgetting meant no longer knowing
    /// anything, and therefore no longer being able to refuse anything.
    ///
    /// This list is the memory of the break. A device listed here is
    /// neither re-registered nor reconnected, until it is paired again
    /// from the window meant for that.
    ///
    /// Hardware identifiers and not addresses: an address changes with
    /// every network lease, and the break must survive it.
    /// </summary>
    public List<string> Discarded { get; set; } = [];

    /// <summary>
    /// Merges the duplicates left by version 1, and returns true if the
    /// file changed.
    ///
    /// A reachable phone was kept under its serial number, and the same
    /// phone, unreachable, under the mDNS name of its wireless
    /// debugging: two entries for a single device, one of them forever
    /// offline. The mDNS name does carry that serial number though, and
    /// it is now read.
    ///
    /// The most recently seen one wins, but whatever the user named or
    /// paired is carried over from the other one: those are choices,
    /// not discoveries.
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

/// <summary>A device remembered between two launches.</summary>
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

    /// <summary>Name given by the user.</summary>
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
    /// Rebuilds the domain model. State is not persisted: a remembered
    /// device is offline until discovery has seen it again.
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
