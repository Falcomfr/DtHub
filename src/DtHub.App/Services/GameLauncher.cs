using System.Collections.Concurrent;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Guidance;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Microsoft.Extensions.Logging;

namespace DtHub.App.Services;

/// <summary>Report of a launch.</summary>
public sealed record LaunchReport(int Opened, IReadOnlyList<string> Problems)
{
    public bool AnyOpened => Opened > 0;
}

/// <summary>
/// What is wrong with a device, ready to be shown under its name.
/// </summary>
/// <param name="Text">
/// The findings, one per line, from the most serious to the
/// mildest.
/// </param>
/// <param name="Serious">
/// True when the first one will cut the session short instead of
/// merely being annoying.
/// </param>
/// <param name="Device">
/// The device's readable name, when it is known.
/// </param>
public sealed record DeviceFindings(
    string Text,
    bool Serious,
    string? Device,
    IReadOnlyList<HealthFinding>? Helps = null);

/// <summary>
/// Opens the checked instances, places their windows and wires up
/// the shortcuts. This is the only place that chains these three
/// things together.
/// </summary>
public sealed partial class GameLauncher : IAsyncDisposable
{
    private readonly ScrcpySessionManager _sessions;
    private readonly WindowManagerService _windows;
    private readonly DeviceDiscoveryService _devices;
    private readonly DeviceReconnectService _reconnect;
    private readonly DevicePairingService _pairing;
    private readonly IDeviceRegistry _registry;
    private readonly DofusInstanceService _instances;
    private readonly SettingsService _settings;
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly AppRestartService _restarts;
    private readonly WindowPlacements _placements;
    private readonly ModifierKeyRelease _modifiers;
    private readonly ILogger<GameLauncher> _logger;

    private bool _hotkeysWired;

    /// <summary>
    /// Only one reopen at a time: two that overlap steal each
    /// other's sessions.
    /// </summary>
    private readonly SemaphoreSlim _reopening = new(1, 1);

    /// <summary>
    /// Folder for the game windows' icon, set by the application.
    /// </summary>
    public string? IconDirectory { get => _iconDirectory; set => _iconDirectory = value; }

    private string? _iconDirectory;


    public GameLauncher(
        ScrcpySessionManager sessions,
        WindowManagerService windows,
        DeviceDiscoveryService devices,
        DeviceReconnectService reconnect,
        DevicePairingService pairing,
        IDeviceRegistry registry,
        DofusInstanceService instances,
        SettingsService settings,
        IHotkeyRegistrar hotkeys,
        AppRestartService restarts,
        WindowPlacements placements,
        IAppLauncher appLauncher,
        IAdbClient adb,
        ILogger<GameLauncher> logger)
    {
        _appLauncher = appLauncher;
        _adb = adb;
        _sessions = sessions;
        _windows = windows;
        _devices = devices;
        _reconnect = reconnect;
        _pairing = pairing;
        _registry = registry;
        _instances = instances;
        _settings = settings;
        _hotkeys = hotkeys;
        _restarts = restarts;
        _placements = placements;
        _modifiers = new ModifierKeyRelease(windows.Controller);
        _logger = logger;

        // A session that dies right after opening left no trace at
        // all: the window vanished and the log stayed silent.
        _sessions.SessionChanged += OnSessionChanged;

        // Two Galaxy S25 never showed the "Switch off" line, and a log
        // that only spoke of unreadable answers said nothing: every step
        // between the setting and the line goes there, on change only.
        _devices.PanelRead += (serial, reading) => LogPanelRead(
            serial,
            reading switch
            {
                { Failed: true } => "sans réponse",
                { Lit: true } => "allumé",
                { Lit: false } => "éteint",
                _ => "incompris",
            },
            reading.Answer);
        _sessions.ScreenHeld += (device, confirmed) =>
            LogScreenHeld(device, confirmed ? "confirmé par le téléphone" : "sans confirmation à l'échéance");
        _sessions.ScreenReleased += (device, release) => LogScreenReleased(
            device,
            release.OnItsOwn ? "s'est arrêtée seule" : "arrêtée par DT Hub",
            release.ExitCode,
            release.LastLines);

        // Whether to stop the game on close is decided at the moment
        // of closing, not at launch: without this subscription,
        // unchecking the box mid session would have changed nothing
        // before the next launch, and the window we close right
        // after would still have killed the game.
        // Renaming an account only showed up in the list and nowhere
        // else: the tab's label was a copy taken when the window was
        // housed, and a free window's title came from scrcpy at
        // launch. Closing and reopening was needed to see the new
        // name.
        _settings.Changed += (_, document) =>
        {
            _sessions.StopAppOnClose = document.StopAppOnClose;
            ApplyRenames(document);
            ApplyColours(document);
        };

        // The window is placed before the game opens, so that it is
        // born at its final size.
        _sessions.PrepareWindow = async (session, placement, cancellationToken) =>
        {
            // The corner aimed at is that of the frame, whereas the
            // placement passed to scrcpy is that of the client area:
            // repositioning the window on the placement's coordinates
            // offset it by a border and a title bar, visibly, right
            // after it opened.
            //
            // Its size is left untouched: the display is already
            // born at the right one, and the game freezes its layout
            // height at initialization.
            if (placement is { } wanted)
            {
                var frame = _windows.WindowChrome();

                await _windows
                    .MoveOnlyAsync(session, wanted.X - frame.Left, wanted.Y - frame.Top, cancellationToken)
                    .ConfigureAwait(false);
            }
        };

        _sessions.RequestClose = session => _windows.RequestClose(session);

        // Stopping the game targets the address of the moment, not
        // the one from launch. Wireless debugging changes port on
        // every resume, and the command would then go to a dead
        // address: the window closed, the game stayed.
        _sessions.CurrentSerial = deviceId =>
            _serials.TryGetValue(deviceId, out var serial) ? serial : null;

        // When all known addresses have failed, we ask again rather
        // than give up: they come from the past, and a port changes
        // in exactly the two seconds that separate two sweeps.
        _sessions.LookUpSerial = async (deviceId, cancellationToken) =>
        {
            var live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

            RememberSerials(live);

            return _serials.TryGetValue(deviceId, out var serial) ? serial : null;
        };

        _sessions.AppStopFailed += OnAppStopFailed;
    }

    /// <summary>
    /// The ADB address of each device, by stable identity, as the
    /// last sweep saw it.
    ///
    /// Held here rather than asked of the registry: closing a
    /// window, and even more so closing the application, runs on a
    /// counted budget where a file read has no place.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _serials =
        new(StringComparer.Ordinal);

    /// <summary>Remembers where to reach each reachable device.</summary>
    private void RememberSerials(DeviceDiscoveryResult discovery)
    {
        foreach (var device in discovery.Devices)
        {
            if (device.IsConnected && !string.IsNullOrWhiteSpace(device.Serial))
            {
                _serials[device.Id] = device.Serial;
            }
            else
            {
                // A device that is gone has no address anymore:
                // keeping the last one would aim at a dead one while
                // believing to aim at the present.
                _ = _serials.TryRemove(device.Id, out _);
            }
        }
    }

    /// <summary>
    /// Says that the game stayed open on the phone.
    ///
    /// Staying silent would be the worse of the two: the window did
    /// disappear, so the screen has no way left to show that the
    /// character is still online, and the user finds out at the next
    /// launch.
    /// </summary>
    private void OnAppStopFailed(object? sender, ScrcpySession session)
    {
        _stopFailed = TimedNotice.Raised(
            Strings.Format("GameLeftRunning", session.Target.DisplayName), DateTimeOffset.UtcNow);

        LogGameLeftRunning(session.Target.DisplayName, session.Serial);
    }

    /// <summary>
    /// Instances set aside by the automatic placements.
    /// </summary>
    private IReadOnlySet<string> _unmanaged = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Accounts that open inside the tabbed frame.</summary>
    private HashSet<string> _tabbed = new(StringComparer.Ordinal);

    /// <summary>
    /// The tabbed frame, created for the first account that asks
    /// for one.
    ///
    /// Lazy: most sessions do not want one, and an empty window
    /// opened for nothing would stand out.
    /// </summary>
    private Windows.TabbedGameWindow? _tabs;

    /// <summary>
    /// Settings derived from the chosen quality, reread at every
    /// launch.
    /// </summary>
    private QualityProfile _quality = QualityProfile.For(StreamQuality.Medium);
    private GameZoom _zoom = GameZoom.Normal;
    private bool _sharpImage;

    /// <summary>
    /// Display delay suited to this link, in milliseconds.
    ///
    /// Reread at the same time as the budget, and for the same
    /// reason: both are derived from what the device says about its
    /// link, and neither changes between two window resizes.
    /// </summary>
    /// <summary>
    /// Display buffer per device, in milliseconds.
    ///
    /// Per device and not shared: two phones can be on different
    /// bands, and that is the case as soon as a second one is
    /// plugged in. Measured with a 13T Pro on 5 GHz and a Mi 9T Pro
    /// on 2.4 GHz, the shared value was whichever came first, and
    /// the second one inherited a buffer computed for a link that
    /// was not its own.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _videoBuffers = new(StringComparer.Ordinal);

    /// <summary>Pace of controls and polling, based on quality.</summary>
    public QualityProfile Quality => _quality;

    /// <summary>
    /// Instances already launched, to know what to reopen when a
    /// window drops. The session only carries a target, not the
    /// original instance.
    /// </summary>
    //
    // Concurrent, like the recovery tables below: written by the launch,
    // the settings and every read loop, a phone dropping its Wi-Fi ends
    // all its accounts' loops at once.
    private readonly ConcurrentDictionary<string, DofusInstance> _launched = new(StringComparer.Ordinal);

    /// <summary>
    /// The distance each window was opened with.
    ///
    /// It is fixed for the whole session, scrcpy receiving it as a
    /// startup argument. The account list uses it to say that a
    /// window is still running with the old one, rather than let
    /// the setting seem dead.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, GameZoom> _zoomsInUse = new(StringComparer.Ordinal);

