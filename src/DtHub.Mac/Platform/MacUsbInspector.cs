using DtHub.Core.Devices;

namespace DtHub.Mac.Platform;

/// <summary>
/// What Windows' device manager says of a phone's USB driver. macOS needs no
/// driver for adb: there is no fault to report, and nothing to count.
/// </summary>
internal sealed class MacUsbInspector : IUsbEnumerationInspector
{
    public IReadOnlyList<UsbFault> Faults() => [];

    public IReadOnlyList<UsbPresence> Present() => [];

    public int AdbInterfaces() => 0;
}
