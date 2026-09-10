using DtHub.Core.Localization;

namespace DtHub.Core.Adb;

/// <summary>
/// Traduit la sortie d'ADB en une erreur exploitable. Fonction pure, donc
/// testable ligne par ligne à partir de sorties réelles relevées sur le
/// terrain.
/// </summary>
public static class AdbErrorInterpreter
{
    /// <summary>
    /// Détermine la nature d'un échec à partir de la sortie brute d'ADB.
    /// Retourne <c>null</c> lorsque rien n'indique une erreur connue.
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

        // Un profil en pause se reconnaît avant tout le reste : c'est l'état
        // ordinaire de Shelter et d'Island, et le téléphone répond alors des
        // choses qui ressemblent à une application manquante.
        if (Contains(text, "quiet mode") || Contains(text, "user is paused"))
        {
            return AdbErrorKind.ProfilePaused;
        }

        // Avant le refus générique, et l'ordre est tout : le shell qui n'atteint
        // pas un profil rend lui aussi une SecurityException, mais les remèdes
        // n'ont rien à voir. Relevé au caractère près sur un vrai poste :
        // « Exception occurred while executing 'install-existing' :
        // java.lang.SecurityException: Shell does not have permission to access
        // user 150 ». L'utilisateur 150 est le dossier sécurisé Samsung, qu'il
        // faut déverrouiller avant. Rangé en refus de permission, le message
        // parlait de profils d'entreprise et envoyait chercher ailleurs.
        if (Contains(text, "does not have permission to access user")
            || Contains(text, "shell does not have permission"))
        {
            return AdbErrorKind.ShellUserAccessDenied;
        }

        // Un refus de permission ne dit rien de l'installation. Le confondre
        // avec une application absente envoyait réinstaller un jeu bien
        // présent, ce que rend le Dossier sécurisé de Samsung.
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

    /// <summary>Message court et actionnable, destiné à l'interface.</summary>
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
