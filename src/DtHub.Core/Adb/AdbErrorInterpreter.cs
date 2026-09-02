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
        var device = string.IsNullOrWhiteSpace(deviceName) ? "Le téléphone" : deviceName;

        return kind switch
        {
            AdbErrorKind.DeviceNotFound =>
                $"{device} n'est plus détecté. Vérifiez le câble ou la connexion Wi-Fi.",
            AdbErrorKind.DeviceOffline =>
                $"{device} ne répond plus. Réveillez l'écran, puis reconnectez-le.",
            AdbErrorKind.DeviceUnauthorized =>
                $"{device} n'a pas encore autorisé ce PC. Déverrouillez l'écran et acceptez la demande de débogage.",
            AdbErrorKind.AmbiguousDevice =>
                "Plusieurs appareils sont connectés et aucun n'a été choisi.",
            AdbErrorKind.ConnectionFailed =>
                $"Impossible de joindre {device}. Vérifiez qu'il est sur le même réseau et que le débogage sans fil est actif.",
            AdbErrorKind.PairingFailed =>
                "L'appairage a échoué. Le code est peut-être expiré : relancez l'association sur le téléphone pour en obtenir un nouveau.",
            AdbErrorKind.PackageNotFound =>
                "L'application n'est plus installée pour ce profil Android.",
            AdbErrorKind.UserNotAvailable =>
                "Ce profil Android n'est pas disponible. Ouvrez-le une fois sur le téléphone, puis réessayez.",
            AdbErrorKind.PermissionDenied =>
                "Le téléphone a refusé l'accès à ce profil. Les dossiers sécurisés et les profils "
                + "gérés par une entreprise n'autorisent pas le lancement depuis un PC.",
            AdbErrorKind.ProfilePaused =>
                "Ce profil est en pause. Réactivez-le sur le téléphone, depuis son interrupteur "
                + "ou depuis l'application qui le gère, puis réessayez.",
            AdbErrorKind.Timeout =>
                $"{device} n'a pas répondu à temps.",
            AdbErrorKind.AdbUnavailable =>
                "Les outils Android n'ont pas pu démarrer. Consultez le diagnostic dans les paramètres.",
            _ => "Une erreur est survenue lors de la communication avec le téléphone.",
        };
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.Ordinal);
}
