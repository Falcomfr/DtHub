namespace DtHub.Core.Apps;

/// <summary>Issue d'une tentative d'ouverture d'application.</summary>
public sealed record AppLaunchResult(bool Succeeded, string? UserMessage = null, string? Details = null)
{
    public static readonly AppLaunchResult Success = new(true);

    public static AppLaunchResult Failure(string userMessage, string? details = null) =>
        new(false, userMessage, details);
}

/// <summary>
/// Ouvre une application sur un utilisateur Android et un afficheur donnés.
/// C'est la pièce qui rend inutile toute modification de scrcpy : le lancement
/// passe par ADB, qui accepte <c>--user</c> depuis toujours.
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
}
