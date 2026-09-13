using DtHub.Core.Localization;

namespace DtHub.Core.Adb;

/// <summary>
/// Translates ADB output into a usable error. Pure function, so
/// testable line by line from real output collected in the field.
/// </summary>
public static class AdbErrorInterpreter
{
    /// <summary>
    /// Determines the nature of a failure from ADB's raw output.
    /// Returns <c>null</c> when nothing indicates a known error.
    /// </summary>
    public static AdbErrorKind? Classify(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var text = output.ToLowerInvariant();

        if (Contains(text, "device unauthorized") || Contains(text, "device still authorizing"))
        {
            return AdbErrorKind.DeviceUnauthorized;
        }

        if (Contains(text, "device offline") || Contains(text, "device is offline"))
        {
            return AdbErrorKind.DeviceOffline;
        }

        if (Contains(text, "more than one device") || Contains(text, "multiple devices"))
        {
            return AdbErrorKind.AmbiguousDevice;
        }

        if (Contains(text, "not found") && Contains(text, "device"))
        {
            return AdbErrorKind.DeviceNotFound;
        }

        if (Contains(text, "no devices/emulators found") || Contains(text, "no devices found"))
        {
            return AdbErrorKind.DeviceNotFound;
        }

        if (Contains(text, "failed to connect") || Contains(text, "connection refused")
            || Contains(text, "cannot connect to"))
        {
            return AdbErrorKind.ConnectionFailed;
        }

        if (Contains(text, "failed to pair") || Contains(text, "wrong password")
            || Contains(text, "pairing failed"))
        {
            return AdbErrorKind.PairingFailed;
        }

        // A paused profile is recognized before everything else: it
        // is the ordinary state of Shelter and Island, and the phone
        // then responds with things that look like a missing
        // application.
        if (Contains(text, "quiet mode") || Contains(text, "user is paused"))
        {
            return AdbErrorKind.ProfilePaused;
        }

        // Before the generic refusal, and the order matters above
        // all: the shell that cannot reach a profile also returns a
        // SecurityException, but the remedies have nothing to do
        // with each other. Recorded character for character on a
        // real device: "Exception occurred while executing
        // 'install-existing' : java.lang.SecurityException: Shell
        // does not have permission to access user 150". User 150 is
        // the Samsung secure folder, which must be unlocked first.
        // Filed under permission refusal, the message talked about
        // enterprise profiles and sent looking elsewhere.
        if (Contains(text, "does not have permission to access user")
            || Contains(text, "shell does not have permission"))
        {
            return AdbErrorKind.ShellUserAccessDenied;
        }

        // A permission refusal says nothing about the installation.
        // Confusing it with a missing application used to send a
        // reinstall of a game that was already present, which is
        // what the Samsung Secure Folder causes.
        if (Contains(text, "permission denial") || Contains(text, "securityexception")
            || Contains(text, "permission denied"))
        {
            return AdbErrorKind.PermissionDenied;
        }

        if (Contains(text, "unknown package") || Contains(text, "package not found")
            || Contains(text, "does not exist for user"))
        {
            return AdbErrorKind.PackageNotFound;
        }

        if (Contains(text, "bad user number") || Contains(text, "user does not exist")
            || Contains(text, "user is not running") || Contains(text, "no such user"))
        {
            return AdbErrorKind.UserNotAvailable;
        }

        return null;
    }

    /// <summary>Short, actionable message, meant for the interface.</summary>
    public static string Describe(AdbErrorKind kind, string? deviceName = null)
    {
        var device = string.IsNullOrWhiteSpace(deviceName) ? Strings.Get("ThePhone") : deviceName;

        return kind switch
        {
            AdbErrorKind.DeviceNotFound => Strings.Format("AdbDeviceNotFound", device),
            AdbErrorKind.DeviceOffline => Strings.Format("AdbDeviceOffline", device),
            AdbErrorKind.DeviceUnauthorized => Strings.Format("AdbDeviceUnauthorized", device),
            AdbErrorKind.AmbiguousDevice => Strings.Get("AdbAmbiguousDevice"),
            AdbErrorKind.ConnectionFailed => Strings.Format("AdbConnectionFailed", device),
            AdbErrorKind.PairingFailed => Strings.Get("AdbPairingFailed"),
            AdbErrorKind.PackageNotFound => Strings.Get("AdbPackageNotFound"),
            AdbErrorKind.UserNotAvailable => Strings.Get("AdbUserNotAvailable"),
            AdbErrorKind.PermissionDenied => Strings.Get("AdbPermissionDenied"),
            AdbErrorKind.ShellUserAccessDenied => Strings.Get("AdbShellUserAccessDenied"),
            AdbErrorKind.ProfilePaused => Strings.Get("AdbProfilePaused"),
            AdbErrorKind.Timeout => Strings.Format("AdbTimeout", device),
            AdbErrorKind.AdbUnavailable => Strings.Get("AdbUnavailable"),
            _ => Strings.Get("AdbUnknownError"),
        };
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.Ordinal);
}
