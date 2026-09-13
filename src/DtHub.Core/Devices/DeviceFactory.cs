using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Builds an <see cref="AndroidDevice"/> from what ADB reports,
/// what the phone declares and what had been remembered. A pure
/// function, therefore verifiable on recorded outputs.
/// </summary>
public static class DeviceFactory
{
    /// <summary>
    /// Prefix for fallback identities, when no hardware number can
    /// be read.
    /// </summary>
    public const string FallbackIdPrefix = "adb:";

    /// <summary>
    /// Merges the three sources. The user's choices, carried by
    /// <paramref name="known"/>, are never overwritten by a
    /// discovery.
    /// </summary>
    /// <param name="entry">Line returned by <c>adb devices</c>.</param>
    /// <param name="properties">
    /// Output of <c>getprop</c>, possibly absent.
    /// </param>
    /// <param name="known">
    /// Already remembered device, if one was recognized.
    /// </param>
    /// <param name="observedAtUtc">Moment of the observation.</param>
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

            // A discovery without getprop should not erase what we
            // already knew: this is the case for a device that is
            // unauthorized or asleep.
            Manufacturer = DeviceProperties.ReadManufacturer(properties) ?? known?.Manufacturer,
            Model = DeviceProperties.ReadModel(properties) ?? entry.Model?.Replace('_', ' ') ?? known?.Model,
            MarketName = DeviceProperties.ReadMarketName(properties) ?? known?.MarketName,
            DeviceCodename = DeviceProperties.ReadDeviceCodename(properties) ?? entry.Device ?? known?.DeviceCodename,
            AndroidVersion = DeviceProperties.ReadAndroidRelease(properties) ?? known?.AndroidVersion,
            SdkVersion = DeviceProperties.ReadSdkVersion(properties) ?? known?.SdkVersion,

            CustomName = known?.CustomName,
            IsPrimary = known?.IsPrimary ?? false,

            // A wireless connection cannot exist without prior
            // pairing: observing it is enough to know the device is
            // paired, and that is what authorizes automatic
            // reconnection afterward.
            IsPaired = (known?.IsPaired ?? false) || entry.ConnectionKind == AdbConnectionKind.Wireless,

            // The remembered address only updates on an active
            // wireless connection, otherwise plugging in over USB
            // would erase the only way to find the phone again
            // without a cable.
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
    /// Determines the stable identity. The hardware number takes
    /// priority; failing that, a USB serial number will do; over
    /// wireless without a hardware number, we keep the already
    /// known identity rather than creating a new one on every
    /// address change.
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

        // An unreachable device does not answer getprop, but the
        // mDNS name under which it announces itself carries its
        // serial number. Without this, the same phone would show
        // up twice in the list: once under its address, once under
        // this name.
        if (MdnsDeviceName.HardwareSerialFrom(entry.Serial) is { } announced)
        {
            return announced;
        }

        return entry.ConnectionKind == AdbConnectionKind.Wireless
            ? FallbackIdPrefix + entry.Serial
            : entry.Serial;
    }

    /// <summary>
    /// Finds a remembered device matching an ADB line. We test the
    /// identity, then the serial number, then the address: that is
    /// what makes it possible to recognize a phone that just
    /// switched from cable to Wi-Fi.
    /// </summary>
    public static AndroidDevice? Match(IEnumerable<AndroidDevice> known, AdbDeviceEntry entry, string? hardwareSerial = null)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(entry);

        var candidates = known as IReadOnlyCollection<AndroidDevice> ?? [.. known];

        // The hardware number takes priority, whether it comes from
        // getprop or from the mDNS name.
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
