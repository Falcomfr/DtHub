namespace DtHub.Core.Sessions;

/// <summary>Outcome of an attempt to open the game.</summary>
public sealed record AppLaunchResult(bool Succeeded, string? UserMessage = null, string? Details = null)
{
    public static readonly AppLaunchResult Success = new(true);

    public static AppLaunchResult Failure(string userMessage, string? details = null) =>
        new(false, userMessage, details);
}

/// <summary>
/// Android's game modes for a game: the one in use, when known, and those
/// a player can choose, "standard" first when offered.
/// </summary>
public sealed record GameModes(string? Current, IReadOnlyList<string> Available);

/// <summary>
/// Opens an application on a given Android profile and display.
/// This is the piece that makes any modification to scrcpy
/// unnecessary: the launch goes through ADB, which accepts
/// <c>--user</c>.
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
    /// Forces the application to stop on a profile.
    ///
    /// Returns true when the order actually reached the phone, false
    /// when it could not be sent. This nuance decides what can be
    /// claimed: without it, a shutdown that leaves the game running
    /// looks exactly like a successful shutdown.
    /// </summary>
    Task<AppLaunchResult> OpenUriAsync(
        string serial,
        int userId,
        string uri,
        int? displayId,
        CancellationToken cancellationToken = default);

    Task<bool> ForceStopAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the phone reports of the game's window on a display, in one
    /// readable line for the diagnostic report.
    /// </summary>
    Task<string> ReadGameWindowAsync(
        string serial,
        int displayId,
        string packageName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the game in one of Android's game modes on a profile, and says
    /// whether the phone accepted. Takes effect at the next start.
    /// </summary>
    Task<bool> SetGameModeAsync(
        string serial,
        int userId,
        string packageName,
        string mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The game modes the phone offers for the game, or <c>null</c> when it
    /// has none to offer (before Android 13, or no answer).
    /// </summary>
    Task<GameModes?> ReadGameModesAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default);
}
