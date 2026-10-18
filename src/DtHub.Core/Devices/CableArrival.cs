using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// What the pairing window watches for when it is set to the cable: there
/// is no code to type, only a phone to plug in and a request to accept on
/// it. The window closes on the phone that arrived, not on one that was
/// already there.
/// </summary>
public static class CableArrival
{
    /// <summary>
    /// The phone plugged in since the window opened and now answering, or
    /// null while there is none.
    /// </summary>
    /// <param name="before">
    /// Serials of the phones already answering by cable when the window
    /// opened. A phone plugged but not yet accepted is left out on purpose:
    /// accepting the request is the very arrival being waited for.
    /// </param>
    public static AndroidDevice? Arrived(IReadOnlySet<string> before, IEnumerable<AndroidDevice> now) =>
        now.FirstOrDefault(d => d.ConnectionKind == AdbConnectionKind.Usb
                                && d.IsConnected
                                && !before.Contains(d.Serial));

    /// <summary>True while a phone at the end of a cable has not accepted this PC yet.</summary>
    public static bool AwaitsApproval(IEnumerable<AndroidDevice> now) =>
        now.Any(d => d.ConnectionKind == AdbConnectionKind.Usb
                     && d.State is AdbDeviceState.Unauthorized or AdbDeviceState.Authorizing);
}
