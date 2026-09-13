namespace DtHub.Core.Adb;

/// <summary>How the device is attached to the host.</summary>
public enum AdbConnectionKind
{
    Unknown = 0,

    /// <summary>USB cable.</summary>
    Usb,

    /// <summary>
    /// Wireless debugging, the serial number is an address and a
    /// port.
    /// </summary>
    Wireless,

    /// <summary>
    /// Local emulator, outside the functional scope but detected
    /// cleanly.
    /// </summary>
    Emulator,
}
