namespace DtHub.Core.Sessions;

/// <summary>Issue d'une tentative d'ouverture du jeu.</summary>
public sealed record AppLaunchResult(bool Succeeded, string? UserMessage = null, string? Details = null)
{
    public static readonly AppLaunchResult Success = new(true);

    public static AppLaunchResult Failure(string userMessage, string? details = null) =>
        new(false, userMessage, details);
}

/// <summary>
/// Ouvre une application sur un profil Android et un afficheur donnés. C'est
/// cette pièce qui rend inutile toute modification de scrcpy : le lancement
/// passe par ADB, qui accepte <c>--user</c>.
/// </summary>
public interface IAppLauncher
{
    Task<AppLaunchResult> LaunchAsync(
        string serial,
        int userId,
        string packageName,
        string? knownComponent,
        int? displayId,
        CancellationToken cancellationToken = default);

    /// <summary>Force l'arrêt de l'application sur un profil, avant relance.</summary>
    Task ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default);
}
