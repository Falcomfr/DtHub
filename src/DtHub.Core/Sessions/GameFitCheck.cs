using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>What the check found.</summary>
public enum GameFitOutcome
{
    /// <summary>Bounds unreadable, or the window gone: nothing was done.</summary>
    Unknown,

    /// <summary>The game covers its whole display.</summary>
    Fills,

    /// <summary>It did not, and was relaunched once.</summary>
    Relaunched,
}

/// <summary>
/// Bounds read before and, after a relaunch, after it.
/// </summary>
public sealed record GameFitResult(
    GameFitOutcome Outcome,
    (int Left, int Top, int Right, int Bottom)? Before = null,
    (int Left, int Top, int Right, int Bottom)? After = null,
    string? State = null);

/// <summary>
/// Checks that the game covers its virtual display, and relaunches it once
/// on the same display when it does not.
///
/// The game declares it does not support size changes. If its display
/// changes after it opened, Android keeps it at its old size, moved inside
/// the display: seen on 14/10/2026 on a Galaxy Tab S10 Ultra (Android 16),
/// a black band on the left and the panel cut on the right. Reproduced on
/// the 13T Pro by opening the game in a smaller window then enlarging it.
/// "--windowingMode 1" asks for full screen, and nothing said whether the
/// system granted it. A relaunch on a display that no longer changes
/// gives the game its real size, whatever the manufacturer did first.
/// </summary>
public sealed class GameFitCheck
{
    private readonly IAppLauncher _apps;
    private readonly AppRestartService _restarts;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public GameFitCheck(
        IAppLauncher apps,
        AppRestartService restarts,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(restarts);

        _apps = apps;
        _restarts = restarts;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>
    /// Time left to the system before reading, after each launch. A
    /// manufacturer that resizes the game does it while it opens.
    /// </summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<GameFitResult> CheckAsync(
        ScrcpySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _delay(SettleDelay, cancellationToken).ConfigureAwait(false);

        var first = await ReadAsync(session, cancellationToken).ConfigureAwait(false);
        var before = first?.Bounds;

        if (before is not { } bounds || session.VirtualDisplaySize is not { } size)
        {
            return new GameFitResult(GameFitOutcome.Unknown, before, State: first?.State);
        }

        // Either way round: a display that turns with its content gives the
        // game its sides swapped, and that is not a misfit.
        if (bounds == (0, 0, size.Width, size.Height) || bounds == (0, 0, size.Height, size.Width))
        {
            return new GameFitResult(GameFitOutcome.Fills, before, State: first?.State);
        }

        var restart = await _restarts.RestartAsync(session, cancellationToken).ConfigureAwait(false);

        if (restart.Outcome != AppRestartOutcome.Restarted)
        {
            return new GameFitResult(GameFitOutcome.Unknown, before, State: first?.State);
        }

        await _delay(SettleDelay, cancellationToken).ConfigureAwait(false);

        var second = await ReadAsync(session, cancellationToken).ConfigureAwait(false);

        return new GameFitResult(GameFitOutcome.Relaunched, before, second?.Bounds, second?.State ?? first?.State);
    }

    /// <summary>Nothing to read once the window is gone.</summary>
    private async Task<GameWindow?> ReadAsync(ScrcpySession session, CancellationToken cancellationToken) =>
        session.IsAlive && session.VirtualDisplayId is { } display
            ? await _apps.ReadGameWindowAsync(session.Serial, display, session.Target.PackageName, cancellationToken)
                .ConfigureAwait(false)
            : null;
}
