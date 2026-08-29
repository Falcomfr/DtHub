using System.Collections.Concurrent;

using DtHub.Core.Sessions;
using DtHub.Core.Dependencies;
using DtHub.Core.Processes;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Ouvre, suit et ferme les sessions de mirroring. Ne connaît que les
/// processus qu'il a lui-même démarrés : les fenêtres scrcpy ouvertes par un
/// autre logiciel ne sont jamais touchées.
/// </summary>
public sealed class ScrcpySessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ScrcpySession> _sessions = new(StringComparer.Ordinal);

    private readonly IScrcpyLocator _scrcpy;
    private readonly Adb.IAdbLocator _adbLocator;
    private readonly IProcessLauncher _launcher;
    private readonly IAppLauncher _appLauncher;

    public ScrcpySessionManager(
        IScrcpyLocator scrcpy,
        Adb.IAdbLocator adbLocator,
        IProcessLauncher launcher,
        IAppLauncher appLauncher)
    {
        _scrcpy = scrcpy;
        _adbLocator = adbLocator;
        _launcher = launcher;
        _appLauncher = appLauncher;
    }

    /// <summary>
    /// Délai laissé à scrcpy pour créer son afficheur virtuel. Un téléphone
    /// endormi ou chargé met plusieurs secondes.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sessions connues, vivantes ou terminées.</summary>
    public IReadOnlyCollection<ScrcpySession> Sessions => [.. _sessions.Values];

    /// <summary>
    /// Rang d'une session dans l'ordre voulu par l'utilisateur. Tant qu'il
    /// n'est pas fourni, les sessions restent dans leur ordre de démarrage.
    /// </summary>
    public Func<ScrcpySession, int>? OrderKey { get; set; }

    /// <summary>
    /// Appelé quand l'afficheur existe, juste avant d'ouvrir l'application.
    ///
    /// C'est le moment de donner à la fenêtre sa taille définitive : le jeu
    /// fixe son échelle et sa mise en page à son ouverture, et ne les revoit
    /// pas toujours si on redimensionne pendant qu'il démarre. L'image se
    /// retrouve alors coupée.
    /// </summary>
    public Func<ScrcpySession, CancellationToken, Task>? PrepareWindow { get; set; }

    /// <summary>
    /// Sessions encore ouvertes, dans l'ordre configuré, ou dans leur ordre de
    /// démarrage à défaut.
    ///
    /// L'ordre de démarrage ne convenait pas seul : relancer une instance lui
    /// donnait un nouvel horodatage et la renvoyait en fin de cycle clavier,
    /// alors qu'elle n'avait pas changé de place à l'écran.
    /// </summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions =>
        OrderKey is { } rank
            ? [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(rank).ThenBy(s => s.StartedUtc)]
            : [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(s => s.StartedUtc)];

    /// <summary>Signalé à chaque changement d'état d'une session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged;

    /// <summary>
    /// Ouvre une session pour une cible. La méthode rend la main dès que la
    /// session est utilisable ou définitivement en échec ; elle ne se met
    /// jamais à attendre indéfiniment.
    /// </summary>
    public async Task<ScrcpySession> StartAsync(
        LaunchTarget target,
        ScrcpyOptions options,
        ScrcpyWindowPlacement? placement = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        var serial = target.Serial;

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var windowTitle = ScrcpyCommandBuilder.BuildWindowTitle(target.DisplayName, options.WindowTitleHint);

        string scrcpyPath;
        string adbPath;

        try
        {
            scrcpyPath = await _scrcpy.GetScrcpyPathAsync(cancellationToken).ConfigureAwait(false);
            adbPath = await _adbLocator.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DependencyProvisioningException exception)
        {
            return FailedSession(sessionId, target, windowTitle, exception.Message);
        }
        catch (Adb.AdbException exception)
        {
            return FailedSession(sessionId, target, windowTitle, exception.UserMessage);
        }

        var request = new ProcessRequest
        {
            FileName = scrcpyPath,
            Arguments = ScrcpyCommandBuilder.BuildMirrorArguments(serial, windowTitle, options, placement),

            // scrcpy honore cette variable : il utilisera notre copie d'ADB
            // plutôt que celle livrée dans sa propre archive.
            Environment = new Dictionary<string, string?>
            {
                ["ADB"] = adbPath,

                // Les fenêtres de jeu portent l'icône de l'application, et non
                // celle de scrcpy.
                ["SCRCPY_ICON_DIR"] = options.IconDirectory,
            },
        };

        IProcessSession process;
        try
        {
            process = _launcher.Start(request);
        }
        catch (ProcessLaunchException exception)
        {
            return FailedSession(
                sessionId, target, windowTitle,
                "scrcpy n'a pas pu démarrer. Le détail est dans les journaux.",
                exception.Message);
        }

        var session = new ScrcpySession(sessionId, target, windowTitle, process)
        {
            // L'afficheur garde une définition fixe : la fenêtre est calculée
            // à son rapport, sur sa zone client.
            SourceAspectRatio = options is { UseVirtualDisplay: true, FlexDisplay: false, VirtualDisplayHeight: > 0 }
                ? (double)options.VirtualDisplayWidth / options.VirtualDisplayHeight
                : 0,

        };

        session.CommandLine = request.ToDisplayString();
        _sessions[sessionId] = session;

        var displayReady = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => PumpAsync(session, displayReady, options.UseVirtualDisplay), CancellationToken.None);

        await CompleteStartupAsync(session, options, displayReady, cancellationToken).ConfigureAwait(false);

        return session;
    }

    /// <summary>Ferme une session ouverte par DT Hub.</summary>
    public async Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        session.Process.Kill();

        try
        {
            await session.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // L'appelant renonce à attendre ; le processus a reçu l'ordre.
        }

        Transition(session, ScrcpySessionState.Stopped);
    }

    /// <summary>
    /// Ferme toutes les sessions ouvertes par DT Hub, et elles seules. Les
    /// fenêtres scrcpy lancées par un autre logiciel sont ignorées.
    /// </summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var running = _sessions.Values.Where(s => s.IsAlive).Select(s => s.Id).ToList();

        foreach (var id in running)
        {
            await StopAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Retire de la liste les sessions terminées.</summary>
    public void PruneFinished()
    {
        foreach (var session in _sessions.Values.Where(s => !s.IsAlive).ToList())
        {
            _sessions.TryRemove(session.Id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.Process.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
    }

    /// <summary>
    /// Attend que la session soit utilisable : afficheur créé puis application
    /// ouverte, ou échec. Sans afficheur virtuel, scrcpy montre l'écran du
    /// téléphone et il n'y a rien à lancer.
    /// </summary>
    private async Task CompleteStartupAsync(
        ScrcpySession session,
        ScrcpyOptions options,
        TaskCompletionSource<int?> displayReady,
        CancellationToken cancellationToken)
    {
        if (!options.UseVirtualDisplay)
        {
            Transition(session, ScrcpySessionState.Running);
            return;
        }

        int? displayId;
        try
        {
            displayId = await displayReady.Task
                .WaitAsync(StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Fail(session, "Le téléphone n'a pas ouvert d'écran virtuel à temps.");
            session.Process.Kill();
            return;
        }

        if (displayId is null)
        {
            Fail(session, session.FailureMessage ?? "La session de mirroring n'a pas pu s'ouvrir.");
            return;
        }

        session.VirtualDisplayId = displayId;

        // La fenêtre prend sa taille définitive avant que le jeu n'arrive :
        // il fixe son échelle à l'ouverture et ne la revoit pas toujours si on
        // redimensionne pendant son démarrage.
        if (PrepareWindow is { } prepare)
        {
            try
            {
                await prepare(session, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                session.Record($"Placement préalable impossible : {exception.Message}");
            }
        }

        var launch = await _appLauncher.LaunchAsync(
            session.Serial,
            session.Target.UserId,
            session.Target.PackageName,
            session.Target.LaunchComponent,
            displayId,
            cancellationToken).ConfigureAwait(false);

        if (!launch.Succeeded)
        {
            // Sans application, la fenêtre resterait vide : on ferme plutôt que
            // de laisser un écran noir sans explication.
            Fail(session, launch.UserMessage ?? "L'application n'a pas pu être ouverte.");
            session.Process.Kill();
            return;
        }

        Transition(session, ScrcpySessionState.Running);
    }

    /// <summary>
    /// Lit la sortie de scrcpy jusqu'à la fin du processus : identifiant
    /// d'afficheur, erreurs, puis état final.
    /// </summary>
    private async Task PumpAsync(
        ScrcpySession session,
        TaskCompletionSource<int?> displayReady,
        bool expectVirtualDisplay)
    {
        try
        {
            await foreach (var line in session.Process.Output.ReadAllAsync().ConfigureAwait(false))
            {
                session.Record(line.Text);

                if (expectVirtualDisplay
                    && !displayReady.Task.IsCompleted
                    && ScrcpyOutputParser.TryParseVirtualDisplayId(line.Text) is { } displayId)
                {
                    displayReady.TrySetResult(displayId);
                    continue;
                }

                if (ScrcpyOutputParser.IsError(line.Text))
                {
                    session.FailureMessage ??= ScrcpyOutputParser.DescribeError(line.Text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Le canal s'est refermé pendant la lecture, rien à signaler.
        }

        // La sortie est close : le processus est terminé ou l'a été.
        displayReady.TrySetResult(null);

        var exitCode = session.Process.ExitCode ?? -1;

        if (session.State == ScrcpySessionState.Failed)
        {
            SessionChanged?.Invoke(this, session);
            return;
        }

        if (exitCode == 0 || session.FailureMessage is null)
        {
            Transition(session, ScrcpySessionState.Stopped);
        }
        else
        {
            Fail(session, session.FailureMessage);
        }
    }

    private ScrcpySession FailedSession(
        string sessionId,
        LaunchTarget target,
        string windowTitle,
        string message,
        string? details = null)
    {
        var session = new ScrcpySession(sessionId, target, windowTitle, NullProcessSession.Instance)
        {
            State = ScrcpySessionState.Failed,
            FailureMessage = details is null ? message : $"{message} ({details})",
        };

        _sessions[sessionId] = session;
        SessionChanged?.Invoke(this, session);

        return session;
    }

    private void Fail(ScrcpySession session, string? message)
    {
        session.FailureMessage = message ?? "La session de mirroring s'est interrompue.";
        Transition(session, ScrcpySessionState.Failed);
    }

    private void Transition(ScrcpySession session, ScrcpySessionState state)
    {
        if (session.State == state)
        {
            return;
        }

        session.State = state;
        SessionChanged?.Invoke(this, session);
    }
}
