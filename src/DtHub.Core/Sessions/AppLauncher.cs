namespace DtHub.Core.Sessions;

/// <summary>
/// The bounds Android gave the game, <c>null</c> when unreadable, and a
/// readable excerpt of what the phone reported, for the diagnostic report.
/// </summary>
public sealed record GameWindow((int Left, int Top, int Right, int Bottom)? Bounds, string State);

/// <summary>Outcome of an attempt to open the game.</summary>
public sealed record AppLaunchResult(bool Succeeded, string? UserMessage = null, string? Details = null)
{
    public static readonly AppLaunchResult Success = new(true);

    public static AppLaunchResult Failure(string userMessage, string? details = null) =>
        new(false, userMessage, details);
}

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
    /// The game's window on a display, as Android reports it. Asking for
    /// full screen is not getting it.
    /// </summary>
    Task<GameWindow> ReadGameWindowAsync(
        string serial,
        int displayId,
        string packageName,
        CancellationToken cancellationToken = default);
}
