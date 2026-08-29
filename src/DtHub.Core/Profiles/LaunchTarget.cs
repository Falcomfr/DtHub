namespace DtHub.Core.Profiles;

/// <summary>
/// Une session à ouvrir : un appareil, un utilisateur Android, une
/// application. C'est l'unité que manipule tout le reste de l'application.
/// </summary>
public sealed record LaunchTarget
{
    /// <summary>Identité stable de l'appareil, pas son numéro de série ADB.</summary>
    public required string DeviceId { get; init; }

    /// <summary>Utilisateur Android. N'importe quel entier positif est valide.</summary>
    public required int UserId { get; init; }

    public required string PackageName { get; init; }

    /// <summary>
    /// Composant mémorisé lors de la découverte. Il est revalidé au
    /// lancement : une mise à jour de l'application peut renommer son activité
    /// principale.
    /// </summary>
    public string? LaunchComponent { get; init; }

    /// <summary>Nom affiché au moment de la constitution du profil.</summary>
    public string? AppLabel { get; init; }

    /// <summary>Nom de l'appareil au moment de la constitution du profil.</summary>
    public string? DeviceLabel { get; init; }

    /// <summary>Nom du profil Android au moment de la constitution du profil.</summary>
    public string? UserLabel { get; init; }

    /// <summary>Clé stable d'une cible, utilisée pour les comparaisons et l'unicité.</summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";

    /// <summary>Libellé complet, du type « Xiaomi 13T Pro - DOFUS Touch - Clone ».</summary>
    public string DisplayName
    {
        get
        {
            var app = AppLabel ?? Apps.AndroidApp.HumanizePackageName(PackageName);
            var device = DeviceLabel ?? DeviceId;

            return string.IsNullOrWhiteSpace(UserLabel)
                ? $"{device} - {app}"
                : $"{device} - {app} - {UserLabel}";
        }
    }
}
