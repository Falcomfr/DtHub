using System.Collections.Concurrent;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Core.Processes;
using DtHub.Core.Sessions;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Opens, tracks, and closes mirroring sessions. It only knows the
/// processes it started itself: scrcpy windows opened by other
/// software are never touched.
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
    /// Time given to scrcpy to create its virtual display. A phone
    /// that is asleep or under load takes several seconds.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Known sessions, alive or finished.</summary>
    public IReadOnlyCollection<ScrcpySession> Sessions => [.. _sessions.Values];

    /// <summary>
    /// A session's rank in the order the user wants. As long as it
    /// is not provided, sessions stay in their startup order.
    /// </summary>
    public Func<ScrcpySession, int>? OrderKey { get; set; }

    /// <summary>
    /// Called when the display exists, just before opening the
    /// application.
    ///
    /// This is the moment to give the window its final size: the
    /// game fixes its scale and layout when it opens, and does not
    /// always revisit them if the window is resized while it is
    /// starting. The image then ends up cropped.
    /// </summary>
    public Func<ScrcpySession, ScrcpyWindowPlacement?, CancellationToken, Task>? PrepareWindow { get; set; }

    /// <summary>
    /// Asks an scrcpy window to close itself. As long as it is not
    /// provided, shutdown happens through <c>Kill</c>.
    /// </summary>
    public Action<ScrcpySession>? RequestClose { get; set; }

    /// <summary>
    /// Time given to scrcpy to leave on its own before being killed.
    ///
    /// It must notify its server on the phone, which requires a
    /// one-way trip over the link. A second and a half largely
    /// covers an ordinary Wi-Fi link, without making the close wait.
    /// </summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// True if closing a window should also stop the game on the
    /// phone.
    ///
    /// Without this the game survives its window. Its virtual
    /// display is returned, but the application itself keeps running
    /// in the background: observed on the test machine, a game had
    /// been running for eighteen minutes with no window facing it,
    /// with two hundred twenty megabytes of its own, and had
    /// survived several closures of DT Hub. The character also stays
    /// connected to the game's servers.
    ///
    /// The cost is known and accepted: a window can no longer be
    /// closed and reopened while still in game. This is the setting
    /// that decides.
    /// </summary>
    public bool StopAppOnClose { get; set; }

    /// <summary>
    /// Time given to stop the game on the phone.
    ///
    /// Short, and explicit, because the default would not do: an ADB
    /// command waits twenty seconds, while the whole application
    /// shuts down in eight. Quitting with an unreachable phone would
    /// exceed the budget and freeze the shutdown. Two seconds largely
    /// cover the three to five tenths measured on an ordinary Wi-Fi
    /// link.
    /// </summary>
    public TimeSpan StopAppTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The ADB address a device currently carries, read from its
    /// stable identity. As long as it is not provided, we stick to
    /// the one from launch.
    ///
    /// **Without this, closing a window did not always close the
    /// game.** A session remembers the address the phone had when it
    /// opened, and wireless debugging changes port on every restart.
    /// Recorded in the log, two closures on the same day:
    ///
    /// <code>
    /// am force-stop … pour 192.168.1.23:41207 : adb.exe: device offline
    /// am force-stop … pour 192.168.1.23:42557 : device '…:42557' not found
    /// </code>
    ///
    /// The phone was there, reachable, under a third address. The
    /// command was heading to a dead address and the game stayed
    /// open.
    /// </summary>
    public Func<string, string?>? CurrentSerial { get; set; }

    /// <summary>
    /// Goes and looks up where a device is, instead of remembering.
    ///
    /// **Called only after a failure, and that is the whole point.**
    /// The two known addresses both come from the past: the one from
    /// the last scan, which lags by up to two seconds, and the one
    /// from launch. And two seconds is exactly the time it takes for
    /// a port to change without anyone seeing it.
    ///
    /// Measured on the phone, by manually expiring its address: the
    /// stop command left at 14:52:26 for the dead address, and the
    /// application knew the right one at 14:52:28. Two and a half
    /// seconds of delay, and a game that stays open. Asking again
    /// costs about sixty milliseconds, once only, and never on the
    /// path that works.
    /// </summary>
    public Func<string, CancellationToken, Task<string?>>? LookUpSerial { get; set; }

    /// <summary>
    /// Fired when the game could not be stopped on the phone even
    /// though it was requested. The window, on the other hand, did
    /// close: that is precisely what makes the failure invisible
    /// without this notice.
    /// </summary>
    public event EventHandler<ScrcpySession>? AppStopFailed;

    /// <summary>Only one opening at a time per phone.</summary>
    private readonly DeviceStartupGate _gate = new();

    /// <summary>
    /// Rest period left on a device after an opening. Zero by
    /// default: the lock already imposes the spacing of a full
    /// opening.
    /// </summary>
    public TimeSpan StartupCooldown
    {
        get => _gate.Cooldown;
        init => _gate.Cooldown = value;
    }

    /// <summary>True if an opening is in progress on this device.</summary>
    public bool IsDeviceBusy(string deviceId) => _gate.IsBusy(deviceId);

    /// <summary>Fired when a device becomes busy, or stops being so.</summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? DeviceBusyChanged
    {
        add => _gate.BusyChanged += value;
        remove => _gate.BusyChanged -= value;
    }

    /// <summary>
    /// Sessions still open, in the configured order, or in their
    /// startup order otherwise.
    ///
    /// The startup order alone would not do: relaunching an instance
    /// gave it a new timestamp and sent it to the end of the
    /// keyboard cycle, even though it had not changed place on
    /// screen.
    /// </summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions =>
        OrderKey is { } rank
            ? [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(rank).ThenBy(s => s.StartedUtc)]
            : [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(s => s.StartedUtc)];

    /// <summary>Fired on every change of a session's state.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged;

    /// <summary>
    /// Opens a session for a target. The method returns control as
    /// soon as the session is usable or has definitively failed; it
    /// never starts waiting indefinitely.
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

            // scrcpy honors this variable: it will use our copy of
            // ADB rather than the one shipped in its own archive.
            Environment = new Dictionary<string, string?>
            {
                ["ADB"] = adbPath,

                // Game windows carry the application's icon, not
                // scrcpy's.
                ["SCRCPY_ICON_DIR"] = options.IconDirectory,
            },
        };

        // Only one opening at a time per phone: two that overlap
        // break, with the first one dying on a connection to the
        // server it had nevertheless already pushed.
        //
        // The lock only covers the push, the connection, and the
        // display's creation. It used to also cover opening the game,
        // which blocked the other rows for nothing: "am force-stop"
        // and "am start" are ordinary commands, specific to an
        // Android profile, that do not touch the server pushed by
        // scrcpy. Measured on the reference phone, this held the
        // lock for 1148 ms instead of 683, and 2094 instead of 1310.
        //
        // A click during the wait joins the queue: refusing it would
        // force clicking again.
        var lease = await _gate
            .EnterAsync(target.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        IProcessSession process;
        try
        {
            process = _launcher.Start(request);
        }
        catch (ProcessLaunchException exception)
        {
            await lease.DisposeAsync().ConfigureAwait(false);

            return FailedSession(
                sessionId, target, windowTitle,
                Strings.Get("ScrcpyDidNotStart"),
                exception.Message);
        }

        var session = new ScrcpySession(sessionId, target, windowTitle, process)
        {
            // The display keeps a fixed resolution: the window is
            // computed to its ratio, on its client area.
            SourceAspectRatio = options is { UseVirtualDisplay: true, VirtualDisplayHeight: > 0 }
                ? (double)options.VirtualDisplayWidth / options.VirtualDisplayHeight
                : 0,

        };

        session.CommandLine = request.ToDisplayString();
        _sessions[sessionId] = session;

        var displayReady = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => PumpAsync(session, displayReady, options.UseVirtualDisplay), CancellationToken.None);

        var chrono = System.Diagnostics.Stopwatch.StartNew();

        int? displayId;
        try
        {
            displayId = await AwaitDisplayAsync(session, options, displayReady, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Returned here and not later: the display exists, the
            // next opening can push its server without risk.
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        if (displayId is not null)
        {
            await LaunchGameAsync(session, placement, displayId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        session.StartupMs = chrono.ElapsedMilliseconds;

        return session;
    }

    /// <summary>
    /// The video encoders the device declares, or an empty list if
    /// the question did not succeed.
    ///
    /// No window, no display: scrcpy pushes its server, queries, and
    /// exits. Nothing is propagated on failure, this is a piece of
    /// information, not a launch step.
    /// </summary>
    public async Task<IReadOnlyList<VideoEncoder>> ListEncodersAsync(
        string deviceId,
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return [];
        }

        try
        {
            var scrcpyPath = await _scrcpy.GetScrcpyPathAsync(cancellationToken).ConfigureAwait(false);
            var adbPath = await _adbLocator.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);

            var request = new ProcessRequest
            {
                FileName = scrcpyPath,
                Arguments = ScrcpyCommandBuilder.BuildListEncodersArguments(serial),
                Environment = new Dictionary<string, string?> { ["ADB"] = adbPath },
            };

            // The same queue as an opening, and for the same reason:
            // this pushes and starts a scrcpy server on the phone.
            //
            // **Measured on a real device**, Mi 9T Pro: started at the
            // same moment as a session that creates a virtual display,
            // the probe survives and the session dies with
            // "ERROR: Server connection failed", two times out of four.
            // Spaced by 1.5 s, both succeed, four times out of four.
            //
            // The probe is asked once per device and per run, right
            // before the loop that opens the windows, so what it used to
            // cost was precisely the first launch on each phone. The
            // second click worked, which is how this was reported.
            await using var lease = await _gate
                .EnterAsync(deviceId, cancellationToken)
                .ConfigureAwait(false);

            await using var process = _launcher.Start(request);

            var lines = new List<string>();

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(EncoderListTimeout);

            await foreach (var line in process.Output.ReadAllAsync(deadline.Token).ConfigureAwait(false))
            {
                lines.Add(line.Text);
            }

            return ScrcpyEncoders.Parse(string.Join('\n', lines));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A device that does not respond, scrcpy missing, a
            // timeout exceeded: not knowing the encoders is a valid
            // result, and the caller does without it. This is the
            // same accepted silence as for heat and battery.
            return [];
        }
    }

    /// <summary>
    /// Beyond this, we give up knowing the encoders. The question
    /// costs a server push, and it is never urgent.
    /// </summary>
    private static readonly TimeSpan EncoderListTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Closes a session opened by DT Hub.</summary>
    public async Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        // Said before anything else: the read loop will see the flag
        // when the channel closes, and will know this end was
        // intended.
        session.StopRequested = true;

        // The right to stop the game is claimed now, before even
        // touching scrcpy. Claiming it afterward would have let the
        // read loop, woken by the process's death, claim it first:
        // this method would then have returned control without
        // waiting for anything, and quitting the application could
        // have cut the stop short mid-flight.
        var mine = StopAppOnClose && session.ClaimAppStop();

        // scrcpy is asked to leave before being killed: it is the
        // one that notifies its server, and the server that returns
        // the virtual display. A client killed outright on a Wi-Fi
        // link used to leave the server alive on the phone, with its
        // display; the next window would then open on a gray screen,
        // the game having stayed on the abandoned display.
        await RequestCloseAsync(session, cancellationToken).ConfigureAwait(false);

        if (!session.Process.HasExited)
        {
            session.Process.Kill();

            try
            {
                await session.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The caller gives up waiting; the process has
                // received the order.
            }
        }

        Transition(session, ScrcpySessionState.Stopped);

        // After scrcpy has left, never before: it is the one that
        // notifies its server, and the server that returns the
        // virtual display. Awaited here, and not just left to the
        // end of reading the output, because quitting the
        // application does not leave it time to finish.
        if (mine)
        {
            await ForceStopGameAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stops the game on the phone, if the setting asks for it and
    /// if no one has already done so for this session.
    ///
    /// Nothing is propagated: a phone that is gone, a broken link, a
    /// profile the shell can no longer reach, none of these cases
    /// must prevent a window from closing or the application from
    /// stopping. A game that survives is an inconvenience; a
    /// shutdown that freezes is an outage.
    /// </summary>
    private async Task StopAppOnDeviceAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        if (!StopAppOnClose || !session.ClaimAppStop())
        {
            return;
        }

        await ForceStopGameAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the game, without asking whether it is the right
    /// moment: the caller has already claimed the right to do so.
    /// </summary>
    private async Task ForceStopGameAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        // What we did not open, we do not close: a session that
        // failed before launching anything would leave running a
        // game that someone might be playing on the phone.
        if (!session.AppLaunchedByUs)
        {
            return;
        }

        await ForceStopCoreAsync(session).ConfigureAwait(false);
    }

    /// <summary>
    /// The command itself, with its own timeout and nothing else.
    ///
    /// The caller's token is deliberately not passed on. On the
    /// closing path, it is precisely that token that gets canceled:
    /// binding it here would amount to giving up the stop at the
    /// exact moment it matters most, and letting the game keep
    /// running on the phone. Once decided, the command is sent; its
    /// own timeout is enough to bound the wait.
    /// </summary>
    private async Task ForceStopCoreAsync(ScrcpySession session)
    {
        using var deadline = new CancellationTokenSource(StopAppTimeout);

        var tried = new HashSet<string>(StringComparer.Ordinal);

        foreach (var serial in StopCandidates(session))
        {
            if (tried.Add(serial)
                && await AttemptStopAsync(session, serial, deadline.Token).ConfigureAwait(false))
            {
                return;
            }
        }

        // The known addresses have failed, and both come from the
        // past. We ask again where the device is, once, before
        // giving up.
        var fresh = await LookUpAsync(session.Target.DeviceId, deadline.Token).ConfigureAwait(false);

        if (fresh is { Length: > 0 }
            && tried.Add(fresh)
            && await AttemptStopAsync(session, fresh, deadline.Token).ConfigureAwait(false))
        {
            return;
        }

        AppStopFailed?.Invoke(this, session);
    }

    /// <summary>
    /// One stop attempt at an address. True if the command arrived.
    /// </summary>
    private async Task<bool> AttemptStopAsync(
        ScrcpySession session,
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            if (await _appLauncher.ForceStopAsync(
                    serial,
                    session.Target.UserId,
                    session.Target.PackageName,
                    cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            session.Record("Arrêt du jeu refusé par " + serial + ".");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing is propagated: the method is called on the
            // closing path, including that of the whole application,
            // and a fault there would have no one to receive it.
            session.Record("Arrêt du jeu sur le téléphone impossible : " + exception.Message);
        }

        return false;
    }

    /// <summary>The freshly looked-up address, or <c>null</c>.</summary>
    private async Task<string?> LookUpAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (LookUpSerial is not { } search)
        {
            return null;
        }

        try
        {
            return await search(deviceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The scan can fail or run out of time: this is a last
            // chance, not a step the closing depends on.
            return null;
        }
    }

    /// <summary>
    /// The addresses to try to reach the phone, the most recent
    /// first.
    ///
    /// Two at most, and most often just one, the two merging as long
    /// as the device has not changed address. The second one serves
    /// when the first is, in turn, outdated: a scan happens every two
    /// seconds, and can narrowly miss a port change.
    /// </summary>
    private IEnumerable<string> StopCandidates(ScrcpySession session)
    {
        var now = Resolve(session.Target.DeviceId);

        if (!string.IsNullOrWhiteSpace(now))
        {
            yield return now;
        }

        if (!string.IsNullOrWhiteSpace(session.Serial)
            && !string.Equals(now, session.Serial, StringComparison.Ordinal))
        {
            yield return session.Serial;
        }
    }

    /// <summary>
    /// The current address, or <c>null</c> when we do not have it.
    /// </summary>
    private string? Resolve(string deviceId)
    {
        if (CurrentSerial is not { } ask)
        {
            return null;
        }

        try
        {
            return ask(deviceId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Not knowing where the device is does not prevent trying
            // the launch address, which is often still the right one.
            return null;
        }
    }

    /// <summary>
    /// Closes all sessions opened by DT Hub, and only those. scrcpy
    /// windows launched by other software are ignored.
    /// </summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var running = _sessions.Values.Where(s => s.IsAlive).Select(s => s.Id).ToList();

        foreach (var id in running)
        {
            await StopAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Requests the close and waits, without exceeding the timeout.
    /// Returns control as soon as the process is gone, to not slow
    /// down the shutdown.
    /// </summary>
    private async Task RequestCloseAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        if (RequestClose is null || session.WindowHandle == 0 || session.Process.HasExited)
        {
            return;
        }

        RequestClose(session);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CloseTimeout);

        try
        {
            await session.Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Left too slowly, or the caller gives up: the Kill follows.
        }
    }

    /// <summary>Removes finished sessions from the list.</summary>
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
        _gate.Dispose();
    }

    /// <summary>
    /// Waits for the phone to open the virtual display. This is the
    /// only part of startup that must be serialized between two
    /// openings.
    /// </summary>
    /// <returns>The opened display, or null if the session failed.</returns>
    private async Task<int?> AwaitDisplayAsync(
        ScrcpySession session,
        ScrcpyOptions options,
        TaskCompletionSource<int?> displayReady,
        CancellationToken cancellationToken)
    {
        if (!options.UseVirtualDisplay)
        {
            Transition(session, ScrcpySessionState.Running);
            return null;
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
            if (session.FailureKind == ScrcpyFailureKind.None)
            {
                session.FailureKind = ScrcpyFailureKind.Timeout;
            }

            // Sound first, when it is requested. On certain Samsung
            // tablets, enabling it is enough to prevent the display
            // from opening: observed on a competing product that
            // takes the same path, where two users spent days making
            // the connection, one of them eventually writing "si je
            // désactive le son ça marche", if I turn the sound off it
            // works. The message only says so if sound is actually
            // requested, otherwise it would send people looking for a
            // cause we have already ruled out.
            Fail(
                session,
                Strings.Get(options.AudioEnabled
                    ? "ScrcpyDisplayTimeoutWithAudio"
                    : "ScrcpyDisplayTimeout"));
            session.Process.Kill();
            return null;
        }

        if (displayId is null)
        {
            Fail(session, session.FailureMessage ?? Strings.Get("ScrcpySessionFailed"));
            return null;
        }

        session.VirtualDisplayId = displayId;
        session.DisplayReadyMs = (long)(DateTimeOffset.UtcNow - session.StartedUtc).TotalMilliseconds;

        return displayId;
    }

    /// <summary>
    /// Places the window then opens the game on the display. Outside
    /// the lock: these commands are specific to an Android profile
    /// and do not touch the server pushed by scrcpy.
    /// </summary>
    private async Task LaunchGameAsync(
        ScrcpySession session,
        ScrcpyWindowPlacement? placement,
        int displayId,
        CancellationToken cancellationToken)
    {
        // The window takes its final size before the game arrives:
        // it fixes its scale on opening and does not always revisit
        // it if the window is resized while it is starting.
        if (PrepareWindow is { } prepare)
        {
            try
            {
                await prepare(session, placement, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                session.Record(Strings.Format("ScrcpyPrePlacementFailed", exception.Message));
            }
        }

        // The game is stopped before being reopened on the new
        // display.
        //
        // "am start --display" does not move an existing task: if
        // the game is already running, Android simply brings it to
        // the foreground wherever it is, and the new display stays
        // empty, hence the gray window. Stopping the task is the
        // only sure way to make it reborn in the right place;
        // opening a window restarts the game anyway.
        _ = await _appLauncher.ForceStopAsync(
            session.Serial,
            session.Target.UserId,
            session.Target.PackageName,
            cancellationToken).ConfigureAwait(false);

        var launch = await _appLauncher.LaunchAsync(
            session.Serial,
            session.Target.UserId,
            session.Target.PackageName,
            session.Target.LaunchComponent,
            displayId,
            cancellationToken).ConfigureAwait(false);

        // Set even when opening fails: "am start" may have launched
        // the game and still returned a fault, and when in doubt,
        // the game we leave running is indeed our own.
        session.AppLaunchedByUs = true;

        if (!launch.Succeeded)
        {
            // Without an application, the window would stay empty:
            // we close rather than leave a black screen with no
            // explanation.
            Fail(session, launch.UserMessage ?? Strings.Get("ScrcpyAppNotOpened"));
            session.Process.Kill();
            return;
        }

        Transition(session, ScrcpySessionState.Running);
    }

    /// <summary>
    /// Reads scrcpy's output until the process ends: display
    /// identifier, errors, then final state.
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

                // The window stops being black here, and nowhere
                // else that anything could observe.
                if (session.FirstImageMs == 0 && ScrcpyOutputParser.IsFirstImage(line.Text))
                {
                    session.FirstImageMs =
                        (long)(DateTimeOffset.UtcNow - session.StartedUtc).TotalMilliseconds;
                }

                if (ScrcpyOutputParser.IsFatal(line.Text))
                {
                    var kind = ScrcpyOutputParser.Classify(line.Text);

                    // The first error is the cause, the following
                    // ones are often the consequences: we keep the
                    // first one.
                    if (session.FailureKind == ScrcpyFailureKind.None)
                    {
                        session.FailureKind = kind;
                    }

                    session.FailureMessage ??= ScrcpyOutputParser.Describe(kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The channel closed while reading, nothing to report.
        }

        // The output is closed: the process has ended or was ended.
        displayReady.TrySetResult(null);

        // This is where the window closed by hand passes through,
        // and the unplugged phone: none of our own code was called,
        // only the channel went quiet. The voluntary close is
        // already handled by StopAsync, and the single-use claim
        // prevents the double round trip.
        await StopAppOnDeviceAsync(session, CancellationToken.None).ConfigureAwait(false);

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
            FailureKind = ScrcpyFailureKind.Environment,
        };

        _sessions[sessionId] = session;
        SessionChanged?.Invoke(this, session);

        return session;
    }

    private void Fail(ScrcpySession session, string? message)
    {
        session.FailureMessage = message ?? Strings.Get("ScrcpySessionInterrupted");
        Transition(session, ScrcpySessionState.Failed);
    }

    private void Transition(ScrcpySession session, ScrcpySessionState state)
    {
        // Before the equality guard: the flag must be set even if
        // the state was already that one.
        if (state == ScrcpySessionState.Running)
        {
            session.EverRan = true;
        }

        if (session.State == state)
        {
            return;
        }

        session.State = state;
        SessionChanged?.Invoke(this, session);
    }
}
