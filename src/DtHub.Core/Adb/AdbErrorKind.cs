namespace DtHub.Core.Adb;

/// <summary>
/// Familles d'échecs ADB, telles qu'un utilisateur peut les comprendre. Sert à
/// afficher un message utile et à décider si une action de rattrapage est
/// possible, sans jamais montrer la sortie brute d'ADB dans l'interface.
/// </summary>
public enum AdbErrorKind
{
    Unknown = 0,

    /// <summary>Aucun appareil ne correspond au numéro de série demandé.</summary>
    DeviceNotFound,

    /// <summary>L'appareil est connu mais ne répond pas.</summary>
    DeviceOffline,

    /// <summary>L'autorisation de débogage n'a pas été accordée sur le téléphone.</summary>
    DeviceUnauthorized,

    /// <summary>Plusieurs appareils sont branchés et aucun n'a été désigné.</summary>
    AmbiguousDevice,

    /// <summary>La connexion réseau vers l'appareil a échoué.</summary>
    ConnectionFailed,

    /// <summary>Le code d'appairage a été refusé ou a expiré.</summary>
    PairingFailed,

    /// <summary>Le paquet demandé n'existe pas pour cet utilisateur Android.</summary>
    PackageNotFound,

    /// <summary>L'utilisateur Android visé n'existe pas ou n'est pas démarré.</summary>
    UserNotAvailable,

    /// <summary>
    /// Le téléphone a refusé l'opération faute de permission. Le Dossier
    /// sécurisé de Samsung et les profils tenus par une politique
    /// d'entreprise répondent ainsi, et le jeu est pourtant bien installé.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// Le profil est en pause. C'est l'état normal d'un profil professionnel
    /// dont l'interrupteur est éteint, et la fonction principale de Shelter et
    /// d'Island.
    /// </summary>
    ProfilePaused,

    /// <summary>La commande n'a pas répondu dans le délai imparti.</summary>
    Timeout,

    /// <summary>L'exécutable ADB est absent ou n'a pas pu démarrer.</summary>
    AdbUnavailable,
}
