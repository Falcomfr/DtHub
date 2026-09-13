namespace DtHub.Core.Adb;

/// <summary>
/// Reads what ADB says about a wireless connection that has failed.
///
/// **Three very different failures are written almost the same
/// way**, and the difference decides what we advise the user to do.
/// Recorded character for character on a French machine, against a
/// real phone:
///
/// <code>
/// closed port          cannot connect to 192.168.1.16:45573: … (10061)
/// machine unreachable  cannot connect to 192.168.1.99:40000: … (10060)
/// key refused          failed to connect to 192.168.1.16:37697
/// </code>
///
/// The first two carry a network error code: the TCP connection did
/// not even go through, the phone is off, out of network range, or
/// its wireless debugging changed port. The third carries none,
/// because there was no network fault: **the phone accepted the
/// connection then refused the handshake.** It is there, it is
/// listening, and it no longer recognizes this PC's key.
///
/// This is the only cause a new pairing fixes, and the only one we
/// cannot guess: nothing was unpaired by hand, and turning wireless
/// debugging off and back on changes nothing about it.
/// </summary>
public static class AdbConnectFailure
{
    /// <summary>
    /// What ADB writes when the network connection itself has
    /// failed.
    /// </summary>
    private const string NetworkFault = "cannot connect to";

    /// <summary>
    /// What it writes when the connection went through but what
    /// follows did not.
    /// </summary>
    private const string Handshake = "failed to connect to";

    /// <summary>
    /// True when the failure says the device refused this PC, and
    /// not that the network was missing.
    ///
    /// False on everything else, including a response we do not
    /// understand: advising a pairing when the phone is simply off
    /// would send the user to the wrong page.
    /// </summary>
    public static bool MeansRefusedKey(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return false;
        }

        var said = failureReason.Trim();

        return said.StartsWith(Handshake, StringComparison.OrdinalIgnoreCase)
            && !said.Contains(NetworkFault, StringComparison.OrdinalIgnoreCase);
    }
}
