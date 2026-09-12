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

    /// <summary>
    /// Force l'arrêt de l'application sur un profil.
    ///
    /// Rend vrai quand l'ordre est bien arrivé au téléphone, faux quand il n'a
    /// pas pu partir. La nuance décide de ce qu'on peut affirmer : sans elle,
    /// une fermeture qui laisse le jeu tourner ressemble trait pour trait à
    /// une fermeture réussie.
    /// </summary>
    Task<bool> ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default);

}
