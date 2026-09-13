namespace DtHub.Core.Adb;

/// <summary>
/// A service advertised by wireless debugging on the local network.
/// The name contains the phone's serial number, which allows an
/// advertisement to be linked to an already known device.
/// </summary>
public sealed record MdnsService(string Name, string ServiceType, string Host, int Port)
{
    /// <summary>
    /// Pairing service, advertised for as long as the code is shown.
    /// </summary>
    public const string PairingType = "_adb-tls-pairing._tcp";

    /// <summary>
    /// Connection service, advertised continuously while wireless
    /// debugging is active.
    /// </summary>
    public const string ConnectType = "_adb-tls-connect._tcp";

    public string Address => $"{Host}:{Port}";

    public bool IsPairing => ServiceType.StartsWith(PairingType, StringComparison.Ordinal);

    public bool IsConnect => ServiceType.StartsWith(ConnectType, StringComparison.Ordinal);

    /// <summary>
    /// True if the advertisement appears to come from the device
    /// whose serial number is given. The mDNS name has the form
    /// <c>adb-&lt;serial&gt;-&lt;random&gt;</c>.
    /// </summary>
    public bool MatchesSerial(string? serial) =>
        !string.IsNullOrWhiteSpace(serial)
        && Name.Contains(serial, StringComparison.OrdinalIgnoreCase);
}
