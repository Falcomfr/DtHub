namespace DtHub.Core.Devices;

/// <summary>
/// mDNS name advertised by Android's wireless debugging, of the form
/// <c>adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp</c>.
///
/// It carries the device's hardware serial number, followed by a
/// randomly drawn token. Reading it avoids creating a second identity
/// for a phone already known: the same device appears under its IP
/// address when it is reachable, and under this name when it is not,
/// since ADB cannot then be queried to get its serial number.
/// </summary>
public static class MdnsDeviceName
{
    private const string Prefix = "adb-";
    private const string ServiceMarker = "._adb";

    /// <summary>
    /// Hardware serial number carried by a full mDNS name, or
    /// <c>null</c> if the given text is not one. The service suffix
    /// is required: it is what distinguishes an advertised name from
    /// an ordinary serial number, and this method exists precisely to
    /// sort out what <c>adb devices</c> reports.
    /// </summary>
    public static string? HardwareSerialFrom(string? serial)
    {
        if (!LooksAnnounced(serial))
        {
            return null;
        }

        var service = serial!.IndexOf(ServiceMarker, StringComparison.Ordinal);

        return service > Prefix.Length ? SerialIn(serial[Prefix.Length..service]) : null;
    }

    /// <summary>
    /// Hardware serial number carried by an advertisement's instance
    /// name, that is, the first column of <c>adb mdns services</c>.
    ///
    /// The service type lives in its own column there: the name
    /// therefore does not carry its suffix, unlike the serial number
    /// that <c>adb devices</c> reports for an unreachable device. The
    /// sorting, meanwhile, is already done by ADB: whatever appears
    /// in this column is an advertisement.
    /// </summary>
    public static string? HardwareSerialFromInstance(string? instance)
    {
        if (!LooksAnnounced(instance))
        {
            return null;
        }

        var service = instance!.IndexOf(ServiceMarker, StringComparison.Ordinal);

        return SerialIn(service > 0 ? instance[Prefix.Length..service] : instance[Prefix.Length..]);
    }

    private static bool LooksAnnounced(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.StartsWith(Prefix, StringComparison.Ordinal);

    private static string? SerialIn(string body)
    {
        // The final token is separated by a hyphen. The serial
        // number can also contain one: it is the last one that
        // separates, not the first.
        var token = body.LastIndexOf('-');

        var extracted = token > 0 ? body[..token] : body;

        return extracted.Length > 0 ? extracted : null;
    }

    /// <summary>
    /// True if the given text is a wireless debugging mDNS name.
    /// </summary>
    public static bool IsMdnsName(string? serial) => HardwareSerialFrom(serial) is not null;
}
