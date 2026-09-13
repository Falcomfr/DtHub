namespace DtHub.Core.Adb;

/// <summary>
/// Families of ADB failures, as a user can understand them. Used to
/// display a useful message and decide whether a recovery action is
/// possible, without ever showing ADB's raw output in the interface.
/// </summary>
public enum AdbErrorKind
{
    Unknown = 0,

    /// <summary>No device matches the requested serial number.</summary>
    DeviceNotFound,

    /// <summary>The device is known but does not respond.</summary>
    DeviceOffline,

    /// <summary>
    /// Debugging authorization has not been granted on the phone.
    /// </summary>
    DeviceUnauthorized,

    /// <summary>
    /// Several devices are connected and none has been designated.
    /// </summary>
    AmbiguousDevice,

    /// <summary>The network connection to the device failed.</summary>
    ConnectionFailed,

    /// <summary>The pairing code was refused or expired.</summary>
    PairingFailed,

    /// <summary>
    /// The requested package does not exist for this Android user.
    /// </summary>
    PackageNotFound,

    /// <summary>
    /// The targeted Android user does not exist or is not started.
    /// </summary>
    UserNotAvailable,

    /// <summary>
    /// The phone refused the operation for lack of permission.
    /// Samsung's Secure Folder and profiles managed by an enterprise
    /// policy respond this way, and yet the game is properly
    /// installed.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// The ADB shell does not have the right to reach this Android
    /// profile.
    ///
    /// Distinct from an ordinary permission refusal, because the
    /// remedies have nothing in common. Two causes, both observed in
    /// the field: Samsung's secure folder, which must be unlocked
    /// first, and the "Débogage USB (paramètres de sécurité)" (USB
    /// debugging (Security settings)) setting that the Xiaomi, Oppo
    /// and Realme overlays require to install into another profile.
    /// </summary>
    ShellUserAccessDenied,

    /// <summary>
    /// The profile is paused. This is the normal state of a work
    /// profile whose switch is off, and the main function of Shelter
    /// and Island.
    /// </summary>
    ProfilePaused,

    /// <summary>
    /// The command did not respond within the allotted time.
    /// </summary>
    Timeout,

    /// <summary>The ADB executable is missing or could not start.</summary>
    AdbUnavailable,
}
