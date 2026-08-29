namespace DtHub.Core.Sessions;

/// <summary>
/// Ce qu'il faut pour ouvrir une session : un téléphone joignable, un profil
/// Android, une application.
/// </summary>
public sealed record LaunchTarget
{
    public required string DeviceId { get; init; }

    /// <summary>Numéro de série ADB à utiliser maintenant.</summary>
    public required string Serial { get; init; }

    public required int UserId { get; init; }

    public required string PackageName { get; init; }

    /// <summary>
    /// Composant mémorisé. Il est revalidé au lancement : une mise à jour du
    /// jeu peut renommer son activité principale.
    /// </summary>
    public string? LaunchComponent { get; init; }

    /// <summary>Nom affiché dans le titre de la fenêtre.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Clé stable, identique à celle de l'instance correspondante.</summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}
