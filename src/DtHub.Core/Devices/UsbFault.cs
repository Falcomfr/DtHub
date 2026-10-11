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
/// A USB device Windows holds, faulty or not, with what says what it is:
/// the class codes it declares and the driver service bound to it.
/// </summary>
/// <param name="Id">The device instance identifier, serial included.</param>
/// <param name="CompatibleIds">The class codes, such as <c>USB\Class_FF&amp;SubClass_42&amp;Prot_01</c>.</param>
/// <param name="Service">The driver service bound to it, null when none is.</param>
public sealed record UsbPresence(string Id, IReadOnlyList<string> CompatibleIds, string? Service)
{
    /// <summary>
    /// True for an Android debugging interface: the phone offers it only
    /// with USB debugging on, whatever the brand.
    /// </summary>
    public bool IsDebugInterface =>
        CompatibleIds.Any(id => id.Contains("Class_FF&SubClass_42&Prot_01", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The phone makers' USB codes, to keep a mouse or a keyboard out of the
    /// log. Samsung's is also its SSDs': harmless in a log line.
    /// </summary>
    private static readonly string[] PhoneVendors =
        ["339B", "12D1", "04E8", "2717", "18D1", "2A70", "22D9", "2D95", "22B8", "0FCE", "1004", "0B05", "17EF", "1949", "19D2", "0BB4"];

    /// <summary>
    /// One log line of the phones Windows holds: identifier without the
    /// serial, whether it is the debugging interface, and its driver.
    /// What tells, from a report, why adb sees nothing.
    /// </summary>
    public static string Summarize(IEnumerable<UsbPresence> devices) =>
        string.Join(" ; ", devices
            .Where(d => d.IsDebugInterface || PhoneVendors.Any(v => d.Id.Contains("VID_" + v, StringComparison.OrdinalIgnoreCase)))
            .Select(d => $"{Short(d.Id)}{(d.IsDebugInterface ? " débogage" : string.Empty)} ({d.Service ?? "aucun pilote"})"));

    /// <summary><c>USB\VID_x&amp;PID_y&amp;MI_z\serial</c> without its first and last parts.</summary>
    private static string Short(string id) => id.Split('\\') is { Length: >= 2 } parts ? parts[1] : id;
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

    /// <summary>
    /// Every USB device Windows holds, faulty or not: a phone on charge
    /// only is there, healthy, and silent to ADB.
    /// </summary>
    IReadOnlyList<UsbPresence> Present();

    /// <summary>
    /// How many Android interfaces a driver has declared: what adb actually
    /// opens. A debugging interface without one is out of adb's reach.
    /// </summary>
    int AdbInterfaces();
}
