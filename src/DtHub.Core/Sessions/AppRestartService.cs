using DtHub.Core.Scrcpy;

namespace DtHub.Core.Sessions;

/// <summary>Issue d'une relance courte.</summary>
public enum AppRestartOutcome
{
    /// <summary>Le jeu est reparti sur le même afficheur.</summary>
    Restarted,

    /// <summary>L'afficheur n'est pas connu : il faut rouvrir la session.</summary>
    NoDisplay,

    /// <summary>Android a refusé le démarrage.</summary>
    Failed,
}

/// <summary>Résultat d'une relance courte, avec le motif d'un refus.</summary>
public sealed record AppRestartResult(AppRestartOutcome Outcome, string? UserMessage = null);

/// <summary>
/// Relance le jeu d'une session sans toucher à sa fenêtre : arrêt forcé côté
/// Android, puis nouveau démarrage sur le même afficheur.
///
/// Rouvrir la session ferait disparaître la fenêtre et recréerait l'afficheur,
/// ce qui n'a pas lieu d'être quand seul le jeu doit repartir. L'afficheur
/// étant conservé, la définition ne change pas : passer à un autre palier
/// demande de fermer puis de rouvrir.
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
    /// Délai laissé à l'arrêt forcé. Il n'est pas instantané : redémarrer trop
    /// vite rouvrirait l'ancienne instance.
    /// </summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMilliseconds(600);

    /// <summary>Arrête puis redémarre le jeu d'une session, sur son afficheur.</summary>
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

        await _apps.ForceStopAsync(target.Serial, target.UserId, target.PackageName, cancellationToken)
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