    /// <summary>
    /// Recovery attempts per instance, and time of the last one.
    /// </summary>
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Last)> _recoveries =
        new(StringComparer.Ordinal);

    /// <summary>
    /// What recovery has to say, or <c>null</c>. Joins the same
    /// banner as the heat: that is where one looks when something is
    /// wrong.
    /// </summary>
    public string? RecoveryNotice =>
        _recovery.IsLiveAt(DateTimeOffset.UtcNow, RecoveryNoticeLife) ? _recovery.Text : null;

    private TimedNotice _recovery;

    /// <summary>
    /// True while this account's window is being brought back.
    ///
    /// <see cref="RecoveryNotice" /> says the same thing in the banner,
    /// but it says it once for everyone: with four accounts open, the
    /// reader is told a window is coming back without being told whose
    /// row to watch. This answers per account.
    ///
    /// **It is a self-expiring notice and not a flag.** If the recovery
    /// itself throws, the caller logs it and carries on, and nothing
    /// would ever clear a plain flag: the row would say "reconnecting"
    /// for as long as the application ran. A notice says by itself
    /// whether it is still true.
    /// </summary>
    public bool IsRecovering(string key) =>
        _recovering.TryGetValue(key, out var notice)
        && notice.IsLiveAt(DateTimeOffset.UtcNow, RecoveryNoticeLife);

    private readonly ConcurrentDictionary<string, TimedNotice> _recovering = new(StringComparer.Ordinal);

    /// <summary>
    /// The device and the moment of the current recovery notice.
    ///
    /// **A recovery notice is an event, not a state.** It promises a
    /// window that comes back; if the phone disappears in the
    /// meantime, the promise no longer holds, yet the notice kept
    /// showing on screen under a line that said "offline". Two
    /// sentences that contradict each other in the same column.
    /// </summary>
    private string? _recoveryDevice;


    /// <summary>
    /// What the notice is granted. The three attempts spread over
    /// twenty-two seconds, plus the time to open a window: beyond
    /// that, the notice talks about a recovery that is no longer
    /// happening.
    /// </summary>
    private static readonly TimeSpan RecoveryNoticeLife = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The game we could not close, or <c>null</c>. Joins the
    /// recovery banner, for the same reason: it explains what just
    /// happened.
    /// </summary>
    public string? StopFailedNotice =>
        _stopFailed.IsLiveAt(DateTimeOffset.UtcNow, StopFailedNoticeLife) ? _stopFailed.Text : null;

    private TimedNotice _stopFailed;

    /// <summary>
    /// What the notice is granted. Enough to be read after a window
    /// closes, too short to survive into the next game.
    /// </summary>
    private static readonly TimeSpan StopFailedNoticeLife = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The account the phone runs at a reduced resolution, or <c>null</c>.
    /// Said in the banner, since nothing in DT Hub can fix it and the player
    /// would otherwise look for it there.
    /// </summary>
    public string? ReducedRenderNotice =>
        _reducedRender.IsLiveAt(DateTimeOffset.UtcNow, ReducedRenderNoticeLife) ? _reducedRender.Text : null;

    private TimedNotice _reducedRender;

    /// <summary>Long enough to be read, and acted on, on the phone.</summary>
    private static readonly TimeSpan ReducedRenderNoticeLife = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The percentage at which the phone draws each account's game, by
    /// account key, for the accounts it draws smaller than their display.
    /// Unlike the banner it lasts as long as the reduction does: the row
    /// offers the fix, and a measure without reduction takes it away.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _reducedRenders =
        new(StringComparer.Ordinal);

    public int? ReducedRenderPercent(string key) =>
        _reducedRenders.TryGetValue(key, out var percent) ? percent : null;

    /// <summary>The account the banner's reduction is about, to keep it quiet on request.</summary>
    public string? ReducedRenderKey { get; private set; }

    /// <summary>
    /// The game modes the phone offers each account, read with its window's
    /// measure: known once its window opened in this run.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, GameModes> _gameModes =
        new(StringComparer.Ordinal);

    public GameModes? GameModesOf(string key) => _gameModes.GetValueOrDefault(key);

    /// <summary>
    /// The accounts DT Hub put in the standard mode in this run. The mode in
    /// use is not readable on a profile other than the main one, and this is
    /// what tells that the standard mode did not help.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _setStandard =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The accounts whose last measure was taken in the standard mode. Kept
    /// apart from the mode shown: a mode picked while the game runs only
    /// applies at its next start, and the measure until then says nothing
    /// of it.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _measuredInStandard =
        new(StringComparer.Ordinal);

    /// <summary>
    /// True when the game is reduced although measured in the standard mode:
    /// the reduction comes from elsewhere on the phone, and the button that
    /// puts the standard mode back would only go round in circles.
    /// </summary>
    public bool StandardDidNotHelp(string key) =>
        _reducedRenders.ContainsKey(key) && _measuredInStandard.GetValueOrDefault(key);

    /// <summary>
    /// Drops the recovery notice when the phone it promises a window
    /// on has gone away.
    ///
    /// **Its age is no longer checked here**: the notice answers that
    /// itself, at every read. Checking it here meant checking it from
    /// the sweep, and the sweep is the one thing that stops when the
    /// panel is hidden, so a notice raised just before hiding it was
    /// still on screen, word for word, hours later.
    ///
    /// The departure of a device cannot be read from a timestamp, so
    /// that half stays. A recovery notice is an event, not a state: it
    /// promises a window that comes back, and if the phone is gone the
    /// promise no longer holds. The notice about a game left running
    /// has no such clause, and needs no sweep at all: a phone that has
    /// gone away does not make it false, it makes it truer still.
    /// </summary>
    private void ForgetRecoveryIfDeviceLeft(DeviceDiscoveryResult discovery)
    {
        if (!_recovery.Exists)
        {
            return;
        }

        var present = _recoveryDevice is { Length: > 0 } id
            && discovery.Devices.Any(d =>
                d.IsConnected && string.Equals(d.Id, id, StringComparison.Ordinal));

        if (!present)
        {
            _recovery = default;
            _recoveryDevice = null;
        }
    }

    /// <summary>
    /// Raised when a lost window needs to be reopened.
    ///
    /// The window is rebuilt by the application, not here: this
    /// service does not live on the UI thread, and a session's death
    /// is announced from scrcpy's read loop.
    /// </summary>
    public event EventHandler<RecoveryRequest>? RecoveryRequested;


    /// <summary>
    /// Logs a session's death, with scrcpy's output. Without this,
    /// a window that closes on its own cannot be diagnosed.
    /// </summary>
    private void OnSessionChanged(object? sender, ScrcpySession session)
    {
        // An account opening or closing shifts the keys of the lines after
        // it, in the titles and on the tabs. A deliberate close refreshes
        // from StopSessionAsync, not from here, which would also retitle
        // every window while quitting; and one refresh per burst: a launch
        // or a phone dropping moves several sessions at once.
        if (!session.StopRequested && Interlocked.Exchange(ref _retitleQueued, 1) == 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(RetitleDelay).ConfigureAwait(false);
                _ = Interlocked.Exchange(ref _retitleQueued, 0);

                try
                {
                    _ = await RefreshWindowTitlesAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    LogTitleStatusFailure(exception);
                }
            });
        }

        // The window is back: the recovery notice no longer has a
        // reason to exist. The attempt count, though, survives, and
        // that is deliberate: a link that flickers must eventually
        // exhaust its credit.
        if (session.State == ScrcpySessionState.Running)
        {
            _recovery = default;
            _ = _recovering.TryRemove(session.Target.Key, out _);
        }

        if (session.IsAlive)
        {
            return;
        }

        // Before the deliberate close is set aside: closing a window from
        // DT Hub, a tab's cross or quitting, is the most common end, and
        // it used to count no time at all.
        CountPlaytime(session);

        // Each session's own mark, not a flag for the whole launcher: the
        // flag also silenced a window dying on another phone while one was
        // being closed here, which was then neither logged nor reopened.
        if (session.StopRequested)
        {
            return;
        }

        LogSessionEnded(
            session.Target.DisplayName,
            session.State.ToString(),
            session.FailureMessage ?? "aucun message",
            string.Join(Environment.NewLine, session.RecentOutput));

        // Before concluding that nothing is left: a window we are
        // about to reopen is not a lost window. Without this
        // reservation, a Wi-Fi hiccup on the last session closed the
        // application.
        if (TryRecover(session))
        {
            return;
        }

        if (_sessions.ActiveSessions.Count == 0)
        {
            LastWindowClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Decides whether this window should be reopened, and asks for
    /// it if so.
    ///
    /// The decision itself lives in <see cref="SessionRecovery" />,
    /// where it is tested. Here we only keep the attempt count, and
    /// forget it after a while without a relapse.
    /// </summary>
    private bool TryRecover(ScrcpySession session)
    {
        var key = session.Target.Key;

        if (!_launched.TryGetValue(key, out var instance))
        {
            return false;
        }

        var already = _recoveries.TryGetValue(key, out var seen)
            && DateTimeOffset.UtcNow - seen.Last < SessionRecovery.Forget
                ? seen.Count
                : 0;

        var decision = SessionRecovery.Decide(session.End, already);

        if (!decision.Retry)
        {
            // We only stay silent if nothing had been promised.
            // After announced attempts, giving up without saying so
            // would leave the reader waiting for a window that will
            // not come back.
            if (already > 0 && SessionRecovery.Recoverable(session.End.Failure))
            {
                _recovery = TimedNotice.Raised(
                    Strings.Format("SessionRecoveryGaveUp", instance.DisplayName),
                    DateTimeOffset.UtcNow);
                _recoveryDevice = instance.DeviceId;

                // Given up is the opposite of recovering: the row must
                // stop promising a window that is not coming.
                _ = _recovering.TryRemove(key, out _);

                LogRecoveryGaveUp(instance.DisplayName, already);
            }

            return false;
        }

        _recoveries[key] = (already + 1, DateTimeOffset.UtcNow);

        _recovery = TimedNotice.Raised(
            Strings.Format("SessionRecovering", instance.DisplayName), DateTimeOffset.UtcNow);
        _recoveryDevice = instance.DeviceId;

        _recovering[key] = _recovery;

        LogRecovering(instance.DisplayName, already + 1, (int)decision.Delay.TotalSeconds);

        RecoveryRequested?.Invoke(this, new RecoveryRequest(instance, decision.Delay));

        return true;
    }

    private int _retitleQueued;

    private static readonly TimeSpan RetitleDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Sessions whose playtime has already been counted.</summary>
    private readonly HashSet<string> _counted = new(StringComparer.Ordinal);

    private bool CountOnce(string sessionId)
    {
        lock (_counted)
        {
            return _counted.Add(sessionId);
        }
    }

    /// <summary>
    /// Adds to the account the time its window stayed open.
    ///
    /// Once per session, never twice: a session can go through two
    /// final states, stopped then failed, and counting it for both
    /// would double the time.
    ///
    /// Only if it actually ran: a session that never opened its
    /// display is not playtime.
    /// </summary>
    private void CountPlaytime(ScrcpySession session)
    {
        if (!session.EverRan || session.Target.IsTransient || !CountOnce(session.Id))
        {
            return;
        }

        var seconds = (int)(DateTimeOffset.UtcNow - session.StartedUtc).TotalSeconds;

        if (seconds < SettingsService.MinimumCountedPlaytimeSeconds)
        {
            return;
        }

        _playtimeWrites.Add(_settings.AddPlaytimeAsync(session.Target.Key, seconds));
    }

    /// <summary>
    /// Playtime writes still running. Quitting closes every window at
    /// once, and its writes were left to finish on their own while the
    /// application went away: CloseAllAsync waits for them.
    /// </summary>
    private readonly ConcurrentBag<Task> _playtimeWrites = [];

    /// <summary>Video encoders known per device, once asked for.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<VideoEncoder>> _encoders = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices whose list has already been asked for, whether it
    /// succeeded or not.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _encodersAsked = new(StringComparer.Ordinal);

    /// <summary>
    /// Asks, in the background, what the devices know how to encode.
    ///
    /// In the background and only once per device: the question
    /// costs a push of the scrcpy server, and it is never urgent.
    /// The result serves the next launch, not this one. Waiting here
    /// would delay opening by several seconds for a piece of
    /// information most devices have no use for.
    /// </summary>
    private void ProbeEncoders(
        IReadOnlyList<DofusInstance> instances,
        Dictionary<string, AndroidDevice> devices)
    {
        foreach (var (deviceId, serial) in instances
                     .Select(i => (i.DeviceId, Serial: devices.TryGetValue(i.DeviceId, out var d) ? d.Serial : null))
                     .Where(p => !string.IsNullOrWhiteSpace(p.Serial))
                     .DistinctBy(p => p.Serial, StringComparer.Ordinal)
                     .ToList())
        {
            if (!_encodersAsked.TryAdd(serial!, 0))
            {
                continue;
            }

            _ = Task.Run(async () =>
            {
                var found = await _sessions.ListEncodersAsync(deviceId, serial!).ConfigureAwait(false);

                if (found.Count == 0)
                {
                    return;
                }

                _encoders[serial!] = found;

                LogEncoders(
                    serial!,
                    string.Join(", ", ScrcpyEncoders.HardwareCodecs(found)),
                    found.Count);
            });
        }
    }

    /// <summary>Currently open sessions.</summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions => _sessions.ActiveSessions;

    /// <summary>
    /// What the phone said of each open game's window at its last check,
    /// for the diagnostic report: a player sends it while the fault is on
    /// screen, and the phone's own words are what we lack.
    /// </summary>
    public IReadOnlyList<string> WindowStates =>
    [
        .. ActiveSessions
            .Select(s => _windowStates.TryGetValue(s.Id, out var state) ? state : null)
            .OfType<string>(),
    ];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _windowStates =
        new(StringComparer.Ordinal);

    /// <summary>True if an opening is in progress on this phone.</summary>
    public bool IsDeviceBusy(string deviceId) => _sessions.IsDeviceBusy(deviceId);

    /// <summary>
    /// Raised when a device becomes busy, or stops being so.
    /// </summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? DeviceBusyChanged
    {
        add => _sessions.DeviceBusyChanged += value;
        remove => _sessions.DeviceBusyChanged -= value;
    }

    /// <summary>
    /// Windows that follow the automatic placements.
    ///
    /// A window unchecked in the list stays where it is: keyboard
    /// navigation, rearranging, tiling and sizes all ignore it. It
    /// opens, closes and remembers its place like the others.
    /// </summary>
    /// <remarks>
    /// Windows opened in passing are left out: stacking or tiling a portrait
    /// store page among the game windows would squash it.
    /// </remarks>
    public IReadOnlyList<ScrcpySession> ManagedSessions =>
        [.. _sessions.ActiveSessions.Where(
            s => !_unmanaged.Contains(s.Target.Key) && !_tabbed.Contains(s.Target.Key) && !s.Target.IsTransient)];

    /// <summary>
    /// Accounts actually housed in the tabbed frame.
    ///
    /// Counted from the open sessions and not from the setting: an
    /// account can be marked as housed and yet never have been
    /// attached.
    /// </summary>
    public int HousedCount =>
        _sessions.ActiveSessions.Count(s => _tabbed.Contains(s.Target.Key));

    /// <summary>
    /// True if the tabbed frame follows geometry commands.
    ///
    /// A single housed account that is locked freezes it: the frame
    /// is a single window, and one tab cannot be held still while
    /// another is moved.
    /// </summary>
    public bool FrameMoves => _tabs is not null && !FrameLock.Freezes(_unmanaged, _tabbed);

    /// <summary>
    /// Number of windows a tidy-up can move, the frame counting as
    /// one. Below two, there is nothing to arrange and the buttons
    /// on the window's footer withdraw.
    /// </summary>
    public int ArrangeableCount =>
        ManagedSessions.Count + (HousedCount > 0 && FrameMoves ? 1 : 0);

    /// <summary>
    /// The frame, when it exists, is visible, and no lock freezes
    /// it.
    /// </summary>
    private Windows.TabbedGameWindow? MovableFrame =>
        _tabs is { Handle: not 0 } frame && FrameMoves ? frame : null;

    /// <summary>
    /// Serializes geometry commands.
    ///
    /// They arrive on the shortcuts thread, hop onto the UI thread
    /// and write the settings back on the way through: two Ctrl+5
    /// pressed close together interleaved two entries into
    /// fullscreen, and the frame lost the rectangle it came from.
    /// </summary>
    private readonly SemaphoreSlim _arranging = new(1, 1);

    /// <summary>
    /// Gives the frame its current size, or fullscreen, without
    /// moving it.
    ///
    /// Nothing is ever applied to the housed window: it is a child
    /// of the frame, and it is the frame that gets sized. Its shape,
    /// though, follows the tab shown: the frame's size is therefore
    /// in practice a share of width, with the height coming from the
    /// image's aspect ratio.
    /// </summary>
    private Task ResizeFrameAsync(bool fullscreen) =>
        MovableFrame is not { } frame
            ? Task.CompletedTask
            : OnUiAsync(() =>
            {
                if (fullscreen)
                {
                    if (_windows.ScreenBoundsFor(frame.Handle) is { } bounds)
                    {
                        frame.SetFullscreen(true, bounds);
                    }

                    return;
                }

                // The previous rectangle is kept by the frame itself.
                var leaving = frame.IsFullscreen;
                frame.SetFullscreen(false, default);

                // Back at the size from before, the frame keeps that
                // rectangle, as the free windows do: recomputed from
                // the share, a frame sized by hand came back smaller.
                if (leaving && _windows.SizePercent == _windows.PercentBeforeFullscreen)
                {
                    return;
                }

                if (frame.Chassis is { } chassis
                    && _windows.ResizedRect(frame.Handle, frame.SelectedAspect ?? 0, chassis)
                        is { } rect)
                {
                    frame.ApplyRect(rect);
                }
            });

    /// <summary>
    /// Places the frame on an imposed rectangle, that of a stack.
    /// </summary>
    private Task StackFrameAsync(ScreenRect rect) =>
        MovableFrame is not { } frame
            ? Task.CompletedTask
            : OnUiAsync(() =>
            {
                frame.SetFullscreen(false, default);
                frame.ApplyRect(rect);
            });

    /// <summary>
    /// Raised when the set of windows the placements can arrange may
    /// have changed: something set aside, an entry into or an exit
    /// from the tabbed frame, a reordering.
    ///
    /// Opening and closing go through <see cref="SessionChanged"/>;
    /// this one covers what changes without any session moving.
    /// </summary>
    public event EventHandler? ArrangeableChanged;

    /// <summary>Raised on every state change of a session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged
    {
        add => _sessions.SessionChanged += value;
        remove => _sessions.SessionChanged -= value;
    }

    /// <summary>Requested by the configurator toggle shortcut.</summary>
    public event EventHandler? ConfiguratorToggleRequested;

    /// <summary>The quest tracker shortcut was pressed.</summary>
    public event EventHandler? QuestsToggleRequested;

    /// <summary>Requested by the Almanax shortcut.</summary>
    public event EventHandler? AlmanaxRequested;

    /// <summary>
    /// Requested by the quit shortcut. The actual shutdown belongs
    /// to the application, which must first remember the session's
    /// state.
    /// </summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Raised when the last game window closes on its own. Closes
    /// wanted by the application are not part of this.
    /// </summary>
    public event EventHandler? LastWindowClosed;

    /// <summary>
    /// Sweeps the phones and returns the known instances, up to
    /// date. Instances whose phone is absent stay listed, offline.
    /// </summary>
    public async Task<IReadOnlyList<DofusInstance>> RefreshInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        // One sweep at a time: the panel's poll, the launch and a reopening
        // overlapped at startup, and the discovery keeps its scan in shared
        // fields that one sweep cleared while the other was forgetting the
        // profiles it no longer saw.
        await _sweeping.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await SweepInstancesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _sweeping.Release();
        }
    }

    private readonly SemaphoreSlim _sweeping = new(1, 1);

    private async Task<IReadOnlyList<DofusInstance>> SweepInstancesAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        _instances.PackageName = settings.PackageName;
        _instances.ShownApps = await _settings.GetShownAppsAsync(cancellationToken).ConfigureAwait(false);

        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        RememberSerials(discovery);

        // Not awaited: one package listing per phone, up to its timeout on a
        // phone in deep doze, held back the accounts' discovery and their
        // reopening at startup for every player, tools used or not.
        _ = SweepPartnerAppsInBackgroundAsync(discovery.Devices, cancellationToken);

        var found = await _instances.DiscoverAsync(discovery.Devices, cancellationToken)
            .ConfigureAwait(false);

        // Vanished accounts are forgotten before the merge:
        // otherwise their remembered entry would reappear in the
        // result, and the list would keep an account that no longer
        // exists anywhere. Two kinds of disappearance count: the
        // profile removed from the phone, and the game uninstalled
        // from a profile that remains.
        var forgotten = await _settings
            .ForgetMissingProfilesAsync(
                _instances.ScannedProfiles,
                _instances.ProfilesWithoutGame,
                cancellationToken)
            .ConfigureAwait(false);

        if (forgotten > 0)
        {
            LogProfilesForgotten(forgotten);
        }

        return await _settings.MergeInstancesAsync(found, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Which connected phones have each MakeInTouch app, as of the last
    /// sweep. Main profile only: the apps are opened from user 0.
    /// </summary>
    public IReadOnlyList<PartnerAvailability> PartnerAvailability { get; private set; } =
        PartnerApps.Availability([]);

    /// <summary>The phones of the last sweep, for a refresh between two.</summary>
    private IReadOnlyList<AndroidDevice> _sweptDevices = [];

    /// <summary>
    /// Reads again which phones have each tool, on the phones of the last
    /// sweep. Called when the tools menu opens: the full sweep comes seldom,
    /// and an app uninstalled meanwhile kept offering Open (2026-10-11).
    /// </summary>
    public Task RefreshPartnerAppsAsync(CancellationToken cancellationToken = default) =>
        SweepPartnerAppsAsync(_sweptDevices, cancellationToken);

    /// <summary>Raised after each sweep, off the interface thread.</summary>
    public event EventHandler? PartnerAvailabilityChanged;

    /// <summary>The display name of each phone seen at the last sweep.</summary>
    public IReadOnlyDictionary<string, string> PartnerDeviceNames { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly IAppLauncher _appLauncher;

    private readonly IAdbClient _adb;

    /// <summary>
    /// Opens a MakeInTouch app's Play Store page in a window on the PC, on a
    /// virtual display of that phone. When no window could open, the page
    /// goes to the phone's own screen instead.
    /// </summary>
    public async Task<PartnerOpening> OpenStoreAsync(
        string deviceId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        var app = PartnerApps.All.First(a => string.Equals(a.PackageName, packageName, StringComparison.Ordinal));
        var uri = PartnerApps.StoreUri(packageName);

        var opening = await OpenPassingAsync(
            deviceId,
            new DofusInstance
            {
                DeviceId = deviceId,
                DeviceName = PartnerDeviceNames.GetValueOrDefault(deviceId, deviceId),
                UserId = 0,
                UserName = "Play Store",
                PackageName = "com.android.vending",
                AppLabel = "Play Store",
                StartUri = uri,
                IsTransient = true,
                IsDeviceConnected = true,
            },
            cancellationToken).ConfigureAwait(false);

        if (opening == PartnerOpening.InWindow
            && _serials.TryGetValue(deviceId, out var watched)
            && FindSession(new DofusInstance
            {
                DeviceId = deviceId,
                DeviceName = string.Empty,
                UserId = 0,
                UserName = string.Empty,
                PackageName = "com.android.vending",
            }) is { } store)
        {
            WatchInstall(store, watched, deviceId, app.PackageName);
        }

        if (opening != PartnerOpening.Failed || !_serials.TryGetValue(deviceId, out var serial))
        {
            return opening;
        }

        var onPhone = await _appLauncher.OpenUriAsync(serial, 0, uri, displayId: null, cancellationToken)
            .ConfigureAwait(false);

        return onPhone.Succeeded ? PartnerOpening.OnPhone
            : string.Equals(onPhone.UserMessage, Strings.Get("PlayStoreMissing"), StringComparison.Ordinal)
                ? PartnerOpening.StoreMissing
                : PartnerOpening.Failed;
    }

    /// <summary>The apps each open store window is waiting for, by session.</summary>
    private readonly Dictionary<string, HashSet<string>> _awaitedInstalls = new(StringComparer.Ordinal);

    private readonly Lock _awaitedLock = new();

    /// <summary>
    /// Watches the phone while its store window is open. One watch per
    /// window, which may wait for both apps: the window is shared, and a
    /// watch per click closed it as soon as the first app arrived, under a
    /// player about to install the second.
    /// </summary>
    private void WatchInstall(ScrcpySession store, string serial, string deviceId, string packageName)
    {
        lock (_awaitedLock)
        {
            if (_awaitedInstalls.TryGetValue(store.Id, out var awaited))
            {
                _ = awaited.Add(packageName);
                return;
            }

            _awaitedInstalls[store.Id] = new HashSet<string>(StringComparer.Ordinal) { packageName };
        }

        _ = WatchInstallAsync(store, serial, deviceId);
    }

    /// <summary>
    /// Each app that arrives turns its line to Open at once, without waiting
    /// for the next sweep. Once every awaited app is there, the store window
    /// closes, its work done. Measured on 2026-10-10: left open, the player
    /// did not know what to do next.
    /// </summary>
    private async Task WatchInstallAsync(ScrcpySession store, string serial, string deviceId)
    {
        try
        {
            // ponytail: polls every 3 s for 15 min at most; only adb is on the
            // phone, nothing there could announce the install instead.
            for (var round = 0; round < 300 && store.IsAlive; round++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                if (await _instances.TryListPackagesAsync(serial, 0).ConfigureAwait(false) is not { } packages)
                {
                    continue;
                }

                List<string> arrived;
                bool done;

                lock (_awaitedLock)
                {
                    var awaited = _awaitedInstalls[store.Id];
                    arrived = [.. awaited.Where(p => packages.Contains(p, StringComparer.Ordinal))];
                    awaited.ExceptWith(arrived);
                    done = awaited.Count == 0;
                }

                foreach (var package in arrived)
                {
                    PartnerAvailability = PartnerApps.WithInstalled(PartnerAvailability, package, deviceId);
                    LogPartnerInstalled(PartnerApps.All.First(a => a.PackageName == package).Name, deviceId);
                }

                if (arrived.Count > 0)
                {
                    PartnerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                }

                if (!done)
                {
                    continue;
                }

                if (store.IsAlive)
                {
                    await StopSessionAsync(store, CancellationToken.None).ConfigureAwait(false);
                }

                // The app just installed opens at once in its own window: the
                // player installed it to use it (asked on 2026-10-11).
                foreach (var package in arrived)
                {
                    _ = await OpenPartnerAppAsync(deviceId, package).ConfigureAwait(false);
                }

                // A deliberate close, like the others: with the panel hidden
                // and no window left, the application must not stay alive
                // with nothing on screen.
                NotifyIfNothingLeft();

                return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogStoreWatchFailed(exception);
        }
        finally
        {
            lock (_awaitedLock)
            {
                _ = _awaitedInstalls.Remove(store.Id);
            }
        }
    }

    /// <summary>Opens an installed MakeInTouch app in a window on the PC.</summary>
    public async Task<PartnerOpening> OpenPartnerAppAsync(
        string deviceId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        var app = PartnerApps.All.First(a => string.Equals(a.PackageName, packageName, StringComparison.Ordinal));

        if (!_serials.TryGetValue(deviceId, out var serial))
        {
            return PartnerOpening.Failed;
        }

        var component = await _instances.ResolveComponentAsync(serial, 0, packageName, cancellationToken)
            .ConfigureAwait(false);

        return await OpenPassingAsync(
            deviceId,
            new DofusInstance
            {
                DeviceId = deviceId,
                DeviceName = PartnerDeviceNames.GetValueOrDefault(deviceId, deviceId),
                UserId = 0,
                UserName = app.Name,
                PackageName = packageName,
                LaunchComponent = component?.Value,
                AppLabel = app.Name,
                IsTransient = true,
                IsDeviceConnected = true,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Notes where the phone actually draws the game, for the log and the
    /// diagnostic report. On 14/10/2026 this line, sent by a player, showed
    /// that Android gave the game its whole display: the fault was in how
    /// the game drew itself on 21:9, not where it was placed.
    /// </summary>
    private async Task RecordGameWindowAsync(ScrcpySession session)
    {
        try
        {
            // The game has opened by then.
            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            if (!session.IsAlive || session.VirtualDisplayId is not { } display)
            {
                return;
            }

            var state = await _appLauncher
                .ReadGameWindowAsync(session.Serial, display, session.Target.PackageName, CancellationToken.None)
                .ConfigureAwait(false);

            _windowStates[session.Id] = $"{session.DisplayName} ({DisplaySize(session)}) : {state}";
            LogGameWindow(session.DisplayName, DisplaySize(session), state);

            // Locked: two accounts measured together, one clean and one
            // reduced, could otherwise clear the other's banner.
            lock (_reducedRenders)
            {
                if (AndroidAppLauncher.Upscale(state) is not { } scale)
                {
                    ForgetReducedRender(session.Target.Key);
                }
                else
                {
                    var percent = (int)Math.Round(100 / scale);

                    _reducedRenders[session.Target.Key] = percent;

                    _reducedRender = TimedNotice.Raised(
                        Strings.Format("ReducedRender", session.Target.DisplayName, percent), DateTimeOffset.UtcNow);
                    ReducedRenderKey = session.Target.Key;
                    LogReducedRender(session.DisplayName, percent);
                }
            }

            var modes = await _appLauncher
                .ReadGameModesAsync(session.Serial, session.Target.UserId, session.Target.PackageName, CancellationToken.None)
                .ConfigureAwait(false);

            if (modes is not null)
            {
                _gameModes[session.Target.Key] = modes;
            }

            // The main profile's mode is read, and the read wins: switched
            // back on the phone, it would otherwise still pass for standard.
            // A profile's is the one DT Hub set, the only one known there.
            _measuredInStandard[session.Target.Key] = modes?.Current is { } current
                ? current == "standard"
                : _setStandard.ContainsKey(session.Target.Key);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _windowStates[session.Id] = $"{session.DisplayName} ({DisplaySize(session)}) : {exception.Message}";
            LogGameWindow(session.DisplayName, DisplaySize(session), exception.Message);
        }
    }

    /// <summary>The phone's screen ratio, or null if "wm size" fails.</summary>
    private async Task<double?> ReadPhoneAspectAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        try
        {
            var output = await _adb.ShellAsync(device.Serial, ["wm", "size"], TimeSpan.FromSeconds(10), cancellationToken)
                .ConfigureAwait(false);
            var aspect = PhoneScreen.Aspect(output);

            LogPhoneAspect(
                device.DisplayName,
                aspect is { } a ? a.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) : output.Trim());

            return aspect;
        }
        catch (AdbException exception)
        {
            LogPhoneAspect(device.DisplayName, exception.Message);
            return null;
        }
    }

    private static string DisplaySize(ScrcpySession session) =>
        session.VirtualDisplaySize is { } size
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}")
            : "?";

    /// <summary>
    /// Keeps an app opened in passing alive while its window is open.
    ///
    /// Its virtual display has its own power group, and once the phone dozes
    /// Android 16 stops handing it input: the window keeps its last frame,
    /// the window manager still names the app as focused, and the input
    /// dispatcher has no focused window on that display, so every click is
    /// lost. DOFUS Touch escapes it by keeping its screen on; these apps do
    /// not. Measured on 2026-10-11, 13T Pro: a neutral key sent to the
    /// window's own display, "input -d id keyevent 0", gave the focus back at
    /// once without waking the phone. Sent to the phone's display instead, it
    /// only delayed the loss.
    ///
    /// The same round renews a one minute exemption from deep doze for the
    /// app, which gives it back the network: DTStuff loads its data online,
    /// and crawled without it. It lapses on its own, so nothing stays on the
    /// phone once the window closes.
    /// </summary>
    private async Task KeepAwakeAsync(ScrcpySession session)
    {
        // ponytail: one adb call every 20 s per passing window; a wake lock
        // would need code running on the phone.
        while (session.IsAlive)
        {
            if (_serials.TryGetValue(session.Target.DeviceId, out var serial)
                && session.VirtualDisplayId is { } display)
            {
                // Each on its own, and with a minute to answer: in deep doze
                // the phone took 33 s to run the first "input" (measured on
                // 2026-10-11), and a five second limit cut every one of them.
                foreach (IReadOnlyList<string> command in (IReadOnlyList<string>[])
                [
                    ["input", "-d", display.ToString(System.Globalization.CultureInfo.InvariantCulture), "keyevent", "0"],
                    ["cmd", "deviceidle", "tempwhitelist", "-d", "60000", session.Target.PackageName],
                ])
                {
                    try
                    {
                        _ = await _adb.ShellAsync(serial, command, TimeSpan.FromSeconds(60), CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (AdbException exception)
                    {
                        LogKeepAwakeFailed(session.Target.DisplayName, exception.Message);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A window opened in passing: brought forward when it is already open,
    /// launched otherwise. A phone gone meanwhile is a failure, not a throw.
    /// </summary>
    private async Task<PartnerOpening> OpenPassingAsync(
        string deviceId,
        DofusInstance instance,
        CancellationToken cancellationToken)
    {
        if (FindSession(instance) is { } open)
        {
            // One Play Store per phone, so one store window: the other app's
            // page opens in the window already there. Measured: with OcrApp's
            // page open, Install on DTStuff only brought that window forward.
            if (instance.StartUri is { } uri && open.VirtualDisplayId is { } display)
            {
                _ = await _appLauncher.OpenUriAsync(open.Serial, 0, uri, display, cancellationToken)
                    .ConfigureAwait(false);
            }

            await OnUiAsync(() => _windows.Focus(open)).ConfigureAwait(false);

            return PartnerOpening.InWindow;
        }

        if (!_serials.ContainsKey(deviceId))
        {
            return PartnerOpening.Failed;
        }

        // A second click while the first window is still opening: the
        // session only exists once scrcpy is up, a few seconds later, and
        // two windows on two displays left one black.
        lock (_openingPassing)
        {
            if (!_openingPassing.Add(instance.Key))
            {
                return PartnerOpening.InWindow;
            }
        }

        try
        {
            var report = await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);

            return report.Opened > 0 ? PartnerOpening.InWindow : PartnerOpening.Failed;
        }
        finally
        {
            lock (_openingPassing)
            {
                _ = _openingPassing.Remove(instance.Key);
            }
        }
    }

    /// <summary>The passing windows being opened, by key.</summary>
    private readonly HashSet<string> _openingPassing = new(StringComparer.Ordinal);

    /// <summary>Game accounts between their check and their registration.</summary>
    private readonly HashSet<string> _openingGames = new(StringComparer.Ordinal);

    /// <summary>
    /// The tools' sweep beside the accounts' one, never two at a time: a
    /// slow phone would otherwise stack them up, sweep after sweep.
    /// </summary>
    private async Task SweepPartnerAppsInBackgroundAsync(
        IReadOnlyList<AndroidDevice> devices,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _partnerSweeping, 1, 0) != 0)
        {
            return;
        }

        try
        {
            await SweepPartnerAppsAsync(devices, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The sweep that asked for it was cancelled: the next one redoes it.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPartnerSweepFailed(exception);
        }
        finally
        {
            _ = Interlocked.Exchange(ref _partnerSweeping, 0);
        }
    }

    private int _partnerSweeping;

    /// <summary>
    /// One "pm list packages" per connected phone and sweep. A phone that
    /// does not answer is left out of this sweep rather than counted as
    /// lacking the apps.
    /// </summary>
    private async Task SweepPartnerAppsAsync(
        IReadOnlyList<AndroidDevice> devices,
        CancellationToken cancellationToken)
    {
        _sweptDevices = devices;

        List<(string DeviceId, IReadOnlyList<string> PackagesUser0)> seen = [];

        foreach (var device in devices.Where(d => d.IsConnected))
        {
            if (await _instances.TryListPackagesAsync(device.Serial, 0, cancellationToken).ConfigureAwait(false)
                is { } packages)
            {
                seen.Add((device.Id, packages));
            }
        }

        PartnerAvailability = PartnerApps.Availability(seen);
        PartnerDeviceNames = devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, d => d.DisplayName, StringComparer.Ordinal);

        PartnerAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Non-blocking issues from the last instance sweep, to be
    /// joined to those from device discovery.
    /// </summary>
    public IReadOnlyList<string> InstanceWarnings => _instances.Warnings;

    /// <summary>
    /// Phones seen now. Devices already paired are reconnected along
    /// the way: a phone announcing itself on the network does not
    /// need to be paired again by hand.
    /// </summary>
    public async Task<DeviceDiscoveryResult> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKnownDevicesConnectedAsync(cancellationToken).ConfigureAwait(false);

        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        RememberSerials(discovery);

        // The health pass is deliberately not run here. It asks each phone six
        // questions in turn, measured at 2.2 seconds for two devices and
        // growing with every phone added, and it held the list back that long
        // before anything could be shown. The caller now displays what
        // discovery found, then awaits RefreshHealthAsync and fills the gauges
        // and the warnings in. Readings are cached for a minute, so this is
        // paid once and not on every sweep.
        return discovery;
    }


    /// <summary>
    /// Each device's findings, keyed by serial number.
    ///
    /// **Keyed, because where a message sits is half the message.**
    /// Gathered in a banner at the bottom of the list, they read as
    /// if they were about the last device shown, which happened to
    /// be the one that had nothing wrong. Each finding now shows
    /// under the header of the device it concerns, and there the
    /// name no longer needs repeating.
    /// </summary>
    public IReadOnlyDictionary<string, DeviceFindings> HealthByDevice => _health;

    private readonly Dictionary<string, DeviceFindings> _health = new(StringComparer.Ordinal);

    /// <summary>
    /// True when the summary carries a finding that will cut the
    /// session short, as opposed to one that will merely hamper it.
    /// Decides the icon's color.
    /// </summary>
    public bool HealthIsSerious { get; private set; }

    /// <summary>
    /// The latest battery reading per device.
    ///
    /// Exposed and not only summarized as a warning: the reading is
    /// taken anyway, and a level shown at all times is worth more
    /// than an alert that only arrives at twenty percent.
    /// </summary>
    public IReadOnlyDictionary<string, BatteryReading> Batteries => _batteries;

    private readonly Dictionary<string, BatteryReading> _batteries = new(StringComparer.Ordinal);

    /// <summary>
    /// The four readings taken of each device, kept whole.
    ///
    /// The health sweep already awaits all four per device and then
    /// drops the objects, keeping only the sentences they produced. So
    /// a phone with nothing wrong had nothing to show: no finding, and
    /// no state either. These are the same readings, at no extra cost,
    /// carried far enough to be displayed.
    /// </summary>
    public IReadOnlyDictionary<string, DeviceVitals> Vitals => _vitals;

    private readonly Dictionary<string, DeviceVitals> _vitals = new(StringComparer.Ordinal);

    /// <summary>Last thermal state logged per device.</summary>
    private readonly Dictionary<string, int> _loggedHeat = new(StringComparer.Ordinal);

    /// <summary>Last battery tier logged per device.</summary>
    private readonly Dictionary<string, int> _loggedBattery = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices whose lack of storage has already been logged.
    /// </summary>
    private readonly HashSet<string> _loggedStorage = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MemoryPressure> _loggedMemory = new(StringComparer.Ordinal);

    /// <summary>
    /// Rereads the devices' state and draws a summary from it.
    ///
    /// Those that carry a window first, and failing that those that
    /// are simply connected. Readings are cached by discovery, one
    /// minute for heat and battery, a quarter of an hour for
    /// storage: the panel can poll every two seconds without paying
    /// for it.
    ///
    /// The log only speaks when a tier changes. The same line
    /// repeated three hundred times in ten minutes would drown out
    /// everything else.
    ///
    /// Called after the list is on screen, never before: the questions it
    /// asks each phone take seconds, and nothing it produces is needed to
    /// show which phones are there.
    /// </summary>
    public async Task RefreshHealthAsync(
        DeviceDiscoveryResult discovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);

        ForgetRecoveryIfDeviceLeft(discovery);

        await RefreshBatteriesAsync(discovery, cancellationToken).ConfigureAwait(false);

        // **A session keeps the serial it was opened on**: the mDNS name
        // when the phone had no other at launch, or an address whose port
        // wireless debugging has changed since. Discovery holds the serial
        // that answers now, and the phone was swept twice, once under each,
        // its second findings landing in the banner at the bottom of the
        // list, which no account carries. Measured on 2026-10-03 with the
        // 13T Pro. The device id, which does not change, ties them.
        var playing = _sessions.ActiveSessions
            .Where(s => s.IsAlive)
            .Select(s => discovery.Devices.FirstOrDefault(d => d.IsConnected && d.Id == s.Target.DeviceId)?.Serial
                ?? s.Target.Serial)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await RefreshLitPanelsAsync(discovery, cancellationToken).ConfigureAwait(false);

        // **Every connected phone, and not only those playing.** The
        // shortcut used to stop at the phones carrying a window as
        // soon as there was one, and the findings of the others were
        // then neither refreshed nor dropped: the pruning below only
        // removes a device that has gone away. A phone left connected
        // and idle therefore kept showing, indefinitely, a finding
        // from the last time it was asked.
        //
        // It costs nothing to speak of: the four readings are cached
        // for a minute by discovery, so an idle phone is worth four
        // ADB calls a minute, whatever the sweep's pace. The lock
        // question below stays reserved for the phones that carry
        // windows, since it is about those windows.
        List<string> serials =
        [
            .. playing,
            .. discovery.Devices
                .Where(d => d.IsConnected)
                .Select(d => d.Serial)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Where(s => !playing.Contains(s, StringComparer.Ordinal)),
        ];

        serials = [.. serials.Distinct(StringComparer.Ordinal)];

        if (serials.Count == 0)
        {
            HealthIsSerious = false;
            _health.Clear();
            _vitals.Clear();
            _loggedHeat.Clear();
            _loggedBattery.Clear();
            _loggedStorage.Clear();
            _loggedMemory.Clear();
            return;
        }

        List<HealthFinding> findings = [];

        int? pcBusy = _pcBusy is >= 0 and var busy ? busy : null;

        foreach (var serial in serials)
        {
            var heat = await _devices.GetThermalAsync(serial, cancellationToken).ConfigureAwait(false);
            var battery = await _devices.GetBatteryAsync(serial, cancellationToken).ConfigureAwait(false);
            var storage = await _devices.GetStorageAsync(serial, cancellationToken).ConfigureAwait(false);
            var link = await _devices.GetWifiLinkAsync(serial, cancellationToken).ConfigureAwait(false);
            var memory = await _devices.GetMemoryAsync(serial, cancellationToken).ConfigureAwait(false);
            var strain = _strains.GetValueOrDefault(serial);

            // The same four, kept whole. The findings below turn them
            // into sentences and keep only those; the panel needs the
            // state as well, and asking the phone twice for it would
            // double the slowest part of the sweep.
            _vitals[serial] = new DeviceVitals(
                battery, heat, storage, link, memory,
                _pings.TryGetValue(serial, out var spikes) ? spikes.Since(DateTimeOffset.UtcNow) : null);

            // Do this device's windows show the lock icon rather
            // than the game? The question is only asked where there
            // are windows, and only on a device whose display
            // follows the lock state: elsewhere, nothing is asked of
            // the phone.
            var locked = playing.Contains(serial, StringComparer.Ordinal)
                && await _devices
                    .IsVirtualDisplayUnlockedAsync(serial, cancellationToken)
                    .ConfigureAwait(false) == false
                && await _devices
                    .IsDeviceLockedAsync(serial, cancellationToken)
                    .ConfigureAwait(false) == true;

            // **An alert only while the app it is about runs**, in a window
            // on this phone (asked on 2026-10-11): a warning about an app
            // nobody has open is noise. Battery preparation has long been
            // described in the help; this check only says whether it has
            // been done, for the game and for each DOFUS Touch tool, whose
            // window HyperOS froze without it.
            var deviceId = discovery.Devices
                .FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.Ordinal))?.Id;
            var running = _sessions.ActiveSessions
                .Where(s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal))
                .ToList();

            // An alert the player hid from its help page stays hidden: on a
            // Galaxy Tab the setting does not exist and Android's list stays
            // empty however well the game runs (2026-10-13).
            var unprepared = running.Any(s => !s.Target.IsTransient)
                && !await _settings
                    .IsBatteryAlertMutedAsync(deviceId ?? serial, DofusPackages.GameName, cancellationToken)
                    .ConfigureAwait(false)
                && await _devices
                    .IsBatteryExemptAsync(serial, DofusPackages.DofusTouch, cancellationToken)
                    .ConfigureAwait(false) == false;

            List<string> unpreparedTools = [];

            foreach (var tool in PartnerApps.All.Where(a => running.Any(
                         s => s.Target.IsTransient && string.Equals(s.Target.PackageName, a.PackageName, StringComparison.Ordinal))))
            {
                if (!await _settings.IsBatteryAlertMutedAsync(deviceId ?? serial, tool.Name, cancellationToken)
                        .ConfigureAwait(false)
                    && await _devices.IsBatteryExemptAsync(serial, tool.PackageName, cancellationToken)
                        .ConfigureAwait(false) == false)
                {
                    unpreparedTools.Add(tool.Name);
                }
            }

            // **The input probe, once per device, on its first
            // window.** It sends Android's "unknown" key, which
            // triggers nothing anywhere, and returns its verdict
            // within a second. Its alert, like the others, only shows
            // while a window is open on the phone.
            //
            // The type that carries it used to say it is only sent
            // on request. That rule changes here, and the field
            // forced it: twice in the same day, windows showed the
            // game without responding to anything, and the guilty
            // setting unchecks itself on its own at restart. Never
            // during a game, never repeatedly: one question, at the
            // moment the first window opens.
            var dead = running.Count > 0
                && await Inputs(serial, cancellationToken).ConfigureAwait(false) == InputInjection.Denied;

            Trace(serial, heat, battery, storage);
            TraceMemory(serial, memory);
            TraceLock(serial, locked);
            TracePreparation(serial, unprepared);
            TraceDeadInput(serial, dead);

            var seen = DeviceHealth.Review(
                heat, battery, storage, locked, unprepared, dead, memory,
                strain,
                playing.Count > 0 && serial == playing[0] ? pcBusy : null,
                unpreparedTools,
                running.Any(s => !s.Target.IsTransient) && _soundAfterReopen.IsStuck(serial));

            if (DeviceHealth.Every(seen) is { } said)
            {
                _health[serial] = new DeviceFindings(
                    said,
                    seen[0].Severity == HealthSeverity.Serious,
                    Named(discovery, serial),
                    [.. seen.Where(f => f.Help != HealthHelp.None)]);
            }
            else
            {
                _ = _health.Remove(serial);
            }

            findings.AddRange(seen);
        }

        var ordered = findings.OrderByDescending(f => f.Severity).ToList();

        HealthIsSerious = ordered.Count > 0 && ordered[0].Severity == HealthSeverity.Serious;
    }

    /// <summary>
    /// Drops what is known about devices that are no longer there.
    ///
    /// Four dictionaries are keyed by serial and all four have to be
    /// pruned the same way. They were three near-identical loops, and
    /// the fourth was added the day someone noticed the second had
    /// been missing for months.
    /// </summary>
    private static void Forget<T>(Dictionary<string, T> known, List<string> connected)
    {
        foreach (var gone in known.Keys.Where(s => !connected.Contains(s, StringComparer.Ordinal)).ToList())
        {
            _ = known.Remove(gone);
        }
    }

    /// <summary>
    /// Reads the battery level of every reachable device.
    ///
    /// All of them, not only those carrying a window: a phone
    /// waiting its turn also drains, and a gauge that would only
    /// appear once the game has launched would arrive after the
    /// decision it is meant to inform.
    ///
    /// The reading is cached for a minute by discovery, so polling
    /// every two seconds costs nothing extra.
    /// </summary>
    private async Task RefreshBatteriesAsync(
        DeviceDiscoveryResult discovery,
        CancellationToken cancellationToken)
    {
        var connected = discovery.Devices
            .Where(d => d.IsConnected)
            .Select(d => d.Serial)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Forget(_health, connected);
        Forget(_batteries, connected);
        Forget(_vitals, connected);
        Forget(_loggedMemory, connected);
        _soundAfterReopen.ForgetGone(connected);

        // The input verdict says it is "kept until the device
        // disappears", and nothing made that true: a phone that
        // refused input once kept the simulated mouse offered in the
        // settings long after it had been unplugged.
        Forget(_inputs, connected);

        foreach (var serial in connected)
        {
            var battery = await _devices.GetBatteryAsync(serial, cancellationToken).ConfigureAwait(false);

            if (battery is null)
            {
                _ = _batteries.Remove(serial);
            }
            else
            {
                _batteries[serial] = battery;
            }
        }
    }

    /// <summary>A device's readable name, or <c>null</c> if unknown.</summary>
    private static string? Named(DeviceDiscoveryResult discovery, string serial) =>
        discovery.Devices
            .FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.Ordinal))
            ?.DisplayName;

    /// <summary>Devices whose lock has already been logged.</summary>
    private readonly HashSet<string> _loggedLock = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs the lock, once per episode.
    ///
    /// The summary is redone every two seconds: without this guard,
    /// an evening spent in front of a locked phone would write the
    /// same line a thousand times.
    /// </summary>
    private void TraceLock(string serial, bool locked)
    {
        if (locked)
        {
            if (_loggedLock.Add(serial))
            {
                LogDisplayLocked(serial);
            }
        }
        else
        {
            _ = _loggedLock.Remove(serial);
        }
    }

    /// <summary>
    /// Devices whose missing preparation has already been logged.
    /// </summary>
    private readonly HashSet<string> _loggedPreparation = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs the missing preparation, once per episode.
    /// </summary>
    private void TracePreparation(string serial, bool unprepared)
    {
        if (unprepared)
        {
            if (_loggedPreparation.Add(serial))
            {
                LogUnprepared(serial);
            }
        }
        else
        {
            _ = _loggedPreparation.Remove(serial);
        }
    }

    /// <summary>
    /// True when at least one device has refused input simulation.
    ///
    /// Decides whether the last-resort remedy appears: a simulated
    /// mouse captures the PC's cursor, so it has no business in the
    /// settings of someone who does not have the problem.
    /// </summary>
    public bool AnyInputRefused => _inputs.Values.Any(v => v == InputInjection.Denied);

    /// <summary>Input verdict per device, asked once and kept.</summary>
    private readonly Dictionary<string, InputInjection> _inputs = new(StringComparer.Ordinal);

    /// <summary>
    /// This device's input verdict, asked only once.
    ///
    /// Kept until the device disappears: the setting does not
    /// change mid-session, and asking the question again would
    /// amount to sending keys while playing.
    /// </summary>
    private async Task<InputInjection> Inputs(string serial, CancellationToken cancellationToken)
    {
        if (_inputs.TryGetValue(serial, out var known))
        {
            return known;
        }

        var answer = await _devices.CheckInputInjectionAsync(serial, cancellationToken).ConfigureAwait(false);

        // An uncertain verdict is not kept: the device may have
        // been busy, and the question will be asked again at the
        // next window.
        if (answer != InputInjection.Unknown)
        {
            _inputs[serial] = answer;
        }

        return answer;
    }

    /// <summary>
    /// Devices whose input refusal has already been logged.
    /// </summary>
    private readonly HashSet<string> _loggedDeadInput = new(StringComparer.Ordinal);

    /// <summary>Logs the input refusal, once per episode.</summary>
    private void TraceDeadInput(string serial, bool dead)
    {
        if (dead)
        {
            if (_loggedDeadInput.Add(serial))
            {
                LogDeadInput(serial);
            }
        }
        else
        {
            _ = _loggedDeadInput.Remove(serial);
        }
    }

    /// <summary>The PC's processor counters at the last reading.</summary>
    private (long Idle, long Total)? _pcTimes;

    /// <summary>
    /// The PC's processor load over the last two readings, the lower of the
    /// two, in percent, or -1 until both are known. Written by the window
    /// watch, read by the sweep.
    /// </summary>
    private volatile int _pcBusy = -1;

    /// <summary>The last reading's load, before two readings are combined.</summary>
    private int _pcMinute = -1;

    /// <summary>Takes a reading of the PC's processor.</summary>
    private void SamplePc()
    {
        if (!PcProcessor.Times(out var idle, out var total))
        {
            _pcBusy = -1;
            return;
        }

        var minute = _pcTimes is { } before && total > before.Total
            ? (int)Math.Round(100.0 * (1 - ((double)(idle - before.Idle) / (total - before.Total))))
            : -1;

        // The lower of two readings in a row, as for the phone: a build or a
        // game launch fills one, a PC that stays loaded fills both.
        _pcBusy = Math.Min(_pcMinute, minute);
        _pcMinute = minute;
        _pcTimes = (idle, total);
    }

    /// <summary>
    /// The serials of the playing phones whose screen the player lit up
    /// again while DT Hub holds it off, read by the panel to show its line.
    /// Empty when the setting is off. Replaced whole, under
    /// <see cref="_litPanelsLock" />, never changed in place.
    /// </summary>
    public IReadOnlySet<string> LitPanels { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    private readonly Lock _litPanelsLock = new();

    /// <summary>
    /// Fired when a screen starts being switched off, from any thread: the
    /// line goes at once, by the shortcut as by a click, not at the next
    /// sweep.
    /// </summary>
    public event EventHandler? LitPanelsChanged;

    /// <summary>
    /// Moved on before and after each screen is switched off. A sweep that
    /// read the phones meanwhile saw the screen the old screen-off session
    /// lit up as it died, and would bring the line back: its answer is
    /// dropped.
    /// </summary>
    private int _panelGeneration;

    /// <summary>
    /// The phones whose screen is being switched off. A second press in that
    /// second finds the screen lit by the old screen-off session's death, and
    /// would replace the new one on a screen it had just darkened.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _switchingOff =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Asked at the panel's pace, and only while it shows: the line is the
    /// only thing waiting on the answer, and the shortcut asks for itself.
    ///
    /// Only the phones whose screen DT Hub holds off: during an account's
    /// first opening the screen is lit and the session that will switch it
    /// off is not started yet. And only a screen seen lit: on a phone whose
    /// answer is not understood, an older Android perhaps, the line would
    /// stay for the whole session.
    /// </summary>
    private async Task RefreshLitPanelsAsync(DeviceDiscoveryResult discovery, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _panelGeneration);

        HashSet<string> lit = new(StringComparer.Ordinal);

        if ((await _settings.GetAsync(cancellationToken).ConfigureAwait(false)).TurnScreenOff)
        {
            var held = _sessions.ActiveSessions
                .Where(s => s.IsAlive && _sessions.HoldsScreenOff(s.Target.DeviceId))
                .Select(s => discovery.Devices.FirstOrDefault(d => d.IsConnected && d.Id == s.Target.DeviceId)?.Serial
                    ?? s.Target.Serial)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var serial in held)
            {
                if (await _devices.IsPanelLitAsync(serial, cancellationToken).ConfigureAwait(false) == true)
                {
                    _ = lit.Add(serial);
                }
            }
        }

        lock (_litPanelsLock)
        {
            if (generation == _panelGeneration)
            {
                LitPanels = lit;
            }
        }
    }

    /// <summary>
    /// Switches off again the screen of a phone the player lit up, see
    /// <see cref="ScrcpySessionManager.TurnScreenOffAgainAsync" />.
    /// </summary>
    public async Task TurnPanelOffAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var serial = _sessions.ActiveSessions
            .Where(s => s.IsAlive && string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(s => Routable(s.Target))
            .FirstOrDefault();

        if (serial is null || !_switchingOff.TryAdd(deviceId, 0))
        {
            return;
        }

        try
        {
            lock (_litPanelsLock)
            {
                _panelGeneration++;
                LitPanels = new HashSet<string>(LitPanels.Where(s => s != serial), StringComparer.Ordinal);
            }

            LitPanelsChanged?.Invoke(this, EventArgs.Empty);

            await _sessions.TurnScreenOffAgainAsync(deviceId, serial, cancellationToken).ConfigureAwait(false);

            lock (_litPanelsLock)
            {
                _panelGeneration++;
            }
        }
        finally
        {
            _ = _switchingOff.TryRemove(deviceId, out _);
        }
    }

    /// <summary>
    /// The shortcut's action: the phone of the game in front, the free
    /// window or the frame's shown tab, and every playing phone when the
    /// keyboard is in another of DT Hub's windows (asked on 2026-10-15).
    /// Lit screens only, asked now rather than read from
    /// <see cref="LitPanels" />, which the panel only refreshes while it
    /// shows.
    ///
    /// **Whether or not the setting holds the screen off.** It used to do
    /// nothing at all without it, silently, and the player who pressed it
    /// could not tell why.
    ///
    /// A phone that does not say counts as lit here, unlike for the line:
    /// the player asked, and the worst is a one-second flash on a dark
    /// screen.
    /// </summary>
    private async Task TurnLitPanelsOffAsync()
    {
        var foreground = _windows.Controller.GetForegroundWindow();
        string? shown = null;

        // A housed window is a child of the frame, never in front itself.
        if (_tabs is { Handle: not 0 } frame && foreground == frame.Handle)
        {
            await OnUiAsync(() => shown = frame.Items.FirstOrDefault(t => t.IsSelected)?.Key).ConfigureAwait(false);
        }

        var alive = _sessions.ActiveSessions.Where(s => s.IsAlive).ToList();
        var front = alive.FirstOrDefault(s =>
            (foreground != 0 && s.WindowHandle == foreground)
            || string.Equals(s.Target.Key, shown, StringComparison.Ordinal));

        var phones = alive
            .Where(s => front is null || string.Equals(s.Target.DeviceId, front.Target.DeviceId, StringComparison.Ordinal))
            .GroupBy(s => s.Target.DeviceId, StringComparer.Ordinal)
            .Select(g => (DeviceId: g.Key, Serial: Routable(g.First().Target)))
            .Where(p => !_switchingOff.ContainsKey(p.DeviceId))
            .ToList();

        foreach (var (deviceId, serial) in phones)
        {
            if (await _devices.IsPanelLitAsync(serial).ConfigureAwait(false) != false)
            {
                await TurnPanelOffAsync(deviceId).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Wi-Fi latency spikes per device, fed by <see cref="SampleInBackground" />.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PingSpikes> _pings =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The last readings of each playing phone's processor, by serial, read by
    /// the health sweep.
    /// </summary>
    private volatile IReadOnlyDictionary<string, PhoneStrain> _strains = new Dictionary<string, PhoneStrain>();

    /// <summary>Each phone's last reading, before two readings are combined.</summary>
    private Dictionary<string, PhoneStrain> _minutes = new(StringComparer.Ordinal);

    private readonly System.Diagnostics.Stopwatch _sincePing = System.Diagnostics.Stopwatch.StartNew();

    private System.Diagnostics.Stopwatch? _sinceStrain;

    private readonly System.Diagnostics.Stopwatch _sinceTitle = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>
    /// Time between two battery readings for the title, the same five
    /// seconds discovery keeps one: a charger plugged in shows within that.
    /// The title is only rewritten when what it says changed.
    /// </summary>
    private static readonly TimeSpan TitleInterval = TimeSpan.FromSeconds(5);

    /// <summary>1 while a round of phone readings is under way.</summary>
    private int _sampling;

    /// <summary>
    /// Time between two processor readings, phones and PC.
    ///
    /// **Thirty seconds, no longer a minute.** The finding waits for two
    /// readings in a row, so a lag took two minutes to show, and a lag of
    /// ninety seconds was over before the panel spoke of it. One more adb
    /// call per playing phone and per minute. The limits are unchanged: the
    /// shares they compare do not depend on the length of the window.
    /// </summary>
    private static readonly TimeSpan StrainInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Measures, in the background, what the lag finding needs: a ping a
    /// second and a processor reading every <see cref="StrainInterval" />,
    /// for every phone carrying a game.
    ///
    /// **From the window watch and not from the panel's sweep**: the panel
    /// only sweeps while it shows, and the player opens it when the game
    /// lags. It would then have waited for two readings before saying
    /// anything.
    /// </summary>
    private void SampleInBackground()
    {
        if (_sincePing.Elapsed < TimeSpan.FromMilliseconds(900))
        {
            return;
        }

        _sincePing.Restart();

        List<string> phones =
        [
            .. _sessions.ActiveSessions
                .Where(s => s.IsAlive)
                .Select(s => Routable(s.Target))
                .Distinct(StringComparer.Ordinal),
        ];

        foreach (var serial in phones)
        {
            // Only the address adb reaches the phone at right now, so it is
            // alive and on Wi-Fi by definition. Not a USB serial, nor an mDNS
            // name: the address remembered for those may be days old, and a
            // ping lost to a moved phone would read as a spike.
            if (PingSpikes.HostOf(serial) is { } host)
            {
                _ = PingAsync(host, _pings.GetOrAdd(serial, _ => new PingSpikes()));
            }
        }

        // A serial no longer playing, an old port after wireless debugging
        // resumed for instance, would otherwise stay forever.
        foreach (var gone in _pings.Keys.Where(k => !phones.Contains(k, StringComparer.Ordinal)))
        {
            _ = _pings.TryRemove(gone, out _);
            _ = _spiky.TryRemove(gone, out _);
        }

        var strainDue = _sinceStrain is null || _sinceStrain.Elapsed >= StrainInterval;

        // Off the interface thread, which starts adb otherwise, and never
        // two at once: a phone slow to answer would have the next round
        // read the same counters concurrently. A strain round that finds a
        // title round under way waits for the next tick, not thirty seconds.
        if ((strainDue || _sinceTitle.Elapsed >= TitleInterval) && Interlocked.Exchange(ref _sampling, 1) == 0)
        {
            _sinceTitle.Restart();

            if (strainDue)
            {
                _sinceStrain = System.Diagnostics.Stopwatch.StartNew();

                SamplePc();
            }

            IReadOnlyList<string> serials = phones;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (strainDue)
                    {
                        await SampleStrainsAsync(serials).ConfigureAwait(false);
                    }

                    // Rewritten on every strain round regardless: the lag
                    // and the play time only change there.
                    await RetitleWithStatusAsync(serials, strainDue).ConfigureAwait(false);
                }
                finally
                {
                    _ = Interlocked.Exchange(ref _sampling, 0);
                }
            });
        }
    }

    /// <summary>
    /// One processor reading per phone, the strain of each since the previous
    /// one, and a log line for each: the limits were set
    /// on two phones, one lagging and one fresh, and only the field can say
    /// whether they hold.
    /// </summary>
    private async Task SampleStrainsAsync(IReadOnlyList<string> serials)
    {
        Dictionary<string, PhoneStrain> minutes = new(StringComparer.Ordinal);
        Dictionary<string, PhoneStrain> strains = new(StringComparer.Ordinal);

        foreach (var serial in serials)
        {
            if (await _devices.GetPhoneStrainAsync(serial).ConfigureAwait(false) is { } strain)
            {
                minutes[serial] = strain;

                // Shown only once two readings in a row agree: one busy
                // half minute is not a lag.
                if (_minutes.TryGetValue(serial, out var before))
                {
                    strains[serial] = PhoneStrain.Sustained(before, strain);
                }

                LogStrain(
                    serial,
                    Math.Round(strain.BusyShare * 100),
                    Math.Round(strain.GameShare * 100),
                    Math.Round(strain.ReclaimCore * 100),
                    Math.Round(strain.Uptime.TotalDays, 1));
            }

            if (await _devices.GetGameSoundAsync(serial).ConfigureAwait(false) is { } sound)
            {
                WatchSound(serial, sound);

                if (_soundAfterReopen.Read(serial, sound, DateTimeOffset.UtcNow))
                {
                    await LogStuckSoundAsync(serial).ConfigureAwait(false);
                }
            }

            WatchPings(serial);
        }

        // Rebuilt whole: a phone no longer playing must not keep its last
        // reading on screen.
        _minutes = minutes;
        _strains = strains;
    }

    /// <summary>Phones whose Wi-Fi spikes are being reported, by serial.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _spiky = new(StringComparer.Ordinal);

    /// <summary>
    /// Writes in the log when a phone's Wi-Fi starts spiking, at the panel's
    /// own threshold, and when it stops.
    ///
    /// **Why.** The panel showed spikes up to 350 ms on 2026-10-17 and none
    /// could be reproduced an hour later: they were never written anywhere,
    /// so neither their frequency nor what came with them could be read.
    /// </summary>
    private void WatchPings(string serial)
    {
        if (!_pings.TryGetValue(serial, out var spikes))
        {
            return;
        }

        var (count, worst) = spikes.Since(DateTimeOffset.UtcNow);

        if (count >= PingSpikes.Enough)
        {
            if (_spiky.TryAdd(serial, 0))
            {
                LogPingSpikes(serial, count, worst);
            }
        }
        else if (_spiky.TryRemove(serial, out _))
        {
            LogPingSpikesOver(serial);
        }
    }

    private readonly SoundAfterReopen _soundAfterReopen = new();

    /// <summary>
    /// Writes what the phone says of its sound once it is found stuck since
    /// a reopen, beside the windows DT Hub has open on it: a scrcpy server
    /// more than these is one no window owns.
    /// </summary>
    private async Task LogStuckSoundAsync(string serial)
    {
        var windows = _sessions.ActiveSessions.Count(s =>
            !s.Target.IsTransient && string.Equals(Routable(s.Target), serial, StringComparison.Ordinal));

        try
        {
            var state = await _adb.ShellAsync(serial, [SoundAfterReopen.Probe], TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            LogSoundStuck(serial, windows, state.Trim());
        }
        catch (AdbException exception)
        {
            LogSoundStuck(serial, windows, exception.UserMessage);
        }
    }

    /// <summary>Silent readings in a row, by serial.</summary>
    private readonly Dictionary<string, int> _silentReadings = new(StringComparer.Ordinal);

    /// <summary>Readings in a row where the game played and nothing took it for the PC.</summary>
    private readonly Dictionary<string, int> _uncapturedReadings = new(StringComparer.Ordinal);

    /// <summary>
    /// Three readings, a minute at least: a loading screen is silent too,
    /// and one silent reading is not a lost sound.
    /// </summary>
    private const int SoundReadingsBeforeWarning = 3;

    /// <summary>
    /// Writes in the log the moment the game's sound goes, and the moment it
    /// comes back.
    ///
    /// **Why.** The player lost the sound now and then, on the PC and on the
    /// phone alike, and relaunching DT Hub brought it back (2026-10-16). The
    /// moment it went was recorded nowhere, and the lines around it are what
    /// says why: a reconnection, a window reopened, the screen turned off.
    /// Nothing is shown: a game whose sound the player turned off in its own
    /// options is silent too.
    /// </summary>
    private void WatchSound(string serial, GameSound sound)
    {
        if (Count(_silentReadings, serial, sound.IsSilent) == SoundReadingsBeforeWarning)
        {
            LogGameSilent(serial, sound.GameProcesses, sound.Captured, sound.MediaVolume?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?");
        }

        var carried = _sessions.ActiveSessions.Any(s =>
            s.IsAlive
            && (s.CarriesAudio || _sessions.HoldsSound(s.Target.DeviceId))
            && string.Equals(Routable(s.Target), serial, StringComparison.Ordinal));

        if (Count(_uncapturedReadings, serial, carried && sound.GamePlayers > 0 && !sound.Captured)
            == SoundReadingsBeforeWarning)
        {
            LogSoundNotCaptured(serial, sound.GamePlayers);
        }
    }

    /// <summary>
    /// Counts the readings in a row where <paramref name="lost" /> holds, and
    /// says in the log when it stops holding after a warning.
    /// </summary>
    private int Count(Dictionary<string, int> readings, string serial, bool lost)
    {
        var before = readings.GetValueOrDefault(serial);

        if (!lost)
        {
            if (before >= SoundReadingsBeforeWarning)
            {
                LogSoundBack(serial);
            }

            _ = readings.Remove(serial);
            return 0;
        }

        return readings[serial] = before + 1;
    }

    /// <summary>
    /// Battery and heat last read for the windows' title, by the serial
    /// <see cref="Routable" /> gives.
    /// </summary>
    private volatile IReadOnlyDictionary<string, (BatteryReading? Battery, ThermalReading? Heat)> _titleReadings =
        new Dictionary<string, (BatteryReading? Battery, ThermalReading? Heat)>();

    /// <summary>
    /// Rereads the battery and the heat, then rewrites the windows' title,
    /// when the player asked for the phone's state there.
    ///
    /// **From this background round and not from the panel's sweep**: the
    /// sweep stops while the panel is hidden, which is while the player
    /// plays. Discovery caches the battery five seconds and the heat thirty,
    /// so this costs two adb calls per phone and per round at most, and
    /// nothing when the option is off.
    ///
    /// The windows are only retitled when the battery or heat they show
    /// changed, or when <paramref name="always" /> asks it.
    /// </summary>
    private async Task RetitleWithStatusAsync(IReadOnlyList<string> serials, bool always)
    {
        try
        {
            var settings = await _settings.GetAsync(CancellationToken.None).ConfigureAwait(false);

            if (!settings.TitleShowsStatus)
            {
                return;
            }

            Dictionary<string, (BatteryReading? Battery, ThermalReading? Heat)> readings = new(StringComparer.Ordinal);

            foreach (var serial in serials)
            {
                readings[serial] = (
                    await _devices.GetBatteryAsync(serial).ConfigureAwait(false),
                    await _devices.GetThermalAsync(serial).ConfigureAwait(false));
            }

            var before = _titleReadings;

            _titleReadings = readings;

            if (always || !SameShown(before, readings))
            {
                _ = await RefreshWindowTitlesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogTitleStatusFailure(exception);
        }
    }

    /// <summary>
    /// True when two sets of readings would put the same battery and heat in
    /// the titles: the temperature to the tenth moves on every reading and
    /// must not have every window rewritten.
    /// </summary>
    private static bool SameShown(
        IReadOnlyDictionary<string, (BatteryReading? Battery, ThermalReading? Heat)> before,
        Dictionary<string, (BatteryReading? Battery, ThermalReading? Heat)> after) =>
        before.Count == after.Count
        && after.All(pair => before.TryGetValue(pair.Key, out var old)
            && ScrcpyCommandBuilder.BuildTitleStatus(old.Battery, old.Heat, false, TimeSpan.Zero)
                == ScrcpyCommandBuilder.BuildTitleStatus(pair.Value.Battery, pair.Value.Heat, false, TimeSpan.Zero));

    /// <summary>What a session's title says of its phone, after the account's name.</summary>
    private string StatusOf(ScrcpySession session)
    {
        var serial = Routable(session.Target);
        var (battery, heat) = _titleReadings.GetValueOrDefault(serial);

        return ScrcpyCommandBuilder.BuildTitleStatus(
            battery,
            heat,
            _strains.GetValueOrDefault(serial)?.Describe() is not null,
            DateTimeOffset.UtcNow - session.StartedUtc);
    }

    /// <summary>
    /// The serial to ask a session's phone through: the one discovery last
    /// kept for it, rather than the mDNS name the session may have been
    /// opened on, which adb often refuses.
    /// </summary>
    private string Routable(LaunchTarget target) =>
        _serials.GetValueOrDefault(target.DeviceId) ?? target.Serial;

    private static async Task PingAsync(string host, PingSpikes spikes)
    {
        using var ping = new System.Net.NetworkInformation.Ping();

        try
        {
            var reply = await ping.SendPingAsync(host, PingSpikes.Timeout).ConfigureAwait(false);

            spikes.Add(
                DateTimeOffset.UtcNow,
                reply.Status == System.Net.NetworkInformation.IPStatus.Success ? reply.RoundtripTime : null);
        }
        catch (System.Net.NetworkInformation.PingException)
        {
            // The PC has no network to send it on: that says nothing about
            // the phone's Wi-Fi, and the lost session already shows.
        }
    }

    /// <summary>
    /// Logs Android's memory verdict whenever it changes, back to normal
    /// included: the next "it lags after an hour" will show in the log
    /// whether the phone was short of memory at the time.
    /// </summary>
    private void TraceMemory(string serial, MemoryReading? memory)
    {
        if (memory is null)
        {
            return;
        }

        if (_loggedMemory.TryGetValue(serial, out var already) ? already != memory.Level : memory.Level != MemoryPressure.Normal)
        {
            _loggedMemory[serial] = memory.Level;
            LogMemory(serial, memory.Level.ToString(), memory.AvailableGigabytes);
        }
    }

    /// <summary>Logs the tiers, and only when they change.</summary>
    private void Trace(
        string serial,
        ThermalReading? heat,
        BatteryReading? battery,
        StorageReading? storage)
    {
        if (heat?.Describe() is not null)
        {
            if (!_loggedHeat.TryGetValue(serial, out var already) || already != heat.Status)
            {
                _loggedHeat[serial] = heat.Status;
                LogHeat(serial, heat.Status, heat.SkinCelsius ?? 0);
            }
        }
        else
        {
            _ = _loggedHeat.Remove(serial);
        }

        if (battery?.Describe() is not null)
        {
            // The tier, not the percentage: logging every lost
            // point would make eighty lines per session.
            var band = battery.Percent <= BatteryReading.Critical ? 2 : 1;

            if (!_loggedBattery.TryGetValue(serial, out var already) || already != band)
            {
                _loggedBattery[serial] = band;
                LogBattery(serial, battery.Percent, battery.Celsius ?? 0);
            }
        }
        else
        {
            _ = _loggedBattery.Remove(serial);
        }

        if (storage?.Describe() is not null)
        {
            if (_loggedStorage.Add(serial))
            {
                LogStorage(serial, storage.FreeGigabytes);
            }
        }
        else
        {
            _ = _loggedStorage.Remove(serial);
        }
    }

    /// <summary>
    /// Reconnects everything that can be, without intervention.
    ///
    /// Two complementary paths. First, everything announcing itself
    /// on the network: the announcement carries the serial number
    /// and the current address, and ADB keeps the pairing key, so a
    /// change of address or port does not matter. Then, remembered
    /// devices that still are not responding, through their last
    /// known address.
    ///
    /// Spaced out over time: no need to poll the network on every
    /// refresh of the list.
    /// </summary>
    private async Task EnsureKnownDevicesConnectedAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _lastReconnectAttempt < ReconnectInterval)
        {
            return;
        }

        _lastReconnectAttempt = DateTimeOffset.UtcNow;

        try
        {
            // Cleanup first: a dead connection skews the list and
            // can hide the phone that is actually reachable.
            await _devices.PruneStaleWirelessTransportsAsync(cancellationToken).ConfigureAwait(false);

            var live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

            var addresses = live.Devices
                .Where(d => d.IsConnected)
                .Select(d => d.Serial)
                .ToHashSet(StringComparer.Ordinal);

            // An attempt on an announcement only succeeds for a
            // phone already paired with this PC: ADB refuses the
            // others. Care must still be taken not to pick back up
            // the one whose pairing was just broken, which keeps
            // announcing itself and whose key ADB still holds.
            var (known, discarded) = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

            // **Scanning announcements only serves to find what is
            // missing.** With everything connected, it still polled
            // the network every five seconds, that is twelve times
            // a minute while playing. It is spaced out in that case,
            // not removed: it also catches a phone paired long ago
            // that starts announcing itself again.
            //
            // A phone gone for over a day does not count as missing here:
            // one paired a month ago and left in a drawer forced the scan on
            // every pass. It is still found by its own announcement, at the
            // idle pace, and asked at its last address now and then below.
            var now = DateTimeOffset.UtcNow;
            var awake = known.Where(d => !DeviceDormancy.IsDormant(d.LastSeenUtc, now)).ToList();

            var whole = known.Count > 0
                && awake.All(d => live.Devices.Any(l =>
                    l.IsConnected && string.Equals(l.Id, d.Id, StringComparison.Ordinal)));

            var opened = new AnnouncedConnections([], []);

            if (!whole || DateTimeOffset.UtcNow - _lastAnnouncedScan >= IdleScanInterval)
            {
                _lastAnnouncedScan = DateTimeOffset.UtcNow;

                opened = await _pairing
                    .ConnectAnnouncedAsync(addresses, discarded, cancellationToken)
                    .ConfigureAwait(false);
            }

            var recovered = opened.Connected.Count;

            // Two paths lead to the same finding: a device that
            // announces itself and refuses, and a remembered device
            // whose address answers but declines the handshake.
            // Refusals from both paths are merged before being
            // counted, otherwise one would erase the other.
            List<string> refused = [.. opened.Refused];

            if (known.Count > 0)
            {
                if (recovered > 0)
                {
                    live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }

                var connected = live.Devices
                    .Where(d => d.IsConnected)
                    .Select(d => d.Id)
                    .ToHashSet(StringComparer.Ordinal);

                // Dormant phones only every few minutes: each costs an adb
                // connect with a five-second deadline the sweep waits on.
                var dormantDue = now - _lastDormantAttempt >= DeviceDormancy.RetryEvery;

                var missing = known
                    .Where(d => !connected.Contains(d.Id))
                    .Where(d => dormantDue || !DeviceDormancy.IsDormant(d.LastSeenUtc, now))
                    .ToList();

                if (dormantDue && missing.Any(d => DeviceDormancy.IsDormant(d.LastSeenUtc, now)))
                {
                    _lastDormantAttempt = now;
                }

                if (missing.Count > 0)
                {
                    var outcomes = await _reconnect.TryReconnectAllAsync(missing, cancellationToken)
                        .ConfigureAwait(false);

                    recovered += outcomes.Count(o => o.Value is ReconnectOutcome.ReconnectedToLastAddress
                                                     or ReconnectOutcome.ReconnectedByDiscovery);

                    refused.AddRange(outcomes
                        .Where(o => o.Value == ReconnectOutcome.RefusedByDevice)
                        .Select(o => o.Key));
                }
            }

            NoteRefusals(refused);

            if (recovered > 0)
            {
                LogReconnected(recovered);
            }
        }
        catch (AdbException)
        {
            // Nothing to reconnect if ADB itself is unavailable: the
            // next sweep will report it.
        }
    }

    /// <summary>
    /// Devices that announce themselves and refuse this PC, and for
    /// how many attempts.
    /// </summary>
    private readonly Dictionary<string, int> _refusals = new(StringComparer.Ordinal);

    /// <summary>
    /// Two refusals in a row before concluding anything. A single
    /// attempt can land at the wrong moment, on a phone that just
    /// changed port or is powering off; two refusals on two fresh
    /// announcements, not so.
    /// </summary>
    private const int RefusalsBeforeDoubt = 2;

    /// <summary>
    /// Devices whose pairing needs redoing: they announce
    /// themselves on the network and refuse this PC's key.
    /// </summary>
    public IReadOnlySet<string> NeedsPairing { get; private set; } =
        new HashSet<string>(StringComparer.Ordinal);

    private void NoteRefusals(IReadOnlyList<string> refused)
    {
        foreach (var gone in _refusals.Keys.Where(s => !refused.Contains(s, StringComparer.Ordinal)).ToList())
        {
            _ = _refusals.Remove(gone);
        }

        foreach (var serial in refused)
        {
            _refusals[serial] = _refusals.GetValueOrDefault(serial) + 1;
        }

        NeedsPairing = _refusals
            .Where(pair => pair.Value >= RefusalsBeforeDoubt)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Pace of the announcement scan when all known devices answer.
    /// It then has nothing to find, and half a minute is enough to
    /// catch up with a phone that reappears on its own.
    /// </summary>
    private static readonly TimeSpan IdleScanInterval = TimeSpan.FromSeconds(30);

    private DateTimeOffset _lastAnnouncedScan = DateTimeOffset.MinValue;

    private DateTimeOffset _lastDormantAttempt = DateTimeOffset.MinValue;

    private DateTimeOffset _lastReconnectAttempt = DateTimeOffset.MinValue;

    /// <summary>
    /// Opens every checked instance, then stacks their windows. An
    /// instance whose phone is absent is reported without
    /// preventing the others from opening.
    /// </summary>
    public async Task<LaunchReport> LaunchEnabledAsync(CancellationToken cancellationToken = default)
    {
        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);

        // **Nothing checked: nothing to discover.**
        //
        // Rediscovery costs two commands per profile on every
        // phone, and it used to run every time before even looking
        // whether there was anything to open. Measured at startup:
        // three seconds during which the screen stayed empty, only
        // to conclude "No instance is ticked.", an answer the
        // settings already gave.
        //
        // Discovery cannot change that answer: an account it has
        // just found is never checked, "StoredInstance.IsEnabled"
        // defaulting to false. Reading the settings is therefore
        // enough, and always will be.
        var stored = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        if (!stored.Instances.Exists(i => i.IsEnabled))
        {
            return new LaunchReport(0, [Strings.Get("NoInstanceChecked")]);
        }

        var instances = await RefreshInstancesAsync(cancellationToken).ConfigureAwait(false);
        var enabled = instances.Where(i => i.IsEnabled).ToList();

        if (enabled.Count == 0)
        {
            return new LaunchReport(0, [Strings.Get("NoInstanceChecked")]);
        }

        return await LaunchAsync(enabled, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a specific list of instances.</summary>
    public Task<LaunchReport> LaunchAsync(
        IReadOnlyList<DofusInstance> instances,
        CancellationToken cancellationToken = default) =>
        LaunchAsync(instances, remembered: null, cancellationToken);

    /// <summary>
    /// Opens a list of instances, optionally with geometry captured
    /// on the spot rather than the one from settings.
    /// </summary>
    private async Task<LaunchReport> LaunchAsync(
        IReadOnlyList<DofusInstance> instances,
        IReadOnlyDictionary<string, StoredWindowRect>? remembered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);

        // A launch is when the player has most likely just been in the
        // phone's settings: the battery question is asked again at the next
        // sweep, not up to fifteen seconds later.
        foreach (var deviceId in instances.Select(i => i.DeviceId).Distinct(StringComparer.Ordinal))
        {
            if (_serials.TryGetValue(deviceId, out var launchedOn))
            {
                _devices.ForgetBatteryExemption(launchedOn);
            }
        }

        // The preamble is timed because it is visible: nothing opens
        // during that time, and the other accounts' buttons wait.
        // Without these three numbers, "this takes too long to
        // unblock" can only be fixed by guesswork.
        var preambule = System.Diagnostics.Stopwatch.StartNew();

        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);

        var raccourcis = preambule.ElapsedMilliseconds;

        // Devices are resolved before the window settings, not
        // after: the link is drawn from them, and it is the link
        // that bounds the quality these settings are about to set.
        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        var appareils = preambule.ElapsedMilliseconds;

        await RefreshLinkAsync(instances, devices, cancellationToken).ConfigureAwait(false);

        var liaison = preambule.ElapsedMilliseconds;

        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var options = await _settings.GetScrcpyOptionsAsync(cancellationToken).ConfigureAwait(false);

        LogPreamble(
            raccourcis,
            appareils - raccourcis,
            liaison - appareils,
            preambule.ElapsedMilliseconds - liaison,
            preambule.ElapsedMilliseconds);

        options = options with
        {
            WindowTitleHint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false),
            IconDirectory = _iconDirectory,
        };

        Dictionary<string, double?> phoneAspects = new(StringComparer.Ordinal);

        // The position is given to scrcpy right at launch. Doing it
        // afterwards is not enough: scrcpy recenters its window when
        // it receives the first frame, that is after our placement.
        remembered ??= await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

        List<string> problems = [];
        // One read for the whole launch: the table does not change
        // while the windows are opening.
        var zooms = await _settings
            .GetInstanceZoomsAsync(cancellationToken)
            .ConfigureAwait(false);

        var qualities = await _settings
            .GetInstanceQualitiesAsync(cancellationToken)
            .ConfigureAwait(false);

        var ownZooms = await _settings
            .GetAccountsWithOwnZoomAsync(cancellationToken)
            .ConfigureAwait(false);

        var phoneFormats = await _settings
            .GetAccountsWithPhoneFormatAsync(cancellationToken)
            .ConfigureAwait(false);

        ProbeEncoders(instances, devices);

        List<ScrcpySession> started = [];

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Remembered before any filter: this is how we will
            // know what to reopen if the window drops.
            _launched[instance.Key] = instance;

            if (IsOpen(instance))
            {
                continue;
            }

            if (!devices.TryGetValue(instance.DeviceId, out var device))
            {
                problems.Add(Strings.Format("PhoneNotConnectedNamed", instance.DisplayName));
                continue;
            }

            // The API level is known from discovery. Reading it
            // here avoids waiting for scrcpy's full timeout on a
            // device we already know will not create a virtual
            // display.
            if (AndroidRequirements.DescribeVirtualDisplayShortfall(
                    device.SdkVersion, device.AndroidVersion) is { } shortfall)
            {
                problems.Add($"{instance.DisplayName} : {shortfall}");
                LogAndroidTooOld(instance.DisplayName, device.SdkVersion ?? 0);
                continue;
            }

            _sdks[instance.DeviceId] = device.SdkVersion;

            remembered.TryGetValue(instance.Key, out var stored);

            // The game at its phone's own screen ratio when the account asks
            // for it, see PhoneScreen: the window is shaped on it too. By
            // default, and unread, the screen's ratio, as before 0.8.10.
            // Never for a window opened in passing, which has its own.
            double? phoneAspect = null;

            if (!instance.IsTransient
                && phoneFormats.Contains(instance.Key)
                && !phoneAspects.TryGetValue(device.Serial, out phoneAspect))
            {
                phoneAspect = await ReadPhoneAspectAsync(device, cancellationToken).ConfigureAwait(false);
                phoneAspects[device.Serial] = phoneAspect;
            }

            var gameOptions = phoneAspect is { } ratio
                ? options with
                {
                    VirtualDisplayWidth = (int)Math.Round(DisplayLadder.FallbackHeight * ratio),
                    VirtualDisplayHeight = DisplayLadder.FallbackHeight,
                }
                : options;

            // A window opened in passing gets a portrait frame of its own,
            // never remembered. Placed by us like a game window, and not
            // left to scrcpy: measured on 2026-10-10, a window scrcpy sized
            // alone ignored clicks until it was resized by hand.
            var placement = instance.IsTransient
                ? ComputePlacement(options with { VirtualDisplayWidth = 1080, VirtualDisplayHeight = 1920 }, remembered: null)
                : ComputePlacement(gameOptions, stored);

            var target = ToTarget(instance, device.Serial);

            // The account's own tier, or the shared one if none was chosen.
            var quality = qualities.TryGetValue(instance.Key, out var own) ? own : _quality;

            // The account's own distance, or the shared one if not chosen.
            var zoom = zooms.TryGetValue(instance.Key, out var ownZoom) ? ownZoom : _zoom;

            // The density typed in the custom quality takes the global
            // distance's place, never an account's own distance: that one
            // was set for this account on purpose. Asked of the accounts
            // that chose one, not of the effective map, which holds every
            // account: the density was ignored everywhere (D192).
            var layoutHeight = ownZooms.Contains(instance.Key) ? 0 : quality.LayoutHeight;

            _zoomsInUse[instance.Key] = zoom;

            // A new window is measured anew: an old reduction would show
            // on the row until then, though fixed on the phone meanwhile.
            // Its banner goes with it, which the clean measure could no
            // longer take away once the reduction is gone.
            lock (_reducedRenders)
            {
                ForgetReducedRender(instance.Key);
            }

            var display = WithDisplayFor(gameOptions, placement, stored, quality, zoom, layoutHeight, phoneAspect ?? 0) with
            {
                // Specific to the device: two phones on different
                // bands do not need the same buffer.
                VideoBufferMs = _videoBuffers.TryGetValue(device.Serial, out var buffer)
                    ? buffer
                    : VideoBuffer.None,
            };

            // The encoder is only forced if the device would put
            // software ahead of hardware, and only if this is
            // already known: the list is requested in the
            // background and serves the next launch.
            if (_encoders.TryGetValue(device.Serial, out var known)
                && ScrcpyEncoders.Force(known, display.VideoCodec ?? "h264") is { } forced)
            {
                display = display with { VideoEncoder = forced };
            }

            // The audio source depends on the phone's API level, and
            // discovery has already read it. Below Android 13 the
            // playback source does not exist and asking for it removes
            // the sound instead of falling back.
            display = display with { DeviceSdkVersion = device.SdkVersion };

            // The captured sound is that of the whole phone:
            // Android cannot isolate it per application. Only one
            // session per device therefore carries it, the first
            // one opened. Granting it to all of them would give the
            // same stream in several copies, that is to say an echo.
            // When that window closes first, a windowless session takes
            // the sound over for the others, see PassSoundOnAsync.
            if (display.AudioEnabled && HasOpenSessionOn(instance.DeviceId))
            {
                display = display with { AudioEnabled = false };
            }

            // The Play Store and the MakeInTouch apps are phone apps, made
            // for a portrait screen. Measured on 2026-10-10 on the 13T Pro:
            // on the game's 1920x1080 landscape display, the store's details
            // page broke its own layout, text wrapping letter by letter; on
            // 1080x1920 at 320 dpi, it is the ordinary page.
            if (instance.IsTransient)
            {
                // Never the sound either: a phone's sound goes to its first
                // window, and the store must not take it from the game
                // opened next.
                display = display with
                {
                    VirtualDisplayWidth = 1080,
                    VirtualDisplayHeight = 1920,
                    VirtualDisplayDpi = 320,
                    AudioEnabled = false,
                    WindowTitleHint = null,
                    RightClickBack = false,
                };
            }

            // Reserved until the session is registered: two openings of the
            // same account, the startup's and a click on its row, both
            // passed IsOpen while waiting for the phone, and the second
            // window turned the first one gray.
            lock (_openingGames)
            {
                if (IsOpen(instance) || !_openingGames.Add(instance.Key))
                {
                    continue;
                }
            }

            ScrcpySession session;

            try
            {
                session = await _sessions.StartAsync(
                    target, display, placement, cancellationToken).ConfigureAwait(false);

                // Video encoders announce a maximum resolution, which
                // varies from one device to another: a modest tablet may
                // cap at 1280x720 where a recent phone goes up to 8K.
                // Rather than give up, we step down through the fallback
                // tiers.
                //
                // Only for refusals a lower resolution can fix: an
                // unplugged phone will stay unplugged, and every attempt
                // costs the full wait.
                while (session.State == ScrcpySessionState.Failed
                    && ScrcpyOutputParser.CanRetrySmaller(session.FailureKind)
                    && DisplayLadder.Below(
                           display.VirtualDisplayHeight,
                           display.VirtualDisplayWidth,
                           display.VirtualDisplayHeight) is { } smaller)
                {
                    LogDisplayFallback(instance.DisplayName, display.VirtualDisplayHeight, smaller.Height);

                    display = display with
                    {
                        VirtualDisplayWidth = smaller.Width,
                        VirtualDisplayHeight = smaller.Height,
                        VirtualDisplayDpi = ZoomProfile.DpiFor(smaller.Height, zoom, layoutHeight),
                        // Never above the bitrate already chosen: with the
                        // sharper image, it follows the window, not the
                        // display, and the smaller display would raise it.
                        VideoBitrateKbps = Math.Min(
                            display.VideoBitrateKbps,
                            quality.BitrateFor(smaller.Width, smaller.Height)),
                    };

                    session = await _sessions.StartAsync(
                        target, display, placement, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_openingGames)
                {
                    _ = _openingGames.Remove(instance.Key);
                }
            }

            // Not alive rather than failed: a window closed while its game
            // was opening comes back Stopped, and counted as opened it was
            // placed, housed and set back to enabled over the user's close.
            if (!session.IsAlive)
            {
                if (session.State == ScrcpySessionState.Failed)
                {
                    problems.Add($"{instance.DisplayName} : {session.FailureMessage}");

                    // Without scrcpy's output, a refusal boils down to
                    // "the session could not open", which helps no one.
                    LogSessionFailure(
                        instance.DisplayName,
                        session.CommandLine,
                        string.Join(Environment.NewLine, session.RecentOutput));
                }

                continue;
            }

            LogStartupTiming(instance.DisplayName, session.DisplayReadyMs, session.StartupMs);

            // The resolution and bitrate settled on: they depend on
            // the window, the screen and the quality tier, and until
            // now could be read nowhere. This is also what lets one
            // check that the bitrate does follow the resolution.
            LogStreamSettings(
                instance.DisplayName,
                display.VirtualDisplayWidth,
                display.VirtualDisplayHeight,
                display.VirtualDisplayDpi,
                display.MaxFps,
                display.VideoBitrateKbps);

            started.Add(session);

            // The game only: a tool window is a phone app of its own.
            if (!session.Target.IsTransient && session.Target.StartUri is null && session.VirtualDisplayId is not null)
            {
                _ = RecordGameWindowAsync(session);
            }

            // Here and not only from the tools menu: a recovery reopens a
            // tool window through this same path, and without its keep-awake
            // the app froze within two minutes of the phone dozing.
            if (session.Target.IsTransient)
            {
                _ = KeepAwakeAsync(session);
            }
        }

        // Only the windows that just opened are placed. Repositioning
        // the others would tear them away from where the user put
        // them, and would make Android recreate their virtual
        // display.
        if (started.Count > 0)
        {
            await _windows.RestoreAsync(started, remembered, cancellationToken).ConfigureAwait(false);

            // Tabbed accounts join the frame. After placement:
            // attaching them first would cause an already housed
            // window to be repositioned, which would jump out of the
            // frame only to come back into it.
            foreach (var session in started.Where(s => _tabbed.Contains(s.Target.Key)))
            {
                await AttachToTabsAsync(session, cancellationToken).ConfigureAwait(false);
            }

            // The account's mark on the window's own frame. After the
            // docking, because a window that has just joined the frame
            // no longer has a frame of its own to carry it.
            await OnUiAsync(() => RefreshWindowColours()).ConfigureAwait(false);

            // Now that the windows have their handle: the key of each
            // account goes into its title.
            _ = await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);
        }

        // What has just opened will reopen at the next launch. Only
        // the "Close" button removes an instance from this set:
        // closing a game window by hand should change nothing here.
        // A window opened in passing, the Play Store's or a MakeInTouch
        // app's, is never part of that set.
        if (started.Where(s => !s.Target.IsTransient).Select(s => s.Target.Key).ToList() is { Count: > 0 } kept)
        {
            await _settings.SetInstancesEnabledAsync(kept, enabled: true, cancellationToken)
                .ConfigureAwait(false);
        }

        LogLaunch(started.Count, problems.Count);

        return new LaunchReport(started.Count, problems);
    }

    /// <summary>
    /// Closes then reopens an instance, without touching the
    /// others. The game is properly stopped on the device: without
    /// this it would resume in the state it was in, and the restart
    /// would have served no purpose.
    /// </summary>
    public async Task<LaunchReport> RestartAsync(
        DofusInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FindSession(instance) is not { } session)
        {
            return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
        }

        // Only the game restarts: the scrcpy session and its window
        // are kept, and the user sees nothing flicker.
        var restart = await _restarts.RestartAsync(session, cancellationToken).ConfigureAwait(false);

        if (restart.Outcome == AppRestartOutcome.Restarted)
        {
            // The game started again in the same window: measured again,
            // or a reduction just fixed would stay on the row.
            if (!session.Target.IsTransient && session.Target.StartUri is null)
            {
                _ = RecordGameWindowAsync(session);
            }

            return new LaunchReport(1, []);
        }

        // The short restart failed: everything is closed and
        // reopened, which remains the last useful resort.
        LogRestartFallback(instance.DisplayName, restart.UserMessage ?? "afficheur inconnu");

        await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);

        return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts the account's game back in Android's standard game mode, then
    /// restarts it, the mode only applying at the start. When the phone
    /// refuses, says how to do it on the phone itself.
    /// </summary>
    public async Task<LaunchReport> RestoreStandardGameModeAsync(
        DofusInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Closed between the panel and the click: nothing left to fix.
        if (FindSession(instance) is null)
        {
            return new LaunchReport(0, []);
        }

        // Taken as fixed right away, before the phone is even asked: the
        // measure that confirms it comes ten seconds after the restart, and
        // a warning showing until then, or brought back by the list's sweep
        // during the adb call, made a player click a second time
        // (2026-10-18). Still reduced, the measure brings it back, as having
        // not helped; refused, it is put back at once.
        int? percent;

        lock (_reducedRenders)
        {
            percent = ReducedRenderPercent(instance.Key);
            ForgetReducedRender(instance.Key);
        }

        var report = await SetGameModeAsync(instance, "standard", cancellationToken).ConfigureAwait(false);

        if (report.Problems.Count > 0)
        {
            if (percent is { } kept)
            {
                _reducedRenders[instance.Key] = kept;
            }

            return report;
        }

        return await RestartAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops an account's reduction, and the banner with it when the banner
    /// names this account: it would otherwise keep saying it for its two
    /// minutes, another account still reduced or not. That one keeps its
    /// row's chip. Called under the lock on <see cref="_reducedRenders" />.
    /// </summary>
    private void ForgetReducedRender(string key)
    {
        if (_reducedRenders.TryRemove(key, out _) && key == ReducedRenderKey)
        {
            _reducedRender = default;
        }
    }

    /// <summary>
    /// Puts the account's game in one of the phone's game modes, chosen in
    /// its settings. Applies at the game's next start, which is left to the
    /// player: restarting disconnects the character.
    /// </summary>
    public async Task<LaunchReport> SetGameModeAsync(
        DofusInstance instance,
        string mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var serial = FindSession(instance)?.Serial ?? _serials.GetValueOrDefault(instance.DeviceId);

        if (serial is null
            || !await _appLauncher
                .SetGameModeAsync(serial, instance.UserId, instance.PackageName, mode, cancellationToken)
                .ConfigureAwait(false))
        {
            return new LaunchReport(0, [Strings.Format("GameModeRefused", instance.DisplayName)]);
        }

        LogGameModeSet(instance.DisplayName, mode);

        if (mode == "standard")
        {
            _setStandard[instance.Key] = true;
        }
        else
        {
            _ = _setStandard.TryRemove(instance.Key, out _);
        }

        if (_gameModes.TryGetValue(instance.Key, out var known))
        {
            _gameModes[instance.Key] = known with { Current = mode };
        }

        return new LaunchReport(0, []);
    }

    /// <summary>
    /// Closes an instance, and removes it from the next launch.
    ///
    /// This is the only action that removes it: closing the game
    /// window by hand leaves it in the set and it will reopen.
    /// </summary>
    public async Task StopAsync(DofusInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FindSession(instance) is { } session)
        {
            // Geometry is captured before closing: otherwise the
            // window would reopen elsewhere the day it is relaunched.
            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        await _settings.SetInstancesEnabledAsync([instance.Key], enabled: false, cancellationToken)
            .ConfigureAwait(false);

        NotifyIfNothingLeft();
    }

    /// <summary>
    /// Says the last game window is gone when a deliberate close leaves none.
    ///
    /// A window we close on purpose carries StopRequested so that it is not
    /// taken for a game that died. That is right for recovery,
    /// but it also swallowed the one signal that tells the application nothing
    /// is left, and that signal is what makes it quit rather than survive with
    /// an empty screen.
    ///
    /// Measured: closing the tabbed frame while the configurator and the guide
    /// were both hidden, which is what happens as soon as accounts are open,
    /// left the process alive at 329 MB with nothing on screen and no way back
    /// except Ctrl+P. App.xaml.cs states the opposite rule in as many words.
    ///
    /// Not raised from CloseAllAsync: that one also serves as the first
    /// half of applying a launch profile, where a reopening follows at once
    /// and quitting in between would take the application down mid-gesture.
    /// </summary>
    private void NotifyIfNothingLeft()
    {
        if (_sessions.ActiveSessions.Count == 0)
        {
            LastWindowClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Stops a session without affecting the next launch.
    ///
    /// The session's StopRequested mark keeps closing the last one from
    /// also closing the application: it is us doing the closing, not the
    /// game dying.
    /// </summary>
    private async Task StopSessionAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        await _sessions.StopAsync(session.Id, cancellationToken).ConfigureAwait(false);
        _sessions.PruneFinished();

        // The tab goes with the session. Leaving it made the frame
        // believe the account was still there, and reopening that
        // account no longer housed it: it reappeared as a free
        // window on top of the frame.
        if (_tabs is not null)
        {
            await OnUiAsync(() => _tabs?.Detach(session.Target.Key)).ConfigureAwait(false);
        }

        // The accounts after it move up a line, and their key with it. The
        // session events leave a deliberate close aside, so that quitting
        // does not retitle every window on its way out: this one is the
        // close of one account, where the keys do shift.
        try
        {
            _ = await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogTitleStatusFailure(exception);
        }
    }

    /// <summary>
    /// Adds an account on a phone: a fresh Android profile, the game
    /// inside it, ready to open.
    ///
    /// The refusal message is completed here, where we know which
    /// brand the device is: when the overlay forbids creation,
    /// saying "go to your settings" without saying where gets
    /// nowhere.
    /// </summary>
    public async Task<AccountAddition> AddAccountAsync(
        string deviceId,
        string name,
        CancellationToken cancellationToken = default)
    {
        // The live state, not the registry's: the registry keeps
        // the last state written, which often reads "offline" even
        // though the phone answers. The button was thus refusing to
        // work on a device the list nonetheless showed as connected.
        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        if (!devices.TryGetValue(deviceId, out var device))
        {
            return new AccountAddition(false, Strings.Get("PhoneNotConnected"));
        }

        var result = await _instances
            .AddAccountAsync(device.Serial, name, cancellationToken)
            .ConfigureAwait(false);

        LogAccountAdded(device.DisplayName, name, result.Succeeded, result.Message);

        if (result.Succeeded)
        {
            return result;
        }

        var brand = PhoneBrands.FromManufacturer(device.Manufacturer);

        return result with
        {
            // Through a resource: the sentence was written in
            // French in the code, so an interface in English or
            // Spanish rendered a translated message followed by a
            // sentence that was not.
            Message = result.Message + Strings.Format("OnBrandUseClonePath", brand.Name, brand.ClonePath),
        };
    }

    /// <summary>
    /// The applications a phone can open, profile by profile, for the
    /// choice window. <c>null</c> when the phone is not connected.
    ///
    /// Two questions: the names, asked of scrcpy since only it reaches
    /// them, then the launchable packages of each profile. Without the
    /// names, the packages are still listed under their package name.
    /// </summary>
    public async Task<IReadOnlyList<ProfileApps>?> ListAppsAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        if (!devices.TryGetValue(deviceId, out var device))
        {
            return null;
        }

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        _instances.PackageName = settings.PackageName;

        var labels = await _sessions
            .ListAppsAsync(device.Id, device.Serial, cancellationToken)
            .ConfigureAwait(false);

        var profiles = await _instances
            .ListLaunchableAppsAsync(device, labels, cancellationToken)
            .ConfigureAwait(false);

        LogAppsListed(device.DisplayName, labels.Count, profiles.Sum(p => p.Apps.Count));

        return profiles;
    }

    /// <summary>The applications currently shown by request.</summary>
    public Task<IReadOnlyList<ShownApp>> GetShownAppsAsync(CancellationToken cancellationToken = default) =>
        _settings.GetShownAppsAsync(cancellationToken);

    /// <summary>
    /// Remembers which applications a phone shows beside the game.
    /// Their rows appear or leave on the next sweep.
    /// </summary>
    public async Task SetShownAppsAsync(
        string deviceId,
        IReadOnlyList<ShownApp> chosen,
        CancellationToken cancellationToken = default)
    {
        if (await _settings.SetShownAppsAsync(deviceId, chosen, cancellationToken).ConfigureAwait(false))
        {
            LogShownAppsChanged(deviceId, chosen.Count);
        }
    }

    /// <summary>
    /// Breaks a device's pairing: its windows close and it drops
    /// out of memory, pairing code included.
    /// </summary>
    public async Task ForgetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.ActiveSessions
            .Where(s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal))
            .ToList())
        {
            await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        // ADB keeps its connections open, so the next sweep would
        // see the phone again like any other. The device is reread
        // before it is discarded, since discarding erases the entry.
        var known = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);
        var device = known.FirstOrDefault(
            d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

        if (device is not null)
        {
            try
            {
                await _devices.DisconnectDeviceAsync(device, cancellationToken).ConfigureAwait(false);
            }
            catch (AdbException exception)
            {
                // A device that is already gone should not make a
                // break-up fail: the rest of the cleanup matters
                // more than this disconnection.
                LogDisconnectFailed(device.DisplayName, exception.UserMessage);
            }
        }

        await _registry.DiscardAsync(deviceId, cancellationToken).ConfigureAwait(false);

        // An irreversible action left no trace: when the user
        // reported having to click twice to forget a device, the log
        // had nothing to say about it and the diagnosis had to be
        // made by reading code.
        LogDeviceForgotten(device?.DisplayName ?? deviceId);
    }

    /// <summary>Between the stop of the games and their relaunch, in a reopen.</summary>
    private static readonly TimeSpan ReopenPause = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Closes then reopens the open windows, at their place and
    /// their size.
    ///
    /// Quality and zoom are scrcpy startup arguments, fixed for the
    /// whole duration of a session: changing them showed up nowhere
    /// until everything had been closed by hand. Geometry is
    /// captured before closing, so each window comes back where it
    /// was.
    /// </summary>
    public async Task<LaunchReport> ReopenAsync(CancellationToken cancellationToken = default)
    {
        // Two overlapping reopens used to steal each other's
        // sessions: the second one only saw one left, closed that
        // one, and reopened everything at the default corner for
        // lack of having captured anything.
        await _reopening.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var instances = _sessions.ActiveSessions
                .Select(s => s.Target)
                .ToList();

            if (instances.Count == 0)
            {
                return new LaunchReport(0, []);
            }

            // Geometry is kept at hand and passed as is to the
            // launch, rather than reread from the settings: an
            // empty reading would otherwise silently fall back to
            // the default placement, and every window would end up
            // stacked in the same spot.
            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            var remembered = await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

            var targets = _sessions.ActiveSessions.Select(s => s.Target).Where(t => !t.IsTransient).ToList();

            _soundAfterReopen.Reopened(targets.Select(Routable), DateTimeOffset.UtcNow);

            await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();

            // The games are stopped here, closing a window leaves them
            // running when the player asked so, and they are relaunched only
            // after a pause. Stopped and relaunched within the second, the
            // four games of a Galaxy S25+ came back without a sound until the
            // phone restarted (2026-10-18).
            // ponytail: fixed pause, not proven to be enough; the stuck sound
            // is told and its state logged, see SoundAfterReopen.
            foreach (var target in targets)
            {
                _ = await _appLauncher
                    .ForceStopAsync(Routable(target), target.UserId, target.PackageName, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (targets.Count > 0)
            {
                await Task.Delay(ReopenPause, cancellationToken).ConfigureAwait(false);
            }

            // Targets carry the identity of a session, not of an
            // instance: it is the up to date instance list that
            // knows what needs reopening.
            var known = await RefreshInstancesAsync(cancellationToken).ConfigureAwait(false);

            var reopen = known
                .Where(i => instances.Any(t => string.Equals(t.Key, i.Key, StringComparison.Ordinal)))
                .ToList();

            var missing = reopen
                .Where(i => !remembered.ContainsKey(i.Key))
                .Select(i => i.DisplayName)
                .ToList();

            if (missing.Count > 0)
            {
                // Without this reading, the window reopens at the
                // default corner: better say so in the log than
                // leave it to be figured out.
                LogMissingGeometry(string.Join(", ", missing));
            }

            return await LaunchAsync(reopen, remembered, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reopening.Release();
        }
    }

    /// <summary>Closes every window opened by the application.</summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        // Geometry is captured while the windows still exist.
        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
        _sessions.PruneFinished();

        try
        {
            await Task.WhenAll(_playtimeWrites).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Information only: a lost count must not hold the close.
            LogPlaytimeFailure(exception);
        }

        // See StopSessionAsync: the frame must not keep a tab on a
        // window that no longer exists. Here, opening a profile
        // follows right after, and waiting for the next maintenance
        // pass would be too late.
        if (_tabs is not null)
        {
            await OnUiAsync(() => _tabs?.DetachAll()).ConfigureAwait(false);
        }

        await _hotkeys.SetEnabledAsync(false).ConfigureAwait(false);
    }

    /// <summary>
    /// Captures where each window was left and saves it. Called
    /// before every close, and on exit: this is what lets a window
    /// moved with the mouse come back to the same spot.
    /// </summary>
    public async Task CaptureGeometriesAsync(CancellationToken cancellationToken = default)
    {
        await CaptureTabsPlacementAsync(cancellationToken).ConfigureAwait(false);

        // Housed windows are set aside: their rectangle is the one
        // they occupy inside the frame, and remembering it as a free
        // spot made them reappear in the middle of the screen the
        // day they were taken out of the tabs.
        var captured = _windows.CaptureGeometries(
            [.. _sessions.ActiveSessions.Where(s => !_tabbed.Contains(s.Target.Key) && !s.Target.IsTransient)]);

        if (captured.Count == 0)
        {
            return;
        }

        await _settings.SaveWindowRectsAsync(
            captured.ToDictionary(c => c.Key, c => c.Rect, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Remembers where the tabbed frame is.
    ///
    /// Apart from the game windows: a housed window no longer has a
    /// place of its own, and it is the frame's that counts. The
    /// reading happens on the UI thread, a WPF window's handle not
    /// being readable anywhere else.
    /// </summary>
    private async Task CaptureTabsPlacementAsync(CancellationToken cancellationToken)
    {
        if (_tabs is not { } frame || frame.IsFullscreen)
        {
            // In fullscreen, the rectangle is that of the whole
            // screen: saving it would make the frame reopen
            // covering everything, with nothing left to say where it
            // came from.
            return;
        }

        WindowPlacement? place = null;

        await OnUiAsync(() => place = _windows.Controller.GetPlacement(frame.Handle))
            .ConfigureAwait(false);

        await _settings
            .SetWindowPlacementAsync(WindowPlacements.Tabs, place, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Periodic maintenance: fixes the windows' shape and remembers
    /// which one is in the foreground.
    /// </summary>
    public void Watch()
    {
        // Tracking looks at every window: knowing which one is
        // active also applies to those housed in the frame.
        _windows.TrackActiveWindow(_sessions.ActiveSessions);

        // The aspect ratio enforcement, though, only concerns free
        // windows. On a housed window, it undid the frame's layout
        // every half second, and the game came back to sit crooked
        // without anyone understanding why.
        _windows.EnforceAspect(ManagedSessions);

        // A safety net for sessions that die without going through
        // us: a game window closed by hand, a phone unplugged.
        // Deliberate closes already detach the tab themselves,
        // without waiting for this pass.
        _tabs?.KeepOnly(
            [.. _sessions.ActiveSessions.Select(s => s.Target.Key)]);

        ReportFirstImages();

        SampleInBackground();
    }

    /// <summary>Sessions whose first image has already been recorded.</summary>
    private readonly HashSet<string> _firstImages = new(StringComparer.Ordinal);

    /// <summary>
    /// Records, once per session, how long its window stayed black.
    ///
    /// **It is the only wait the user actually watches**, and nothing
    /// measured it: the launch reported the display being ready and
    /// the game's start command returning, both of which happen
    /// seconds before anything is drawn. A window black for half a
    /// minute therefore left no trace, and a game slow to boot could
    /// not be told apart from a stream that never came.
    ///
    /// Read from the sweep rather than pushed from the reader: the
    /// reader runs on a pool thread, and the log is not what it is
    /// there for.
    /// </summary>
    private void ReportFirstImages()
    {
        foreach (var session in _sessions.ActiveSessions)
        {
            if (session.FirstImageMs > 0 && _firstImages.Add(session.Id))
            {
                LogFirstImage(session.DisplayName, session.FirstImageMs);
            }
        }

        _firstImages.IntersectWith(_sessions.ActiveSessions.Select(s => s.Id));
    }


    /// <summary>
    /// Tiles the windows side by side, the active window on the
    /// right.
    /// </summary>
    public async Task<int> TileAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // The frame counts as one window. In the foreground, it
            // takes the right side and every free window moves to
            // the left: without this, one of them would land on the
            // right on top of it.
            var frame = ArrangeableCount > 1 ? MovableFrame : null;
            var frameFirst = frame is not null
                && _windows.Controller.GetForegroundWindow() == frame.Handle;

            var placed = await _windows
                .TileAsync(ManagedSessions, frameFirst, cancellationToken)
                .ConfigureAwait(false);

            if (frame is not null)
            {
                await TileFrameAsync(frame, frameFirst).ConfigureAwait(false);
                placed++;
            }

            if (placed > 0)
            {
                await LeaveFullscreenStateAsync(cancellationToken).ConfigureAwait(false);
            }

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            return placed;
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    /// <summary>Gives the frame half of the screen.</summary>
    private Task TileFrameAsync(Windows.TabbedGameWindow frame, bool onRight) =>
        OnUiAsync(() =>
        {
            frame.SetFullscreen(false, default);

            if (frame.Chassis is { } chassis && _windows.WorkAreaFor(frame.Handle) is { } work)
            {
                frame.ApplyRect(
                    TileLayout.Half(work, onRight, frame.SelectedAspect ?? 0, chassis));
            }
        });

    /// <summary>
    /// Lays the windows out in a grid, in the order of the list. The
    /// frame counts as one window and takes the cell of its first
    /// housed account.
    /// </summary>
    public async Task<int> GridAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var sessions = ManagedSessions;
            var frame = ArrangeableCount > 1 ? MovableFrame : null;
            int? frameCell = null;

            if (frame is not null)
            {
                var before = 0;

                foreach (var session in _sessions.ActiveSessions)
                {
                    if (_tabbed.Contains(session.Target.Key))
                    {
                        frameCell = before;
                        break;
                    }

                    if (sessions.Contains(session))
                    {
                        before++;
                    }
                }
            }

            var placed = await _windows
                .GridAsync(sessions, frameCell, cancellationToken)
                .ConfigureAwait(false);

            if (frame is not null && frameCell is { } cell)
            {
                // ponytail: the frame takes its quarter on its own screen, as
                // side by side does; one shared screen if several are used.
                await OnUiAsync(() =>
                {
                    frame.SetFullscreen(false, default);

                    if (frame.Chassis is { } chassis && _windows.WorkAreaFor(frame.Handle) is { } work)
                    {
                        frame.ApplyRect(
                            TileLayout.Quarter(work, cell, frame.SelectedAspect ?? 0, chassis));
                    }
                }).ConfigureAwait(false);

                placed++;
            }

            if (placed > 0)
            {
                await LeaveFullscreenStateAsync(cancellationToken).ConfigureAwait(false);
            }

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            return placed;
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    /// <summary>
    /// Stacks the windows onto the active one, or onto the first
    /// one. This is what the rearrange shortcut does, and the button
    /// on the bottom bar: a window is placed wherever wanted, the
    /// others join it.
    /// </summary>
    public async Task<int> StackOnActiveAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var frame = MovableFrame;
            var sessions = ManagedSessions;

            // The frame in the foreground serves as the reference:
            // what is being looked at is not moved in order to align
            // everything else elsewhere.
            var vu = frame is not null
                && _windows.Controller.GetForegroundWindow() == frame.Handle
                    ? _windows.Controller.GetWindowRect(frame.Handle)
                    : null;

            if (vu is { IsEmpty: false } depuisLeCadre)
            {
                var pris = await _windows
                    .StackOnAsync(sessions, depuisLeCadre, null, cancellationToken)
                    .ConfigureAwait(false);

                await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

                return pris;
            }

            if (await _windows.StackTargetAsync(sessions, cancellationToken).ConfigureAwait(false)
                is not { } cible)
            {
                return 0;
            }

            var moved = await _windows
                .StackOnAsync(sessions, cible.Rect, cible.Reference, cancellationToken)
                .ConfigureAwait(false);

            if (frame is not null)
            {
                await StackFrameAsync(cible.Rect).ConfigureAwait(false);
                moved++;
            }

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            return moved;
        }
        finally
        {
            _ = _arranging.Release();
        }
    }


    /// <summary>
    /// Puts every window back in place, at its current size.
    /// </summary>
    public async Task<int> ArrangeAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ArrangeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    private async Task<int> ArrangeCoreAsync(CancellationToken cancellationToken)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        // Before placing: the placement reads the size, and in full
        // screen it stacked every window back over the whole screen.
        if (ManagedSessions.Count > 0 || MovableFrame is not null)
        {
            await LeaveFullscreenStateAsync(cancellationToken).ConfigureAwait(false);
        }

        var moved = await _windows.ArrangeAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        if (MovableFrame is { } frame)
        {
            await OnUiAsync(() =>
            {
                frame.SetFullscreen(false, default);

                if (frame.Chassis is { } chassis
                    && _windows.AnchoredRect(frame.Handle, frame.SelectedAspect ?? 0, chassis)
                        is { } rect)
                {
                    frame.ApplyRect(rect);
                }
            }).ConfigureAwait(false);

            moved++;
        }

        // The quick rearrange becomes the new reference geometry,
        // otherwise memory would drift from what is on screen.
        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return moved;
    }

    /// <summary>
    /// Applies the size set on the slider.
    ///
    /// Two uses, deliberately kept separate. While the slider moves,
    /// only the windows are moved: rereading the settings and
    /// writing the file at every notch made the gesture jerky.
    /// Saving happens only once, when the slider is released.
    /// </summary>
    public async Task<int> ApplyPercentAsync(
        int percent,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        var moved = await _windows
            .ApplyPercentAsync(ManagedSessions, percent, cancellationToken)
            .ConfigureAwait(false);

        if (persist)
        {
            // The frame only follows on release: at every notch, it
            // would take a round trip through the UI thread to
            // resize a WPF window, and the gesture would become
            // jerky.
            await ResizeFrameAsync(fullscreen: false).ConfigureAwait(false);

            await _settings.SaveCustomSizePercentAsync(percent, cancellationToken).ConfigureAwait(false);

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);
        }

        await ArmEscapeAsync().ConfigureAwait(false);

        return moved;
    }

    /// <summary>Applies a size to every window and remembers it.</summary>
    public async Task<int> ApplySizeAsync(int sizeIndex, CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ApplySizeCoreAsync(sizeIndex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    /// <summary>
    /// Switches to full screen, or, if already there, goes back to the
    /// size from before. The shortcut used to only enter it: a player
    /// pressing it again stayed stuck, with the slider as the only way
    /// out.
    /// </summary>
    public async Task ToggleFullscreenAsync(
        bool leaveOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_windows.IsFullscreen)
            {
                _ = await ApplyPercentAsync(_windows.PercentBeforeFullscreen, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!leaveOnly)
            {
                _ = await ApplySizeCoreAsync(_windows.Presets.FullscreenIndex, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    private async Task<int> ApplySizeCoreAsync(int sizeIndex, CancellationToken cancellationToken)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        await _settings.UpdateAsync(
            s =>
            {
                s.SizeIndex = sizeIndex;
                s.CustomSizePercent = 0;
            },
            cancellationToken).ConfigureAwait(false);


        var moved = await _windows
            .ApplySizeAsync(ManagedSessions, sizeIndex, cancellationToken)
            .ConfigureAwait(false);

        await ResizeFrameAsync(sizeIndex >= _windows.Presets.FullscreenIndex).ConfigureAwait(false);

        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        await ArmEscapeAsync().ConfigureAwait(false);

        return moved;
    }

    /// <summary>
    /// Open session matching an instance, if there is one.
    /// </summary>
    /// <summary>Every open game window, docked in the frame or free.</summary>
    private IEnumerable<nint> GameWindows() =>
        _sessions.ActiveSessions.Select(s => s.WindowHandle).Where(h => h != 0);

    /// <summary>
    /// Game windows whose mouse scrcpy captures, read from their own command
    /// line: the setting may have changed since they opened.
    /// </summary>
    private HashSet<nint> MouseCapturingWindows() =>
    [
        .. _sessions.ActiveSessions
            .Where(s => s.WindowHandle != 0 && s.CommandLine.Contains("--mouse=uhid", StringComparison.Ordinal))
            .Select(s => s.WindowHandle),
    ];

    public ScrcpySession? FindSession(DofusInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return _sessions.ActiveSessions.FirstOrDefault(
            s => string.Equals(s.Target.Key, instance.Key, StringComparison.Ordinal));
    }

    /// <summary>True if the instance has an open window.</summary>
    public bool IsOpen(DofusInstance instance) => FindSession(instance) is not null;

    /// <summary>
    /// Where this account's window is in its life, or <c>null</c> when
    /// it has none.
    ///
    /// <see cref="IsOpen" /> answers the same question with a boolean,
    /// and that is all it ever could: a window being built and a window
    /// showing the game are both "open" to it. The row needs to tell
    /// them apart.
    /// </summary>
    public ScrcpySessionState? SessionStateOf(DofusInstance instance) => FindSession(instance)?.State;

    /// <summary>
    /// When this account's window was opened, or <c>null</c> when it
    /// has none. The elapsed time is worked out by the caller: this
    /// type does not own a clock.
    /// </summary>
    public DateTimeOffset? SessionStartedUtc(DofusInstance instance) => FindSession(instance)?.StartedUtc;

    /// <summary>
    /// The distance this account's window was opened with, or
    /// <c>null</c> if it has none.
    /// </summary>
    public GameZoom? ZoomInUse(string key) =>
        _zoomsInUse.TryGetValue(key, out var zoom) ? zoom : null;

    public ScreenRect? WorkArea() => _windows.WorkArea();


    /// <summary>Reloads the shortcuts after a change.</summary>
    public async Task<IReadOnlyList<HotkeyAction>> ReloadHotkeysAsync(
        CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);

        _hotkeySet = hotkeys;

        // The reminder written into the windows' title follows the
        // key combination. This is where it must happen: the editor
        // reloads through this path, whichever window opened it.
        await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);

        Task<IReadOnlyList<HotkeyAction>> applying;

        lock (_escapeLock)
        {
            _escapeArmed = WantsEscape;
            applying = _hotkeys.ApplyAsync(hotkeys.WhileFullscreen(_escapeArmed));
        }

        return await applying.ConfigureAwait(false);
    }

    /// <summary>
    /// Escape leaves fullscreen only from a game window or the frame: the
    /// panel, the guides and the Almanax stay above a fullscreen game and
    /// use Escape themselves.
    /// </summary>
    private bool WantsEscape => _windows.IsFullscreen && _gameInFront;

    /// <summary>A game window or the frame is in the foreground.</summary>
    private volatile bool _gameInFront;

    /// <summary>Whether Escape is armed, under <see cref="_escapeLock"/>.</summary>
    private bool _escapeArmed;

    /// <summary>
    /// The registrar keeps the last set handed to it: deciding and handing
    /// over in one step keeps a slider notch and a Ctrl+5 from leaving
    /// Escape armed with the flag saying it is not.
    /// </summary>
    private readonly Lock _escapeLock = new();

    /// <summary>
    /// Arms Escape or gives it back to the game, only when that changes:
    /// the slider and the foreground watcher call this all the time.
    /// </summary>
    /// <summary>
    /// Drops full screen for a command that places the windows
    /// itself, and remembers the size from before as the current one.
    /// </summary>
    private async Task LeaveFullscreenStateAsync(CancellationToken cancellationToken)
    {
        if (!_windows.LeaveFullscreenState())
        {
            return;
        }

        await _settings.SaveCustomSizePercentAsync(_windows.SizePercent, cancellationToken).ConfigureAwait(false);
        await ArmEscapeAsync().ConfigureAwait(false);
    }

    private Task ArmEscapeAsync()
    {
        lock (_escapeLock)
        {
            if (WantsEscape == _escapeArmed || _hotkeySet is not { } hotkeys)
            {
                return Task.CompletedTask;
            }

            _escapeArmed = WantsEscape;

            return _hotkeys.ApplyAsync(hotkeys.WhileFullscreen(_escapeArmed));
        }
    }

    /// <summary>
    /// Updates the shortcut reminder in the title of open windows.
    /// Called after a change made in the editor.
    /// </summary>
    public async Task<int> RefreshWindowTitlesAsync(CancellationToken cancellationToken = default)
    {
        var hint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        // A tool's window is not a game and has no play time to tell.
        string? Status(ScrcpySession session) =>
            settings.TitleShowsStatus && !session.Target.IsTransient ? StatusOf(session) : null;

        await OnUiAsync(() =>
        {
            if (_tabs is { } frame)
            {
                frame.ShortcutReminder = hint;
                frame.ShowAccountKeys(AccountKeyOf);

                foreach (var session in _sessions.ActiveSessions.Where(s => s.IsAlive))
                {
                    frame.Restate(session.Target.Key, Status(session));
                }
            }
        }).ConfigureAwait(false);

        return _windows.Retitle(
            _sessions.ActiveSessions,
            session => ScrcpyCommandBuilder.BuildWindowTitle(
                Status(session) is { } status
                    ? $"{CurrentName(session, settings)} · {status}"
                    : CurrentName(session, settings),
                session.Target.IsTransient ? null : hint,
                AccountKeyOf(session.Target.Key)));
    }


    /// <summary>
    /// Tints the frame of every open game window with its account's
    /// colour.
    ///
    /// The colour comes from the launched copy, which
    /// <see cref="ApplyColours" /> keeps in step, and not from the
    /// target, which carries what scrcpy needed and nothing else.
    /// </summary>
    public int RefreshWindowColours() =>
        _windows.Recolour(
            _sessions.ActiveSessions,
            session => AccountTints.FrameColourRefFor(
                _launched.GetValueOrDefault(session.Target.Key)?.Colour));

    /// <summary>
    /// The name this account carries now, not the one it carried at
    /// launch.
    ///
    /// <c>LaunchTarget.DisplayName</c> is frozen at opening, by
    /// design: it is a target, it describes what was launched. Using
    /// it to rewrite a title would put the old name back, which is
    /// exactly what the refresh following a shortcut change already
    /// did.
    ///
    /// The chosen name is read from the settings rather than from the
    /// launched copy, so that no surface that shows a name depends on
    /// <see cref="ApplyRenames" /> having run first. The launched copy
    /// still gives the Android profile name, which is the fallback when
    /// no name was chosen, and the target still answers for a session
    /// that never went through the launcher.
    /// </summary>
    private string CurrentName(ScrcpySession session, AppSettingsDocument document) =>
        _launched.TryGetValue(session.Target.Key, out var instance)
            ? InstanceRenames.NameNow(instance.Key, instance.UserName, ChosenNameIn(document))
            : session.Target.DisplayName;

    /// <summary>Reads a chosen name out of a settings document.</summary>
    private static Func<string, string?> ChosenNameIn(AppSettingsDocument document) =>
        key => document.Instances
            .Find(i => string.Equals(i.Key, key, StringComparison.Ordinal))
            ?.CustomName;

    /// <summary>
    /// Carries a changed account colour through to the tab that shows
    /// it.
    ///
    /// **Guarded like the renames, and for the same reason**, written
    /// out in the comment above: this event fires on every write of the
    /// settings, a window being dragged included. Without the map below,
    /// every drag would repaint every tab.
    ///
    /// Only the tabs are updated here, because a tab is the only surface
    /// that keeps a copy of the colour of its own. A free window's frame
    /// would be repainted here too, if it could be: whether a colour can
    /// be set on a window scrcpy owns is what `build/sonde-bordure`
    /// exists to answer, and it has not been answered yet.
    /// </summary>
    private void ApplyColours(AppSettingsDocument document)
    {
        Dictionary<string, AccountColour?> wanted = [];

        foreach (var instance in document.Instances)
        {
            wanted[instance.Key] = instance.Colour;
        }

        if (wanted.Count == _colours.Count
            && wanted.All(pair => _colours.TryGetValue(pair.Key, out var had) && had == pair.Value))
        {
            return;
        }

        _colours = wanted;

        // The launched copy carries the colour that the tabbed frame
        // reads when a window is docked. Left untouched, a colour
        // changed while the window was free came back to the launch-time
        // one the moment it entered the frame, and stayed wrong for the
        // rest of the session. The renames above keep their copy in step
        // for exactly this reason.
        foreach (var (key, colour) in wanted)
        {
            if (_launched.TryGetValue(key, out var instance) && instance.Colour != colour)
            {
                _launched[key] = instance with { Colour = colour };
            }
        }

        var repaint = wanted
            .Where(pair => _launched.ContainsKey(pair.Key))
            .ToList();

        if (repaint.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await OnUiAsync(() =>
                {
                    foreach (var (key, colour) in repaint)
                    {
                        _tabs?.Recolour(key, AccountTints.KeyFor(colour));
                    }

                    // And the free windows, whose frame carries the same
                    // mark. A docked one has had its caption taken away
                    // by the frame, so there is nothing to paint and
                    // nothing that fails.
                    _ = RefreshWindowColours();
                }).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogColourFailure(exception);
            }
        });
    }

    /// <summary>
    /// The colours last carried through, so that a settings write that
    /// changed none of them costs nothing.
    /// </summary>
    private Dictionary<string, AccountColour?> _colours = [];

    /// <summary>
    /// Carries onto the open windows the names the settings have
    /// just written.
    ///
    /// **Nothing happens in the ordinary case**, and it must be
    /// said: this event fires on every write of the settings,
    /// including the geometry of a window being dragged. Without the
    /// comparison done by
    /// <see cref="InstanceRenames"/>, a simple window drag would
    /// rewrite every title of every window.
    ///
    /// The action is not awaited: writing the settings must not
    /// hold up the UI, and a fault here must not bubble up into the
    /// save path.
    /// </summary>
    private void ApplyRenames(AppSettingsDocument document)
    {
        var open = _launched.Values
            .Select(i => new OpenInstance(i.Key, i.UserName, i.DisplayName))
            .ToList();

        var pending = InstanceRenames.Pending(open, ChosenNameIn(document));

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var key in pending.Keys)
        {
            if (_launched.TryGetValue(key, out var instance))
            {
                // The raw name from the settings, not the one shown:
                // a cleared name must stay cleared, so that falling
                // back to the Android profile still holds at the
                // next rename.
                _launched[key] = instance with
                {
                    CustomName = document.Instances
                        .Find(i => string.Equals(i.Key, key, StringComparison.Ordinal))
                        ?.CustomName,
                };
            }
        }

        // One guarded path rather than two, the tab first and the free
        // windows after. The tab hop used to be started and dropped, so a
        // dispatcher fault while the frame was closing went nowhere.
        _ = Task.Run(async () =>
        {
            try
            {
                await OnUiAsync(() =>
                {
                    foreach (var (key, name) in pending)
                    {
                        _tabs?.Rename(key, name);
                    }
                }).ConfigureAwait(false);

                _ = await RefreshWindowTitlesAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogRenameFailure(exception);
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _hotkeys.ForegroundWindowChanged -= OnForegroundChanged;
        _hotkeys.MouseCaptured -= OnMouseCaptured;

        await _sessions.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Rectangle every window will share. Computed once: this is
    /// what guarantees their exact overlap.
    /// </summary>
    private ScrcpyWindowPlacement? ComputePlacement(ScrcpyOptions options, StoredWindowRect? remembered)
    {
        // The window keeps the display's aspect ratio: the image is
        // scaled to fit it, and departing from it would leave a
        // band.
        var aspect = options is { UseVirtualDisplay: true, VirtualDisplayHeight: > 0 }
            ? (double)options.VirtualDisplayWidth / options.VirtualDisplayHeight
            : 0;

        var monitors = _windows.GetMonitors();

        var rect = remembered is not null
            ? WindowLayoutCalculator.RestoreRemembered(
                  remembered.Bounds, remembered.MonitorDeviceName, remembered.Monitor, monitors)
              ?? _windows.PreviewGameArea(aspect)
            : _windows.PreviewGameArea(aspect);

        // Logged: the layout depends on the screen and its scaling,
        // and an unexpected figure shows up right away here.
        LogPlacement(
            string.Join(", ", monitors.Select(m => $"{m.DeviceName} {m.Bounds} utile {m.WorkArea}")),
            rect?.ToString() ?? "aucun");

        if (rect is not { } value)
        {
            return null;
        }

        // The rectangle passed on is that of the client area, chrome
        // subtracted and corner offset: scrcpy sizes **and
        // positions** its window from the inside. Giving it the
        // outer rectangle made it born too large by a title bar,
        // then, once the size was corrected, born too far left by a
        // border and too high by a title bar. The placement that
        // followed then repositioned it visibly, two hundred
        // milliseconds after it opened.
        var client = _windows.WindowChrome().ClientOf(value);

        return new ScrcpyWindowPlacement(client.X, client.Y, client.Width, client.Height);
    }

    /// <summary>
    /// Adapts the display's resolution to this instance's window.
    ///
    /// The image is scaled to the window: a display always taken at
    /// the screen's resolution would make the game's interface tiny
    /// in a small window. The resolution therefore follows the
    /// window, by tiers, since it is fixed for the whole session.
    ///
    /// The screen is the one the window will actually open on, not a
    /// reference screen: a window left on a second screen of a
    /// different shape would otherwise be born malformed.
    /// </summary>
    /// <summary>
    /// The resolution and bitrate of a window, based on its
    /// account's tier.
    ///
    /// The tier comes from the account, not from the shared field: a
    /// main account deserves better than four mules, and what is
    /// spared on the mules is that much less processor, bandwidth,
    /// heat and battery. Polling rates, though, stay shared.
    /// </summary>
    private ScrcpyOptions WithDisplayFor(
        ScrcpyOptions options,
        ScrcpyWindowPlacement? placement,
        StoredWindowRect? remembered,
        QualityProfile quality,
        GameZoom zoom,
        int layoutHeight,
        double phoneAspect)
    {
        if (placement is not { Height: > 0 } window
            || _windows.MonitorBoundsFor(remembered) is not { } screen)
        {
            return options;
        }

        var (width, height) = DisplayLadder.For(
            window.Height, screen.Width, screen.Height, quality.MaximumDisplayHeight, _sharpImage, phoneAspect,
            DisplayLadder.MinimumHeight);

        // The sharper image and the small window's floor are paid for in
        // pixels, not in bits. Measured when it was made: at the bitrate of
        // the window's own size, the red text is already as sharp as the
        // white. Following the doubled size instead multiplied the bitrate
        // by up to two and a half, 9.5 to 24.3 Mb/s on the 13T Pro in the
        // high tier, per account, and the players said it lagged.
        var (bitrateWidth, bitrateHeight) =
            DisplayLadder.For(window.Height, screen.Width, screen.Height, quality.MaximumDisplayHeight, phoneAspect: phoneAspect);

        // Density is derived from the chosen resolution: leaving it
        // fixed made the game's zoom vary with the window's size,
        // since the resolution does follow it.
        //
        // Bitrate is derived from it too, and for the same reason: a
        // fixed bitrate served a small window generously and starved
        // a large one.
        return options with
        {
            VirtualDisplayWidth = width,
            VirtualDisplayHeight = height,
            VirtualDisplayDpi = ZoomProfile.DpiFor(height, zoom, layoutHeight),
            VideoBitrateKbps = quality.BitrateFor(bitrateWidth, bitrateHeight),

            // The frame rate too, and it is the one that gets
            // forgotten: resolution and bitrate did go down, but an
            // account on a low tier still ran at sixty frames, which
            // is the bulk of the cost. Seen in the log, not in the
            // code.
            MaxFps = quality.MaxFps,
        };
    }

    /// <summary>
    /// Makes the window order follow the list's order, Alt+Tab
    /// included.
    ///
    /// Every window goes through it, locked ones included: the lock
    /// applies to position, not to rank.
    /// </summary>
    public Task<int> ApplyWindowOrderAsync(CancellationToken cancellationToken = default) =>
        _windows.ApplyOrderAsync(_sessions.ActiveSessions, cancellationToken);

    /// <summary>
    /// Rereads the intended order and gives it to the session
    /// manager. Everything that iterates over sessions inherits it:
    /// opening, placement and keyboard cycling.
    /// </summary>
    public async Task RefreshRanksAsync(CancellationToken cancellationToken = default)
    {
        _unmanaged = await _settings.GetUnmanagedKeysAsync(cancellationToken).ConfigureAwait(false);

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        _tabbed = settings.Instances
            .Where(i => i.IsTabbed)
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);

        var ranks = await _settings.GetInstanceRanksAsync(cancellationToken).ConfigureAwait(false);

        _sessions.OrderKey = session =>
            ranks.TryGetValue(session.Target.Key, out var rank) ? rank : int.MaxValue;

        // Every reorder ends here, and moves the keys with the lines.
        _ = await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);

        // After the reread, not before: whatever is listening will
        // then read the right set. This is where "set aside" and
        // "housed in a tab" take effect for everything else.
        ArrangeableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reminder of the account-switching shortcut, as configured at
    /// the moment of launch. Empty if the user removed it.
    /// </summary>
    private async Task<string?> BuildTitleHintAsync(CancellationToken cancellationToken)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);

        // The configurator first: it is the only reminder needed
        // when one no longer knows how to get back to the
        // application. It has no icon in the taskbar once hidden,
        // and without this reminder the combination is learned
        // nowhere.
        List<string> parts = [];

        if (hotkeys.For(HotkeyAction.ToggleConfigurator) is { IsAssigned: true } toggle)
        {
            parts.Add(Strings.Format("TitleHintSettings", toggle.DisplayText));
        }

        if (hotkeys.For(HotkeyAction.NextInstance) is { IsAssigned: true } next)
        {
            parts.Add(Strings.Format("TitleHintNextWindow", next.DisplayText));
        }

        // Kept for the tabbed frame, which is created later and on the
        // interface thread.
        _titleHint = parts.Count > 0 ? string.Join("  ·  ", parts) : null;

        return _titleHint;
    }

    /// <summary>The last shortcut reminder built, for the tabbed frame's title.</summary>
    private volatile string? _titleHint;

    private static LaunchTarget ToTarget(DofusInstance instance, string serial) => new()
    {
        DeviceId = instance.DeviceId,
        Serial = serial,
        UserId = instance.UserId,
        PackageName = instance.PackageName,
        LaunchComponent = instance.LaunchComponent,
        DisplayName = instance.DisplayName,
        StartUri = instance.StartUri,
        IsTransient = instance.IsTransient,
    };

    /// <summary>
    /// Rereads what the link is worth, to derive the display buffer
    /// from it.
    ///
    /// Once per launch, and never during a geometry gesture: the
    /// figure does not move between two resizes, whereas the call
    /// would cost half a second every time.
    ///
    /// Every session in a launch goes through the same phone: the
    /// first one found is enough to know the link, and asking the
    /// others would give the same answer.
    /// </summary>
    private async Task RefreshLinkAsync(
        IReadOnlyList<DofusInstance> instances,
        Dictionary<string, AndroidDevice> devices,
        CancellationToken cancellationToken)
    {
        var serials = instances
            .Select(i => devices.TryGetValue(i.DeviceId, out var device) ? device.Serial : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var serial in serials)
        {
            var link = await _devices.GetWifiLinkAsync(serial!, cancellationToken).ConfigureAwait(false);

            _videoBuffers[serial!] = VideoBuffer.MillisecondsFor(link);

            if (link is null)
            {
                LogUnknownLink();
                continue;
            }

            LogLink(
                link.Standard,
                link.FrequencyMhz,
                link.LinkSpeedMbps,
                link.Rssi,
                Math.Round(link.RetryShare * 100, 1),
                _videoBuffers[serial!]);
        }
    }

    /// <summary>
    /// Connected devices, indexed by identifier. The whole record is
    /// kept, not just the serial number: the Android version is in
    /// there, and the launch needs it.
    /// </summary>
    private async Task<Dictionary<string, AndroidDevice>> ResolveDevicesAsync(
        CancellationToken cancellationToken)
    {
        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        return discovery.Devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, d => d, StringComparer.Ordinal);
    }

    /// <summary>
    /// Houses a session in the tabbed frame, creating it if needed.
    ///
    /// The window must exist: it is looked for as for a placement,
    /// with the same wait. A session whose window never appeared
    /// stays free rather than being lost.
    /// </summary>
    private async Task AttachToTabsAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        // Read before the window is resolved, so that the name below is the
        // one the settings carry now. A tab used to be seeded from
        // <c>session.DisplayName</c>, frozen when scrcpy started: renaming an
        // account whose window was open but not docked, then docking it, gave
        // the tab the name from before the rename, and nothing ever wrote it
        // again for the rest of the session.
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        var name = InstanceRenames.NameNow(
            session.Target.Key,
            _launched.TryGetValue(session.Target.Key, out var launched)
                ? launched.UserName
                : session.DisplayName,
            ChosenNameIn(settings));

        var handle = await _windows.ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

        if (handle == 0)
        {
            LogTabWindowMissing(name);
            return;
        }

        // The intended order is reapplied after every arrival, and
        // not left to the order of arrivals. Displays do not get
        // ready at the same speed: from one launch to the next, tabs
        // came out in a different order, and one arranged with the
        // mouse did not hold from one session to the next.
        var order = settings.Instances
            .OrderBy(i => i.Order)
            .Select(i => i.Key)
            .ToList();

        await OnUiAsync(() =>
        {
            var frame = EnsureTabs(settings);

            frame.Attach(
                session.Target.Key,
                name,
                IconFor(session),
                handle,
                session.SourceAspectRatio,
                // From the launched copy and not from the target: a
                // launch target carries what scrcpy needs, and a colour
                // is not one of those things.
                AccountTints.KeyFor(_launched.GetValueOrDefault(session.Target.Key)?.Colour));

            frame.Reorder(order);
            frame.ShowAccountKeys(AccountKeyOf);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a UI action on the thread that has the right to do so.
    ///
    /// Returns control right away if there is no WPF application,
    /// which is the case in tests.
    /// </summary>
    private static Task OnUiAsync(Action geste)
    {
        var thread = System.Windows.Application.Current?.Dispatcher;

        if (thread is null || thread.CheckAccess())
        {
            geste();
            return Task.CompletedTask;
        }

        return thread.InvokeAsync(geste).Task;
    }

    /// <summary>
    /// Closes the accounts the tabbed frame was housing.
    ///
    /// The account behaves as if its window had been closed one by
    /// one: its place is remembered, and it reopens at the next
    /// startup, as D22 wants for any window closed by hand. Only the
    /// "Close" button takes it out of that set. It stays housed in
    /// tabs, so it returns there.
    ///
    /// A session that survives the stop gets its window back
    /// visible: it left the frame hidden, and leaving it that way
    /// would make it unfindable.
    /// </summary>
    private async Task CloseTabbedAsync(IReadOnlyList<string> keys)
    {
        // First, while the frame still exists: once closed it is
        // forgotten, and the save made on exit no longer sees it.
        // The frame then came back where it stood two runs ago.
        await CaptureTabsPlacementAsync(CancellationToken.None).ConfigureAwait(false);

        foreach (var key in keys)
        {
            if (_sessions.ActiveSessions.FirstOrDefault(
                    s => string.Equals(s.Target.Key, key, StringComparison.Ordinal)) is not { } session)
            {
                continue;
            }

            await StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);

            if (session.IsAlive && session.WindowHandle != 0)
            {
                await OnUiAsync(() => _windows.Controller.SetVisible(session.WindowHandle, true))
                    .ConfigureAwait(false);
            }
        }

        // A session that outlived its stop is a free window again, and it
        // was docked when the colours were set: see SetTabbedAsync.
        await OnUiAsync(() => RefreshWindowColours()).ConfigureAwait(false);

        NotifyIfNothingLeft();
    }

    /// <summary>
    /// The frame, created on first need and kept open afterwards.
    /// </summary>
    private Windows.TabbedGameWindow EnsureTabs(AppSettingsDocument document)
    {
        if (_tabs is { } existant)
        {
            return existant;
        }

        var frame = new Windows.TabbedGameWindow(_windows.Controller) { ShortcutReminder = _titleHint };

        // The frame counts as one window to arrange: the panel's fullscreen
        // and arranging keys follow it in and out. Accounts reopened in tabs
        // at startup reached Running before the frame existed, and nothing
        // told the panel afterwards: the fullscreen key stayed hidden
        // (2026-10-17).
        frame.Closed += (_, _) =>
        {
            _tabs = null;
            ArrangeableChanged?.Invoke(this, EventArgs.Empty);
        };

        // Closing the frame closes the accounts it was housing:
        // that is what the action announces, and seeing them
        // scatter into free windows was the opposite of what was
        // asked.
        frame.CloseRequested += async (_, loges) =>
        {
            // Bounded, these two actions. A fault would escape an
            // "async void" lambda and reach the dispatcher's
            // safety net, which would open an error window for a
            // click on a close cross or a tab drag. The log catches
            // it instead, and only the action fails.
            try
            {
                await CloseTabbedAsync(loges).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogTabsFailure(exception);
            }
        };

        // Reordering the tabs reorders the accounts: a single order
        // everywhere.
        frame.Reordered += async (_, mouvement) =>
        {
            try
            {
                if (await _settings
                        .MoveInstanceAsync(mouvement.Moved, mouvement.Onto, mouvement.Before)
                        .ConfigureAwait(true))
                {
                    await RefreshRanksAsync().ConfigureAwait(true);

                    var order = await _settings.GetInstanceRanksAsync().ConfigureAwait(true);

                    frame.Reorder([.. order.OrderBy(p => p.Value).Select(p => p.Key)]);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogTabsFailure(exception);
            }
        };

        _tabs = frame;
        frame.Show();
        ArrangeableChanged?.Invoke(this, EventArgs.Empty);

        // After it is shown: there is no handle before that, so
        // nothing to place. The frame thus reappears where it was
        // left, and a tabbed profile reopens it where it was when it
        // was saved.
        _placements.Restore(frame, WindowPlacements.Tabs, document);

        return frame;
    }

    /// <summary>
    /// Moves an account into or out of the frame, without reopening
    /// its session.
    ///
    /// It is the same window being attached or detached: reopening
    /// it would cost several seconds and would make its virtual
    /// display be recreated.
    /// </summary>
    public async Task SetTabbedAsync(
        DofusInstance instance,
        bool tabbed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        await _settings.SetInstanceTabbedAsync(instance.Key, tabbed, cancellationToken)
            .ConfigureAwait(false);

        await RefreshRanksAsync(cancellationToken).ConfigureAwait(false);

        if (FindSession(instance) is not { } session)
        {
            return;
        }

        if (tabbed)
        {
            await AttachToTabsAsync(session, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await OnUiAsync(() => _tabs?.Detach(instance.Key)).ConfigureAwait(false);

            // The colour is set once, at launch, and a window docked then
            // had no frame of its own to carry it: taken out of the frame,
            // it came back with no border colour at all.
            await OnUiAsync(() => RefreshWindowColours()).ConfigureAwait(false);
        }

        // A second time, and it is the only one that matters for the
        // arrange shortcuts. The first one fired before the frame
        // existed: the count of windows to arrange therefore saw it
        // as absent, and "one free window plus one tab" made one
        // instead of two. Both shortcuts disappeared even though
        // there really were two windows to arrange.
        ArrangeableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The account's icon, as the list shows it.</summary>
    private string? IconFor(ScrcpySession session) =>
        _iconDirectory is null ? null : System.IO.Path.Combine(_iconDirectory, "scrcpy.png");

    /// <summary>True if a session is already open on this phone.</summary>
    /// <summary>
    /// A game already open on the phone, the one carrying its sound. A tool
    /// window does not count: it never carries the sound, and counted, it
    /// left the game opened next silent for its whole session.
    /// </summary>
    private bool HasOpenSessionOn(string deviceId) =>
        _sessions.ActiveSessions.Any(
            s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal) && !s.Target.IsTransient);

    private async Task ApplyWindowSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        await RefreshRanksAsync(cancellationToken).ConfigureAwait(false);

        // The chosen tier is rendered as is. It was for a time
        // trimmed down by a link calculation; measurement showed
        // that calculation cost sharpness for no gain, bandwidth
        // having never been the limiting factor. See the decision
        // about jitter.
        _quality = QualityProfile.For(settings.Quality, settings.CustomQuality);

        _zoom = settings.GameZoom;
        _sharpImage = settings.SharpImage;
        _sessions.StopAppOnClose = settings.StopAppOnClose;
        _windows.Anchor = settings.GameAnchor;
        _windows.Presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(false);

        // The empty list only serves to reset the size state
        // without touching the windows, which will be placed
        // afterwards.
        await _windows.ApplySizeAsync([], settings.SizeIndex, cancellationToken).ConfigureAwait(false);

        if (settings.CustomSizePercent > 0)
        {
            await _windows.ApplyPercentAsync([], settings.CustomSizePercent, cancellationToken)
                .ConfigureAwait(false);
        }

        // The hotkeys were armed before this size was read, at startup.
        await ArmEscapeAsync().ConfigureAwait(false);
    }

    private async Task EnsureHotkeysAsync(CancellationToken cancellationToken)
    {
        if (_hotkeysWired)
        {
            return;
        }

        _hotkeysWired = true;

        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _hotkeys.ForegroundWindowChanged += OnForegroundChanged;
        _hotkeys.MouseCaptured += OnMouseCaptured;

        await ReloadHotkeysAsync(cancellationToken).ConfigureAwait(false);
        await _hotkeys.SetEnabledAsync(true).ConfigureAwait(false);
    }

    /// <summary>Each phone's API level, known from its last launch.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int?> _sdks =
        new(StringComparer.Ordinal);

    /// <summary>When each phone was last asked whether it sleeps.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _sleepAsked =
        new(StringComparer.Ordinal);

    /// <summary>
    /// One question per phone at most this often, however fast the clicks
    /// come: a phone that fell asleep is woken within fifteen seconds of
    /// clicking, and at the first click after a quiet spell that long.
    /// </summary>
    private static readonly TimeSpan SleepAskInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A click in a game window whose phone sleeps wakes the phone up: on
    /// some phones, see <see cref="PhoneSleep" />, a sleeping phone drops
    /// every click while its image keeps coming. That click is lost, the
    /// next ones arrive.
    ///
    /// Only for the Android versions concerned: elsewhere, nothing is asked
    /// and nothing changes. The screen DT Hub held off is switched off again
    /// once the phone is awake.
    /// </summary>
    private async void OnMouseCaptured(object? sender, nint window)
    {
        try
        {
            // Every window on the desktop that takes the mouse comes through
            // here: the filter must cost nothing.
            var session = window == 0
                ? null
                : _sessions.ActiveSessions.FirstOrDefault(s => s.IsAlive && s.WindowHandle == window);

            if (session is null
                || !_sdks.TryGetValue(session.Target.DeviceId, out var sdk)
                || !PhoneSleep.DropsClicksWhenAsleep(sdk))
            {
                return;
            }

            var deviceId = session.Target.DeviceId;
            var now = Environment.TickCount64;

            if (_sleepAsked.TryGetValue(deviceId, out var asked)
                && now - asked < (long)SleepAskInterval.TotalMilliseconds)
            {
                return;
            }

            _sleepAsked[deviceId] = now;

            // Off the hotkey thread before adb starts: the shortcuts and the
            // foreground watch wait on it.
            await Task.Yield();

            var serial = Routable(session.Target);

            if (await _devices.IsAsleepAsync(serial).ConfigureAwait(false) != true
                || !await _devices.WakeAsync(serial).ConfigureAwait(false))
            {
                return;
            }

            LogPhoneWoken(deviceId);

            if (!_sessions.HoldsScreenOff(deviceId))
            {
                return;
            }

            // Switched off again only once lit: on a dark screen, replacing
            // the screen-off session would flash it for nothing.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await Task.Delay(250).ConfigureAwait(false);

                if (await _devices.IsPanelLitAsync(serial).ConfigureAwait(false) == true)
                {
                    await TurnPanelOffAsync(deviceId).ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogWakeFailure(exception);
        }
    }

    /// <summary>
    /// Shortcuts stay active as long as a window of the application
    /// is in the foreground, game windows as much as the
    /// configurator. Elsewhere, the key combinations go back to
    /// other software.
    /// </summary>
    private async void OnForegroundChanged(object? sender, nint window)
    {
        try
        {
            // **The window in the foreground now, not the one the event
            // names.** Alt+Tab back to the frame raised the frame's event,
            // then one more for Windows' switcher as it went away: the
            // shortcuts turned on, then off, with the frame in front, and
            // Ctrl+Tab stayed dead until one left the window and came back.
            // Measured on 2026-10-03: "actifs" at 14:57:18.573, "en veille"
            // for the switcher at .575. A late event then reads the window
            // that is really there.
            if (_windows.Controller.GetForegroundWindow() is var current and not 0)
            {
                window = current;
            }

            // Recognized by its process, not by the handle we have
            // on record: that of a freshly reopened session is not
            // resolved yet, and the shortcuts would then think they
            // were away from home. They stayed off until one clicked
            // elsewhere and then back on a game window.
            var owner = _windows.GetWindowProcessId(window);

            // The tabbed frame counts among our windows. It was
            // missing: clicking on the tab bar or the frame's edge
            // to switch accounts turned off all twelve shortcuts,
            // Ctrl+P included, until one clicked back into the
            // game's image.
            var ours = OwnsWindow?.Invoke(window) == true
                       || (_tabs is { Handle: var frame } && frame != 0 && frame == window);

            var mine = HotkeyScope.Holds(
                window,
                owner,
                _sessions.ActiveSessions.Select(s => new SessionWindow(s.WindowHandle, s.ProcessId)),
                ours);

            // Back on one of ours by a click, a tab or Alt+Tab: a game window
            // left behind by a shortcut may still believe Ctrl is down.
            if (mine)
            {
                _ = _modifiers.ReleaseUnlessHeld(GameWindows(), MouseCapturingWindows());
            }

            // The toggle is logged: without it, shortcuts turned off
            // by an unrecognized window left no trace, and the
            // symptom looked like a shortcut that "no longer works".
            if (mine != _hotkeysActive)
            {
                _hotkeysActive = mine;

                LogHotkeyScope(mine ? "actifs" : "en veille", window);
            }

            _gameInFront = mine && OwnsWindow?.Invoke(window) != true;
            await ArmEscapeAsync().ConfigureAwait(false);

            await _hotkeys.SetEnabledAsync(mine).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // These two guards run on a background thread, at the
            // pace of foreground window changes. A fault there is
            // frequent and harmless for the session; it goes to the
            // log, and the panel's line says an incident was
            // recorded.
            LogHotkeyFailure(exception);
        }
    }

    private bool _hotkeysActive = true;

    /// <summary>
    /// Keyboard navigation switches free windows when one of them is in
    /// front, and the frame's tabs otherwise, bringing the frame back
    /// first if another of our windows held the keyboard.
    ///
    /// Ctrl+Tab used to do nothing inside the frame: navigation goes
    /// through <see cref="ManagedSessions"/>, which sets housed
    /// accounts aside because the automatic placements have nothing
    /// to say to them. The keyboard, though, still needed them, and
    /// the shortcut therefore did not mean the same thing depending
    /// on the mode.
    ///
    /// The foreground window stays the frame even when it is the
    /// game that holds the keyboard: housed, it is a child of the
    /// frame and cannot be the foreground window itself.
    /// </summary>
    private async Task<bool> CycleTabsAsync(bool forward)
    {
        if (_tabs is not { } frame || frame.Handle == 0)
        {
            return false;
        }

        var foreground = _windows.Controller.GetForegroundWindow();

        // A free game window in front keeps cycling among the free ones.
        if (foreground != frame.Handle
            && ManagedSessions.Any(s => s.IsAlive && s.WindowHandle == foreground))
        {
            return false;
        }

        // **From another of our windows, the frame comes back.** Typed in
        // the guides, the panel or the almanax, Ctrl+Tab did nothing: the
        // frame was not in front, and there was no free window to move to.
        // It stayed dead until one clicked back into the game. Seen on
        // 2026-10-03 at 15:53:39, the guides holding the keyboard. Free
        // windows already come forward this way.
        var raise = foreground != frame.Handle;
        var tourne = false;

        await OnUiAsync(() =>
        {
            if (raise)
            {
                _ = frame.Activate();
            }

            tourne = frame.Cycle(forward);
        }).ConfigureAwait(false);

        return tourne;
    }

    /// <summary>
    /// The key that brings this open account forward, as its row and tab
    /// show it, or <c>null</c> when it is closed or has none.
    /// </summary>
    public string? AccountKeyOf(string key) =>
        _hotkeySet?.AccountKeyText(
            AccountSessions().FindIndex(s => string.Equals(s.Target.Key, key, StringComparison.Ordinal)));

    /// <summary>The shortcuts last applied, for the badges.</summary>
    private volatile HotkeySet? _hotkeySet;

    /// <summary>
    /// The open accounts, one per line of the list: windows opened in
    /// passing are not accounts.
    /// </summary>
    private List<ScrcpySession> AccountSessions() => [.. ActiveSessions.Where(s => !s.Target.IsTransient)];

    /// <summary>
    /// Brings forward the open account on this line of the list, counted
    /// from zero: its tab when the frame houses it, its window otherwise.
    /// Locked windows count, unlike for Ctrl+Tab: the lock keeps a window
    /// from moving, not from being reached on purpose. The tabs follow the
    /// list's order, so a single count holds in both modes.
    /// </summary>
    private async Task FocusAccountAsync(int line)
    {
        if (AccountSessions().ElementAtOrDefault(line) is not { } session)
        {
            return;
        }

        var housed = false;

        if (_tabs is { } frame && frame.Handle != 0)
        {
            await OnUiAsync(() =>
            {
                housed = frame.Holds(session.Target.Key);

                if (housed)
                {
                    _ = frame.Activate();
                    frame.Show(session.Target.Key);
                }
            }).ConfigureAwait(false);
        }

        if (!housed)
        {
            _ = _windows.Focus(session);
        }
    }

    /// <summary>
    /// Lets the UI declare its own windows, so that the shortcuts
    /// also work from the configurator.
    /// </summary>
    public Func<nint, bool>? OwnsWindow { get; set; }

    private async void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        try
        {
            // First, before the shortcut moves the focus: the window it was
            // typed in got the Ctrl press, and the release will go elsewhere.
            _modifiers.Release(GameWindows(), MouseCapturingWindows());

            switch (action)
            {
                case HotkeyAction.ToggleConfigurator:
                    ConfiguratorToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case HotkeyAction.NextInstance:
                    if (!await CycleTabsAsync(forward: true).ConfigureAwait(false))
                    {
                        _windows.FocusNext(ManagedSessions);
                    }

                    break;

                case HotkeyAction.PreviousInstance:
                    if (!await CycleTabsAsync(forward: false).ConfigureAwait(false))
                    {
                        _windows.FocusPrevious(ManagedSessions);
                    }

                    break;

                case >= HotkeyAction.Account1 and <= HotkeyAction.Account8:
                    await FocusAccountAsync(action - HotkeyAction.Account1).ConfigureAwait(false);
                    break;

                case HotkeyAction.Rearrange:
                    await StackOnActiveAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Tile:
                    await TileAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Grid:
                    await GridAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Quests:
                    QuestsToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case HotkeyAction.Almanax:
                    AlmanaxRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case HotkeyAction.Size1:
                    await ApplySizeAsync(0).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size2:
                    await ApplySizeAsync(1).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size3:
                    await ApplySizeAsync(2).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size4:
                    await ApplySizeAsync(3).ConfigureAwait(false);
                    break;

                case HotkeyAction.Fullscreen:
                    await ToggleFullscreenAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.LeaveFullscreen:
                    await ToggleFullscreenAsync(leaveOnly: true).ConfigureAwait(false);
                    break;

                case HotkeyAction.ScreenOff:
                    await TurnLitPanelsOffAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Quit:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // These two guards run on a background thread, at the
            // pace of foreground window changes. A fault there is
            // frequent and harmless for the session; it goes to the
            // log, and the panel's line says an incident was
            // recorded.
            LogHotkeyFailure(exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Écran de {Serial} lu {Verdict}. Réponse du téléphone :\n{Answer}")]
    private partial void LogPanelRead(string serial, string verdict, string answer);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Écran de {Device} tenu éteint, {How}.")]
    private partial void LogScreenHeld(string device, string how);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "La session qui tient l'écran de {Device} éteint {How}, code {ExitCode}, dernières lignes : {LastLines}")]
    private partial void LogScreenReleased(string device, string how, int? exitCode, string lastLines);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Format de l'écran du téléphone {Device} : {Aspect}.")]
    private partial void LogPhoneAspect(string device, string aspect);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{Window} : le jeu sur son afficheur {Display} ; {State}")]
    private partial void LogGameWindow(string window, string display, string state);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Window} : le téléphone dessine le jeu à {Percent} % de son afficheur puis l'agrandit, d'où une image floue. Réglage du téléphone (mode jeu de cette copie), pas de DT Hub.")]
    private partial void LogReducedRender(string window, int percent);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{Window} : mode de jeu Android mis sur {Mode}.")]
    private partial void LogGameModeSet(string window, string mode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Window} : le signal qui garde l'app éveillée n'est pas passé : {Reason}")]
    private partial void LogKeepAwakeFailed(string window, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La surveillance d'une installation depuis le Play Store a échoué.")]
    private partial void LogStoreWatchFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recherche des outils DOFUS Touch interrompue.")]
    private partial void LogPartnerSweepFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "{App} installée sur {Device} : la fenêtre du Play Store se ferme.")]
    private partial void LogPartnerInstalled(string app, string device);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lancement terminé : {opened} fenêtre(s) ouverte(s), {problems} problème(s).")]
    private partial void LogLaunch(int opened, int problems);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Préambule d'ouverture : raccourcis {hotkeysMs} ms, appareils {devicesMs} ms, "
            + "liaison {linkMs} ms, réglages {settingsMs} ms, total {totalMs} ms.")]
    private partial void LogPreamble(
        long hotkeysMs,
        long devicesMs,
        long linkMs,
        long settingsMs,
        long totalMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La coupure d'ADB sur {address} a échoué : {reason}. L'association est rompue quand même.")]
    private partial void LogDisconnectFailed(string address, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "L'appareil {serial} se bride : état thermique {status}, surface {skin} °C.")]
    private partial void LogHeat(string serial, int status, double skin);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{serial} : l'afficheur virtuel suit le verrouillage et le téléphone est verrouillé ; ses fenêtres montrent le cadenas, pas le jeu.")]
    private partial void LogDisplayLocked(string serial);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{serial} : le jeu n'est pas exempté d'économie d'énergie ; Android finira par le geler.")]
    private partial void LogUnprepared(string serial);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{serial} : l'appareil refuse la simulation d'entrée ; ses fenêtres ne répondront pas.")]
    private partial void LogDeadInput(string serial);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "L'appareil {serial} déclare {count} encodeurs vidéo, matériel pour : {hardware}.")]
    private partial void LogEncoders(string serial, string hardware, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "L'appareil {serial} est à {percent} % de batterie, non branché, batterie à {celsius} °C.")]
    private partial void LogBattery(string serial, int percent, double celsius);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "L'appareil {serial} n'a plus que {free} Go libres.")]
    private partial void LogStorage(string serial, double free);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Fenêtre perdue, réouverture de {name} dans {seconds} s (tentative {attempt}).")]
    private partial void LogRecovering(string name, int attempt, int seconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Réouverture de {name} abandonnée après {attempts} tentatives.")]
    private partial void LogRecoveryGaveUp(string name, int attempts);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Liaison : filaire, ou Wi-Fi que l'appareil ne décrit pas. Aucun tampon.")]
    private partial void LogUnknownLink();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Liaison : {standard} à {frequencyMhz} MHz, {linkSpeedMbps} Mb/s annoncés, "
            + "{rssi} dBm, {retryPercent} % de réémissions. "
            + "Tampon d'affichage {bufferMs} ms.")]
    private partial void LogLink(
        string standard,
        int frequencyMhz,
        int linkSpeedMbps,
        int rssi,
        double retryPercent,
        int bufferMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Aucune géométrie mémorisée pour {instances} : la fenêtre rouvre au coin par défaut.")]
    private partial void LogMissingGeometry(string instances);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{instance} : afficheur prêt en {displayMs} ms, démarrage complet en {totalMs} ms.")]
    private partial void LogStartupTiming(string instance, long displayMs, long totalMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{instance} : première image en {firstImageMs} ms, fenêtre noire jusque-là.")]
    private partial void LogFirstImage(string instance, long firstImageMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{instance} : afficheur {width}x{height} à {dpi} ppp, {fps} ips, {kbps} kb/s.")]
    private partial void LogStreamSettings(
        string instance,
        int width,
        int height,
        int dpi,
        int fps,
        int kbps);

    [LoggerMessage(Level = LogLevel.Information, Message = "{count} téléphone(s) reconnecté(s) automatiquement.")]
    private partial void LogReconnected(int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Compte « {name} » ajouté sur {device} : {succeeded}. {message}")]
    private partial void LogAccountAdded(string device, string name, bool succeeded, string message);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Applications de {device} lues : {labels} nom(s), {apps} application(s) lançable(s) hors jeu.")]
    private partial void LogAppsListed(string device, int labels, int apps);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Applications affichées sur {deviceId} : {count} choisie(s).")]
    private partial void LogShownAppsChanged(string deviceId, int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{count} compte(s) oublié(s) : leur profil Android a disparu, "
            + "ou le jeu n'y est plus installé.")]
    private partial void LogProfilesForgotten(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ouverture de {instance} refusée.{newLine}Commande : {commandLine}{newLine}Sortie de scrcpy :{newLine}{output}")]
    private partial void LogSessionFailure(string instance, string commandLine, string output, string newLine = "\n");

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} refusée en {height} de haut : nouvel essai en {retry} de haut.")]
    private partial void LogDisplayFallback(string instance, int height, int retry);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} : fenêtre introuvable, le compte reste en fenêtre libre.")]
    private partial void LogTabWindowMissing(string instance);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} non lancée : appareil au niveau d'API {sdk}, sous le minimum requis.")]
    private partial void LogAndroidTooOld(string instance, int sdk);

    [LoggerMessage(Level = LogLevel.Information, Message = "Raccourcis {state} (fenêtre {window}).")]
    private partial void LogHotkeyScope(string state, nint window);

    [LoggerMessage(Level = LogLevel.Information, Message = "Écrans : {monitors}. Fenêtres de jeu : {placement}.")]
    private partial void LogPlacement(string monitors, string placement);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Relance courte de {instance} impossible ({reason}) : la fenêtre est rouverte.")]
    private partial void LogRestartFallback(string instance, string reason);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mémoire de l'appareil {serial} selon Android : {level}, {available} Go disponibles.")]
    private partial void LogMemory(string serial, string level, double available);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Processeur de l'appareil {serial} sur les 30 dernières secondes : {busy} % occupé, {game} % pour le jeu, kswapd0 à {reclaim} % d'un cœur, allumé depuis {days} j.")]
    private partial void LogStrain(string serial, double busy, double game, double reclaim, double days);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Un raccourci n'a pas pu être traité.")]
    private partial void LogHotkeyFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Un geste sur le cadre à onglets n'a pas pu être traité.")]
    private partial void LogTabsFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le nouveau nom n'a pas pu être posé sur les fenêtres ouvertes.")]
    private partial void LogRenameFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "L'état du téléphone n'a pas pu être posé dans le titre des fenêtres.")]
    private partial void LogTitleStatusFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le jeu ne joue plus aucun son sur l'appareil {serial} depuis au moins une minute : {processes} jeu(x) ouvert(s), capture du son pour le PC {captured}, volume média du téléphone {volume}/15.")]
    private partial void LogGameSilent(string serial, int processes, bool captured, string volume);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le jeu joue du son sur l'appareil {serial} ({players} lecteur(s)) mais plus rien ne le capte pour le PC depuis au moins une minute.")]
    private partial void LogSoundNotCaptured(string serial, int players);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Le son du jeu est revenu sur l'appareil {serial}.")]
    private partial void LogSoundBack(string serial);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le son du jeu n'est pas revenu sur l'appareil {serial} depuis la réouverture des fenêtres ({windows} fenêtre(s) de DT Hub). Son du téléphone :\n{state}")]
    private partial void LogSoundStuck(string serial, int windows, string state);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Pics de latence Wi-Fi vers l'appareil {serial} : {count} en une minute, jusqu'à {worst} ms.")]
    private partial void LogPingSpikes(string serial, int count, long worst);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Les pics de latence Wi-Fi vers l'appareil {serial} ont cessé.")]
    private partial void LogPingSpikesOver(string serial);

    [LoggerMessage(Level = LogLevel.Information, Message = "Téléphone {device} endormi au clic : réveillé.")]
    private partial void LogPhoneWoken(string device);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Le réveil d'un téléphone endormi a échoué.")]
    private partial void LogWakeFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le temps de jeu n'a pas pu être enregistré à la fermeture.")]
    private partial void LogPlaytimeFailure(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La nouvelle couleur n'a pas pu être posée sur les onglets ouverts.")]
    private partial void LogColourFailure(Exception exception);


    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La session {instance} s'est terminée seule ({state}) : {message}\n{output}")]
    private partial void LogSessionEnded(string instance, string state, string message, string output);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Le jeu de {instance} n'a pas pu être arrêté : {serial} est resté injoignable.")]
    private partial void LogGameLeftRunning(string instance, string serial);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Association rompue avec {device} : ses comptes et ses réglages sont effacés.")]
    private partial void LogDeviceForgotten(string device);
}

/// <summary>How opening a MakeInTouch app, or its store page, went.</summary>
public enum PartnerOpening
{
    /// <summary>In a window on the PC.</summary>
    InWindow,

    /// <summary>On the phone's own screen, the window having failed.</summary>
    OnPhone,

    /// <summary>The phone has no Play Store.</summary>
    StoreMissing,

    /// <summary>Nothing opened.</summary>
    Failed,
}
