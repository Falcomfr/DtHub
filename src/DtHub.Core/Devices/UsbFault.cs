namespace DtHub.Core.Devices;

/// <summary>
/// What Windows blames on a USB device, brought back to families that
/// a user can understand and fix.
///
/// This is the layer below ADB. When a phone does not enumerate, ADB
/// has nothing to say: for it, there is simply no device. Windows, on
/// the other hand, knows exactly what happened, and says so through a
/// problem code.
/// </summary>
public enum UsbFaultKind
{
    /// <summary>Nothing to report.</summary>
    None = 0,

    /// <summary>
    /// Code 43: the device descriptor could not be read, so Windows
    /// does not even know which device this is. The cable, the port,
    /// or the connector, never the phone itself.
    /// </summary>
    Unreadable,

    /// <summary>
    /// Code 28: the device is identified but no driver answers for it.
    /// This is the case for the ADB interface on a machine where it
    /// has never been used.
    /// </summary>
    DriverMissing,

    /// <summary>
    /// Another fault, named by its code for lack of anything better.
    /// </summary>
    Other,
}

/// <summary>
/// A USB device in fault, as Windows reports it.
/// </summary>
/// <param name="Kind">The fault's family.</param>
/// <param name="ProblemCode">The raw problem code, for the log.</param>
/// <param name="DeviceId">
/// The device identifier. It names no one: on an unreadable
/// descriptor, it is in fact worth <c>USB\VID_0000&amp;PID_0002</c>.
/// </param>
public sealed record UsbFault(UsbFaultKind Kind, int ProblemCode, string DeviceId)
{
    /// <summary>Problem codes that we know how to explain.</summary>
    public const int FailedPostStart = 43;
    public const int DriverNotInstalled = 28;

    /// <summary>Sorts a problem code into its family.</summary>
    public static UsbFaultKind KindOf(int problemCode) => problemCode switch
    {
        0 => UsbFaultKind.None,
        FailedPostStart => UsbFaultKind.Unreadable,
        DriverNotInstalled => UsbFaultKind.DriverMissing,
        _ => UsbFaultKind.Other,
    };
}

/// <summary>
/// Reads the state of USB devices from Windows.
///
/// Behind an interface because this is Win32: the core must be able
/// to reason about a fault without there being a real USB port on the
/// other end.
/// </summary>
public interface IUsbEnumerationInspector
{
    /// <summary>
    /// The USB devices that Windows failed to start, if there are any.
    /// Returns an empty list rather than an error: not knowing is an
    /// ordinary state, and the application works perfectly well
    /// without this information.
    /// </summary>
    IReadOnlyList<UsbFault> Faults();
}
