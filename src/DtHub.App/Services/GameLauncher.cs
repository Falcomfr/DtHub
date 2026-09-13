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
public sealed record DeviceFindings(string Text, bool Serious, string? Device);

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

    /// <summary>
    /// True during a deliberate close: no need to log its detail.
    /// </summary>
    private bool _closing;

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
        ILogger<GameLauncher> logger)
    {
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
        _logger = logger;

        // A session that dies right after opening left no trace at
        // all: the window vanished and the log stayed silent.
        _sessions.SessionChanged += OnSessionChanged;

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
    private readonly Dictionary<string, int> _videoBuffers = new(StringComparer.Ordinal);

    /// <summary>Pace of controls and polling, based on quality.</summary>
    public QualityProfile Quality => _quality;

    /// <summary>
    /// Instances already launched, to know what to reopen when a
    /// window drops. The session only carries a target, not the
    /// original instance.
    /// </summary>
    private readonly Dictionary<string, DofusInstance> _launched = new(StringComparer.Ordinal);

    /// <summary>
    /// The distance each window was opened with.
    ///
    /// It is fixed for the whole session, scrcpy receiving it as a
    /// startup argument. The account list uses it to say that a
    /// window is still running with the old one, rather than let
    /// the setting seem dead.
    /// </summary>
    private readonly Dictionary<string, GameZoom> _zoomsInUse = new(StringComparer.Ordinal);

    /// <summary>
    /// Recovery attempts per instance, and time of the last one.
    /// </summary>
    private readonly Dictionary<string, (int Count, DateTimeOffset Last)> _recoveries =
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
        // The window is back: the recovery notice no longer has a
        // reason to exist. The attempt count, though, survives, and
        // that is deliberate: a link that flickers must eventually
        // exhaust its credit.
        if (session.State == ScrcpySessionState.Running)
        {
            _recovery = default;
        }

        if (session.IsAlive || _closing)
        {
            return;
        }

        LogSessionEnded(
            session.Target.DisplayName,
            session.State.ToString(),
            session.FailureMessage ?? "aucun message",
            string.Join(Environment.NewLine, session.RecentOutput));

        CountPlaytime(session);

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
                LogRecoveryGaveUp(instance.DisplayName, already);
            }

            return false;
        }

        _recoveries[key] = (already + 1, DateTimeOffset.UtcNow);

        _recovery = TimedNotice.Raised(
            Strings.Format("SessionRecovering", instance.DisplayName), DateTimeOffset.UtcNow);
        _recoveryDevice = instance.DeviceId;

        LogRecovering(instance.DisplayName, already + 1, (int)decision.Delay.TotalSeconds);

        RecoveryRequested?.Invoke(this, new RecoveryRequest(instance, decision.Delay));

        return true;
    }

    /// <summary>Sessions whose playtime has already been counted.</summary>
    private readonly HashSet<string> _counted = new(StringComparer.Ordinal);

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
        if (!session.EverRan || !_counted.Add(session.Id))
        {
            return;
        }

        var seconds = (int)(DateTimeOffset.UtcNow - session.StartedUtc).TotalSeconds;

        if (seconds < SettingsService.MinimumCountedPlaytimeSeconds)
        {
            return;
        }

        _ = _settings.AddPlaytimeAsync(session.Target.Key, seconds);
    }

    /// <summary>Video encoders known per device, once asked for.</summary>
    private readonly Dictionary<string, IReadOnlyList<VideoEncoder>> _encoders = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices whose list has already been asked for, whether it
    /// succeeded or not.
    /// </summary>
    private readonly HashSet<string> _encodersAsked = new(StringComparer.Ordinal);

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
            if (!_encodersAsked.Add(serial!))
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
    public IReadOnlyList<ScrcpySession> ManagedSessions =>
        [.. _sessions.ActiveSessions.Where(
            s => !_unmanaged.Contains(s.Target.Key) && !_tabbed.Contains(s.Target.Key))];

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
                frame.SetFullscreen(false, default);

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
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        _instances.PackageName = settings.PackageName;

        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        RememberSerials(discovery);

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

    /// <summary>Last thermal state logged per device.</summary>
    private readonly Dictionary<string, int> _loggedHeat = new(StringComparer.Ordinal);

    /// <summary>Last battery tier logged per device.</summary>
    private readonly Dictionary<string, int> _loggedBattery = new(StringComparer.Ordinal);

    /// <summary>
    /// Devices whose lack of storage has already been logged.
    /// </summary>
    private readonly HashSet<string> _loggedStorage = new(StringComparer.Ordinal);

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

        var alive = _sessions.ActiveSessions
            .Where(s => s.IsAlive)
            .Select(s => s.Target.Serial)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var playing = alive.Distinct(StringComparer.Ordinal).ToList();

        var serials = playing.Count > 0
            ? playing
            : [.. discovery.Devices
                .Where(d => d.IsConnected)
                .Select(d => d.Serial)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)];

        if (serials.Count == 0)
        {
            HealthIsSerious = false;
            _health.Clear();
            _loggedHeat.Clear();
            _loggedBattery.Clear();
            _loggedStorage.Clear();
            return;
        }

        List<HealthFinding> findings = [];

        foreach (var serial in serials)
        {
            var heat = await _devices.GetThermalAsync(serial, cancellationToken).ConfigureAwait(false);
            var battery = await _devices.GetBatteryAsync(serial, cancellationToken).ConfigureAwait(false);
            var storage = await _devices.GetStorageAsync(serial, cancellationToken).ConfigureAwait(false);
            var link = await _devices.GetWifiLinkAsync(serial, cancellationToken).ConfigureAwait(false);

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

            // Battery preparation has long been described in the
            // help; this check only says whether it has been done.
            // The question also matters before launch: that is the
            // moment when it can still be fixed.
            var unprepared = await _devices
                .IsBatteryExemptAsync(serial, DofusPackages.DofusTouch, cancellationToken)
                .ConfigureAwait(false) == false;

            // **The input probe, once per device, on its first
            // window.** It sends Android's "unknown" key, which
            // triggers nothing anywhere, and returns its verdict
            // within a second.
            //
            // The type that carries it used to say it is only sent
            // on request. That rule changes here, and the field
            // forced it: twice in the same day, windows showed the
            // game without responding to anything, and the guilty
            // setting unchecks itself on its own at restart. Never
            // during a game, never repeatedly: one question, at the
            // moment the first window opens.
            var dead = playing.Contains(serial, StringComparer.Ordinal)
                && await Inputs(serial, cancellationToken).ConfigureAwait(false) == InputInjection.Denied;

            Trace(serial, heat, battery, storage);
            TraceLock(serial, locked);
            TracePreparation(serial, unprepared);
            TraceDeadInput(serial, dead);

            var seen = DeviceHealth.Review(
                heat, battery, storage, link, locked, unprepared, dead);

            if (DeviceHealth.Every(seen) is { } said)
            {
                _health[serial] = new DeviceFindings(
                    said,
                    seen[0].Severity == HealthSeverity.Serious,
                    Named(discovery, serial));
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

        foreach (var gone in _health.Keys.Where(s => !connected.Contains(s, StringComparer.Ordinal)).ToList())
        {
            _ = _health.Remove(gone);
        }

        foreach (var gone in _batteries.Keys.Where(s => !connected.Contains(s, StringComparer.Ordinal)).ToList())
        {
            _ = _batteries.Remove(gone);
        }

        // The input verdict says it is "kept until the device
        // disappears", and nothing made that true: a phone that
        // refused input once kept the simulated mouse offered in the
        // settings long after it had been unplugged.
        foreach (var gone in _inputs.Keys.Where(s => !connected.Contains(s, StringComparer.Ordinal)).ToList())
        {
            _ = _inputs.Remove(gone);
        }

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
            var whole = known.Count > 0
                && known.All(d => live.Devices.Any(l =>
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

                var missing = known.Where(d => !connected.Contains(d.Id)).ToList();

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

            remembered.TryGetValue(instance.Key, out var stored);

            var placement = ComputePlacement(options, stored);

            var target = ToTarget(instance, device.Serial);

            // The account's own tier, or the shared one if none was chosen.
            var quality = qualities.TryGetValue(instance.Key, out var own) ? own : _quality;

            // The account's own distance, or the shared one if not chosen.
            var zoom = zooms.TryGetValue(instance.Key, out var ownZoom) ? ownZoom : _zoom;

            _zoomsInUse[instance.Key] = zoom;

            var display = WithDisplayFor(options, placement, stored, quality, zoom) with
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
            if (display.AudioEnabled && HasOpenSessionOn(instance.DeviceId))
            {
                display = display with { AudioEnabled = false };
            }

            var session = await _sessions.StartAsync(
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
                    VirtualDisplayDpi = ZoomProfile.DpiFor(smaller.Height, _zoom),
                    VideoBitrateKbps = quality.BitrateFor(smaller.Width, smaller.Height),
                };

                session = await _sessions.StartAsync(
                    target, display, placement, cancellationToken).ConfigureAwait(false);
            }

            if (session.State == ScrcpySessionState.Failed)
            {
                problems.Add($"{instance.DisplayName} : {session.FailureMessage}");

                // Without scrcpy's output, a refusal boils down to
                // "the session could not open", which helps no one.
                LogSessionFailure(
                    instance.DisplayName,
                    session.CommandLine,
                    string.Join(Environment.NewLine, session.RecentOutput));

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

        }

        // What has just opened will reopen at the next launch. Only
        // the "Close" button removes an instance from this set:
        // closing a game window by hand should change nothing here.
        if (started.Count > 0)
        {
            await _settings.SetInstancesEnabledAsync(
                [.. started.Select(s => s.Target.Key)], enabled: true, cancellationToken)
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
            return new LaunchReport(1, []);
        }

        // The short restart failed: everything is closed and
        // reopened, which remains the last useful resort.
        LogRestartFallback(instance.DisplayName, restart.UserMessage ?? "afficheur inconnu");

        await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);

        return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
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
    /// StopSessionAsync raises the closing flag so that a window we close on
    /// purpose is not taken for a game that died. That is right for recovery,
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
    /// The flag keeps closing the last session from also closing
    /// the application: it is us doing the closing, not the game
    /// dying.
    /// </summary>
    private async Task StopSessionAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        _closing = true;

        try
        {
            await _sessions.StopAsync(session.Id, cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }
        finally
        {
            _closing = false;
        }

        // The tab goes with the session. Leaving it made the frame
        // believe the account was still there, and reopening that
        // account no longer housed it: it reappeared as a free
        // window on top of the frame.
        if (_tabs is not null)
        {
            await OnUiAsync(() => _tabs?.Detach(session.Target.Key)).ConfigureAwait(false);
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

            _closing = true;

            try
            {
                await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
                _sessions.PruneFinished();
            }
            finally
            {
                _closing = false;
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

        _closing = true;

        try
        {
            await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }
        finally
        {
            _closing = false;

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
            [.. _sessions.ActiveSessions.Where(s => !_tabbed.Contains(s.Target.Key))]);

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

        return moved;
    }

    /// <summary>
    /// Open session matching an instance, if there is one.
    /// </summary>
    public ScrcpySession? FindSession(DofusInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return _sessions.ActiveSessions.FirstOrDefault(
            s => string.Equals(s.Target.Key, instance.Key, StringComparison.Ordinal));
    }

    /// <summary>True if the instance has an open window.</summary>
    public bool IsOpen(DofusInstance instance) => FindSession(instance) is not null;

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

        // The reminder written into the windows' title follows the
        // key combination. This is where it must happen: the editor
        // reloads through this path, whichever window opened it.
        await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);

        return await _hotkeys.ApplyAsync(hotkeys).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the shortcut reminder in the title of open windows.
    /// Called after a change made in the editor.
    /// </summary>
    public async Task<int> RefreshWindowTitlesAsync(CancellationToken cancellationToken = default)
    {
        var hint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        return _windows.Retitle(
            _sessions.ActiveSessions,
            session => ScrcpyCommandBuilder.BuildWindowTitle(CurrentName(session, settings), hint));
    }

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
        GameZoom zoom)
    {
        if (placement is not { Height: > 0 } window
            || _windows.MonitorBoundsFor(remembered) is not { } screen)
        {
            return options;
        }

        var (width, height) = DisplayLadder.For(
            window.Height, screen.Width, screen.Height, quality.MaximumDisplayHeight);

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
            VirtualDisplayDpi = ZoomProfile.DpiFor(height, zoom),
            VideoBitrateKbps = quality.BitrateFor(width, height),

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

        return parts.Count > 0 ? string.Join("  ·  ", parts) : null;
    }

    private static LaunchTarget ToTarget(DofusInstance instance, string serial) => new()
    {
        DeviceId = instance.DeviceId,
        Serial = serial,
        UserId = instance.UserId,
        PackageName = instance.PackageName,
        LaunchComponent = instance.LaunchComponent,
        DisplayName = instance.DisplayName,
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
                session.SourceAspectRatio);

            frame.Reorder(order);
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
    /// The frame, created on first need and kept open afterwards.
    /// </summary>
    /// <summary>
    /// Closes the accounts the tabbed frame was housing.
    ///
    /// The account behaves as if its window had been closed one by
    /// one: its place is remembered, and it will not reopen on its
    /// own at the next startup. It stays housed in tabs, so it will
    /// return there the day it is reopened.
    ///
    /// A session that survives the stop gets its window back
    /// visible: it left the frame hidden, and leaving it that way
    /// would make it unfindable.
    /// </summary>
    private async Task CloseTabbedAsync(IReadOnlyList<string> keys)
    {
        List<string> fermes = [];

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

                continue;
            }

            fermes.Add(key);
        }

        if (fermes.Count > 0)
        {
            await _settings.SetInstancesEnabledAsync(fermes, enabled: false).ConfigureAwait(false);
        }

        NotifyIfNothingLeft();
    }

    private Windows.TabbedGameWindow EnsureTabs(AppSettingsDocument document)
    {
        if (_tabs is { } existant)
        {
            return existant;
        }

        var frame = new Windows.TabbedGameWindow(_windows.Controller);

        frame.Closed += (_, _) => _tabs = null;

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
    private bool HasOpenSessionOn(string deviceId) =>
        _sessions.ActiveSessions.Any(
            s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal));

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

        await ReloadHotkeysAsync(cancellationToken).ConfigureAwait(false);
        await _hotkeys.SetEnabledAsync(true).ConfigureAwait(false);
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

            // The toggle is logged: without it, shortcuts turned off
            // by an unrecognized window left no trace, and the
            // symptom looked like a shortcut that "no longer works".
            if (mine != _hotkeysActive)
            {
                _hotkeysActive = mine;

                LogHotkeyScope(mine ? "actifs" : "en veille", window);
            }

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
    /// Keyboard navigation switches tabs when the frame has focus,
    /// and switches free windows otherwise.
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
        if (_tabs is not { } frame
            || frame.Handle == 0
            || _windows.Controller.GetForegroundWindow() != frame.Handle)
        {
            return false;
        }

        var tourne = false;

        await OnUiAsync(() => tourne = frame.Cycle(forward)).ConfigureAwait(false);

        return tourne;
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

                case HotkeyAction.Rearrange:
                    await StackOnActiveAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Tile:
                    await TileAsync().ConfigureAwait(false);
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
                    await ApplySizeAsync(_windows.Presets.FullscreenIndex).ConfigureAwait(false);
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
