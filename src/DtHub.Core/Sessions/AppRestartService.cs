using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>Outcome of a short restart.</summary>
public enum AppRestartOutcome
{
    /// <summary>The game restarted on the same display.</summary>
    Restarted,

    /// <summary>
    /// The display is not known: the session must be reopened.
    /// </summary>
    NoDisplay,

    /// <summary>Android refused the launch.</summary>
    Failed,
}

/// <summary>
/// Result of a short restart, with the reason for a refusal.
/// </summary>
public sealed record AppRestartResult(AppRestartOutcome Outcome, string? UserMessage = null);

/// <summary>
/// Restarts a session's game without touching its window: forced
/// stop on the Android side, then a fresh launch on the same
/// display.
///
/// Reopening the session would make the window disappear and would
/// recreate the display, which has no reason to happen when only
/// the game needs to restart. Since the display is kept, the
/// resolution does not change: moving to another size tier
/// requires closing then reopening.
/// </summary>
public sealed class AppRestartService
{
    private readonly IAppLauncher _apps;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public AppRestartService(IAppLauncher apps, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(apps);

        _apps = apps;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
    }

    /// <summary>
    /// Delay left for the forced stop. It is not instantaneous:
    /// restarting too quickly would reopen the old instance.
    /// </summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Stops then restarts a session's game, on its display.
    /// </summary>
    public async Task<AppRestartResult> RestartAsync(
        ScrcpySession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.IsAlive || session.VirtualDisplayId is not { } display)
        {
            return new AppRestartResult(AppRestartOutcome.NoDisplay);
        }

        var target = session.Target;

        _ = await _apps.ForceStopAsync(target.Serial, target.UserId, target.PackageName, cancellationToken)
            .ConfigureAwait(false);

        await _delay(SettleDelay, cancellationToken).ConfigureAwait(false);

        var launch = await _apps.LaunchAsync(
            target.Serial,
            target.UserId,
            target.PackageName,
            target.LaunchComponent,
            display,
            cancellationToken).ConfigureAwait(false);

        return launch.Succeeded
            ? new AppRestartResult(AppRestartOutcome.Restarted)
            : new AppRestartResult(AppRestartOutcome.Failed, launch.UserMessage);
    }
}
