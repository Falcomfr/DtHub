namespace DtHub.Core.Adb;

/// <summary>
/// State reported by <c>adb devices</c>. The raw value is kept
/// alongside so nothing is lost when ADB introduces a state we do
/// not know yet.
/// </summary>
public enum AdbDeviceState
{
    /// <summary>
    /// Unrecognized state. The original string remains available.
    /// </summary>
    Unknown = 0,

    /// <summary>Ready to receive commands.</summary>
    Device,

    /// <summary>
    /// Known but unreachable: cable unplugged, Wi-Fi off, sleep.
    /// </summary>
    Offline,

    /// <summary>The RSA key has not yet been accepted on the phone.</summary>
    Unauthorized,

    /// <summary>Authorization currently being negotiated.</summary>
    Authorizing,

    /// <summary>TCP connection in progress.</summary>
    Connecting,

    /// <summary>The USB driver refuses access to the device.</summary>
    NoPermissions,

    Bootloader,
    Recovery,
    Sideload,
    Rescue,
    Host,

    /// <summary>The port exists but no device responds.</summary>
    Detached,
}
