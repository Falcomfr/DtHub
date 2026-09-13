using DtHub.Core.Adb;
using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// What the application can say about the link with the phone.
/// </summary>
public enum ConnectionVerdict
{
    /// <summary>
    /// At least one phone answers: there is nothing to explain.
    /// </summary>
    Ready = 0,

    /// <summary>The Android tools could not start.</summary>
    ToolsMissing,

    /// <summary>
    /// A phone is there, but it has not yet authorized this PC.
    /// </summary>
    WaitingAuthorization,

    /// <summary>The USB driver refuses access to the device.</summary>
    DriverRefused,

    /// <summary>
    /// A device is plugged in but Windows could not read its
    /// identity.
    /// </summary>
    UsbUnreadable,

    /// <summary>A device is plugged in but no driver answers for it.</summary>
    UsbDriverMissing,

    /// <summary>
    /// A device is plugged in and Windows turned it away for another
    /// reason.
    /// </summary>
    UsbOther,

    /// <summary>Nothing plugged in, nothing reachable.</summary>
    NoDevice,
}

/// <summary>
/// Says in one word where the link stands, and why it is not
/// succeeding.
///
/// A pure function, like <see cref="AdbErrorInterpreter"/> whose work
/// it extends one level down. All of ADB's richness stops where it
/// can no longer see anything: a cable that does not carry data
/// produces no line at all in <c>adb devices</c>, and the application
/// then had nothing to say even though Windows, for its part, knew
/// everything.
///
/// The order of the rules is the order in which the causes follow one
/// another, from the closest to success to the farthest: a phone that
/// answers wins over another one waiting for authorization, and a
/// device that ADB can see wins over an enumeration fault, which can
/// then only concern another port.
/// </summary>
public static class ConnectionCheck
{
    /// <summary>
    /// The verdict, from what <c>adb devices</c> returns and what
    /// Windows knows about its USB ports.
    /// </summary>
    /// <param name="states">
    /// The state of each device seen, in any order.
    /// </param>
    /// <param name="faults">
    /// The USB devices that Windows failed to start.
    /// </param>
    /// <param name="toolsReady">
    /// False if the ADB executable is missing or silent.
    /// </param>
    public static ConnectionVerdict Of(
        IReadOnlyList<AdbDeviceState> states,
        IReadOnlyList<UsbFault> faults,
        bool toolsReady)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(faults);

        if (!toolsReady)
        {
            return ConnectionVerdict.ToolsMissing;
        }

        if (states.Contains(AdbDeviceState.Device))
        {
            return ConnectionVerdict.Ready;
        }

        if (states.Any(s => s is AdbDeviceState.Unauthorized or AdbDeviceState.Authorizing))
        {
            return ConnectionVerdict.WaitingAuthorization;
        }

        if (states.Contains(AdbDeviceState.NoPermissions))
        {
            return ConnectionVerdict.DriverRefused;
        }

        // ADB sees nothing: this is the only place where Windows's
        // opinion is used. The most explicit fault wins, since an
        // ordinary machine often has some faulting device that has
        // nothing to do with us.
        return Worst(faults) switch
        {
            UsbFaultKind.Unreadable => ConnectionVerdict.UsbUnreadable,
            UsbFaultKind.DriverMissing => ConnectionVerdict.UsbDriverMissing,
            UsbFaultKind.Other => ConnectionVerdict.UsbOther,
            _ => ConnectionVerdict.NoDevice,
        };
    }

    /// <summary>
    /// The most telling fault of the lot. An unreadable descriptor
    /// comes before a missing driver, which comes before a code that
    /// can only be quoted.
    /// </summary>
    private static UsbFaultKind Worst(IReadOnlyList<UsbFault> faults)
    {
        if (faults.Any(f => f.Kind == UsbFaultKind.Unreadable))
        {
            return UsbFaultKind.Unreadable;
        }

        if (faults.Any(f => f.Kind == UsbFaultKind.DriverMissing))
        {
            return UsbFaultKind.DriverMissing;
        }

        return faults.Any(f => f.Kind == UsbFaultKind.Other)
            ? UsbFaultKind.Other
            : UsbFaultKind.None;
    }

    /// <summary>
    /// A sentence for the screen, never a code or raw output.
    /// </summary>
    public static string Describe(ConnectionVerdict verdict) => Strings.Get(verdict switch
    {
        ConnectionVerdict.Ready => "CheckReady",
        ConnectionVerdict.ToolsMissing => "AdbUnavailable",
        ConnectionVerdict.WaitingAuthorization => "CheckWaitingAuthorization",
        ConnectionVerdict.DriverRefused => "CheckDriverRefused",
        ConnectionVerdict.UsbUnreadable => "CheckUsbUnreadable",
        ConnectionVerdict.UsbDriverMissing => "CheckUsbDriverMissing",
        ConnectionVerdict.UsbOther => "CheckUsbOther",
        _ => "CheckNoDevice",
    });

    /// <summary>
    /// True if the verdict deserves to be shown.
    ///
    /// Two verdicts have nothing to say. The first is obvious: a
    /// phone that answers calls for no comment.
    ///
    /// The second is less so. Having no device at all is not a
    /// failure: it is the application's resting state, the one found
    /// on opening it without having plugged anything in. Saying so in
    /// an alert block amounts to flagging a problem where there is
    /// only an absence, and the account list already says it just
    /// below, more briefly and in the right place. So two blocks used
    /// to echo each other, one long, one short, for the same nothing.
    ///
    /// What remains are the cases where something is there and does
    /// not work: a device seen but not yet authorized, a driver that
    /// refuses, an unreadable descriptor, the tools missing. Nobody
    /// can guess those, and the block exists for them.
    /// </summary>
    public static bool NeedsExplaining(ConnectionVerdict verdict) =>
        verdict is not (ConnectionVerdict.Ready or ConnectionVerdict.NoDevice);

    /// <summary>
    /// True if the cable troubleshooting sheet has something to
    /// offer. It does not show when the phone answers, nor when the
    /// ball is in the phone's court.
    /// </summary>
    public static bool NeedsCableHelp(ConnectionVerdict verdict) =>
        verdict is ConnectionVerdict.UsbUnreadable
            or ConnectionVerdict.UsbDriverMissing
            or ConnectionVerdict.UsbOther
            or ConnectionVerdict.NoDevice;
}
