using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.ViewModels;

/// <summary>
/// The main window once the game has launched. Three tabs, nothing
/// more. Every change is saved immediately and applied to windows
/// already open.
/// </summary>
public sealed partial class ConfiguratorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;

    private readonly DiagnosticReporter _reporter;
    private bool _loading;
    private bool _movingWindows;
    private int? _pendingPercent;
    private CancellationTokenSource? _persistSize;

    public ConfiguratorViewModel(
        InstanceListViewModel instances,
        SettingsService settings,
        GameLauncher launcher,
        IDialogService dialogs,
        IAppPaths paths,
        DiagnosticReporter reporter,
        IUsbEnumerationInspector usb)
    {
        Instances = instances;
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;
        _reporter = reporter;
        _usb = usb;

        // A fault outside the UI thread does not show up: it gets
        // counted, and this line is the only place where it can be
        // seen.
        _reporter.IncidentRecorded += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                () => OnPropertyChanged(nameof(HasIncident)));

        // The shortcuts write the size and the anchor without going
        // through here: without this subscription, the slider and the
        // grid kept the value they had when opened and lied until the
        // restart.
        _settings.Changed += OnSettingsChanged;

        // What the connection absorbs depends on the number of open
        // windows. Without this subscription, the line reported the
        // cost of a single window while two were running, that is
        // half the truth, and exactly when it matters.
        _launcher.SessionChanged += OnSessionChanged;

        // Setting a window aside or moving it into the tabbed frame
        // touches no session, yet both arrangement shortcuts still
        // have to disappear.
        _launcher.ArrangeableChanged += OnArrangeableChanged;
    }

    /// <summary>
    /// True when there is something to arrange: at least two windows
    /// that the placements can move.
    ///
    /// Stacking or placing side by side makes no sense with a single
    /// window, and no more sense with windows set aside: these
    /// placements ignore them. The shortcuts therefore withdraw
    /// rather than do nothing when pressed.
    ///
    /// The tabbed frame counts as one window, which it has not always
    /// done: two accounts docked in it made both shortcuts disappear,
    /// and one docked account plus one free window did too, even
    /// though there were indeed two windows to arrange. A frame
    /// locked by a padlock does not count.
    /// </summary>
    public bool CanArrange => _launcher.ArrangeableCount > 1;

    private void OnArrangeableChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(
            () => OnPropertyChanged(nameof(CanArrange)));

    /// <summary>
    /// A window opened or closed: what the binding carries has
    /// changed.
    ///
    /// The event comes from a background thread, hence the dispatch
    /// through the dispatcher.
    /// </summary>
    private void OnSessionChanged(object? sender, Core.Scrcpy.ScrcpySession session) =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(BitrateSummary));
            OnPropertyChanged(nameof(LinkSummary));
            OnPropertyChanged(nameof(DisplayFitSummary));
            OnPropertyChanged(nameof(CanArrange));

            // The input verdict is settled when the first window
            // opens, so this is where the remedy can appear.
            OnPropertyChanged(nameof(ShowsSimulatedMouse));
        });

    /// <summary>
    /// Reflects a write coming from elsewhere, keyboard shortcut
    /// included.
    ///
    /// The loading guard is essential: without it, the slider
    /// resetting to the right value would retrigger a resize, and
    /// the grid a rearrangement, each one rewriting the settings in
    /// a loop.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettingsDocument document)
    {
        RememberNames(document);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        // Deliberately without await inside: two callbacks
        // interleaving on the UI thread were handing the guard back
        // and forth, and the second one lowered it while the first
        // was still writing. The slider and the quality then ran off
        // on their own, each write triggering another. The tiers
        // come from the document itself, so there is nothing to wait
        // for.
        dispatcher.InvokeAsync(() =>
        {
            if (_loading || _movingWindows)
            {
                return;
            }

            var presets = new WindowSizePresets { Percentages = document.SizePercentages }.Sanitized();

            _loading = true;

            try
            {
                GameAnchor = document.GameAnchor;
                Quality = document.Quality;
                Zoom = document.GameZoom;
                AudioEnabled = document.AudioEnabled;
                ReadCustomQuality(document);
                SizePercent = document.CustomSizePercent > 0
                    ? document.CustomSizePercent
                    : presets.PercentageAt(document.SizeIndex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public InstanceListViewModel Instances { get; }

    // Windows tab

    /// <summary>The nine possible positions of the window block.</summary>
    public IReadOnlyList<WindowAnchor> Anchors { get; } = WindowAnchors.All;

    [ObservableProperty]
    private WindowAnchor _gameAnchor = WindowAnchor.MiddleLeft;

    /// <summary>
    /// Size of the windows, as a percentage of the usable area of
    /// the screen. It is therefore proportional to the screen in
    /// use.
    /// </summary>
    [ObservableProperty]
    private int _sizePercent = 60;

    /// <summary>
    /// Trade-off between image detail and load on the machine. The
    /// image only changes when the windows reopen: the options are
    /// fixed when scrcpy launches.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomQuality))]
    private StreamQuality _quality = StreamQuality.Medium;

    /// <summary>
    /// True when the custom tier is chosen. The fine settings appear
    /// only then: showing them permanently would load the panel with
    /// four lines that most people do not need to know about.
    /// </summary>
    public bool IsCustomQuality => Quality == StreamQuality.Custom;

    /// <summary>Height of the display, at the custom tier.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    [NotifyPropertyChangedFor(nameof(DisplayFitSummary))]
    private int _customHeight = 1080;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private int _customFps = 60;

    /// <summary>
    /// Image detail, in bits per pixel per frame.
    ///
    /// And not a bitrate in megabits, unlike what interfaces that
    /// drive only a single mirror offer: here the display resolution
    /// follows the window size, and an absolute bitrate would feast
    /// a small window and starve a large one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private double _customBitsPerPixel = 0.09;

    /// <summary>
    /// "h264" or "h265". Nothing else is offered: measured with
    /// <c>scrcpy --list-encoders</c>, these are the only ones the
    /// reference phone encodes in hardware.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private string _customCodec = "h264";

    /// <summary>
    /// What the four numbers amount to, brought back to the only
    /// measure that matters.
    ///
    /// A bare bitrate means nothing: sixteen megabits are generous
    /// at 720p and miserable at 2160p. That is the mistake this
    /// project has already fallen into, and this sentence exists so
    /// it does not repeat itself under the user's hand.
    /// </summary>
    public string BitrateSummary => CurrentPlan.Summary;

    /// <summary>
    /// What the connection will receive, all windows combined.
    ///
    /// This is the line that sets this panel apart from a plain
    /// mirror's: DT Hub opens several windows on a single phone over
    /// a single connection. Three accounts at twenty-five megabits
    /// ask for seventy-five, where a Wi-Fi 4 phone on 2.4 GHz
    /// delivers around sixty.
    /// </summary>
    public string LinkSummary => CurrentPlan.LinkSummary;

    /// <summary>
    /// The resolution actually requested, and whether the chosen
    /// ceiling changes anything about it.
    ///
    /// This is the answer to a discreet trap: the tier retained is
    /// the first one above the window, so raising the ceiling beyond
    /// the window size asks for nothing more. Without this line, one
    /// believes to gain detail where nothing is gained, and sometimes
    /// pays dearly to find out.
    /// </summary>
    public string DisplayFitSummary => Core.Scrcpy.DisplayFit.Describe(
        CustomHeight,
        Core.Scrcpy.DisplayFit.FromCommandLine(
            _launcher.ActiveSessions.FirstOrDefault(s => s.IsAlive)?.CommandLine));

    private BitratePlan CurrentPlan => BitrateAdvice.Plan(
        CustomBitsPerPixel,
        CustomWidth,
        CustomHeight,
        CustomFps,
        CustomCodec,
        OpenWindowsOnBusiestDevice,
        QualityProfile.For(StreamQuality.Custom).CeilingKbps);

    /// <summary>
    /// Windows open on the phone that carries the most of them.
    ///
    /// It is that phone that decides: its connection and its encoder
    /// are the first to give way. Counting every window across every
    /// device would overstate the load on each.
    /// </summary>
    private int OpenWindowsOnBusiestDevice
    {
        get
        {
            var sessions = _launcher.ActiveSessions;

            return sessions.Count == 0
                ? 1
                : sessions
                    .GroupBy(s => s.Target.DeviceId, StringComparer.Ordinal)
                    .Max(g => g.Count());
        }
    }

    /// <summary>
    /// The width that goes with the chosen height. The game displays
    /// in landscape and the virtual display follows 16:9: asking for
    /// it separately would be one more setting for a value that can
    /// be derived.
    ///
    /// This is a <b>ceiling</b>, not the resolution retained: that
    /// one follows the window size and can be lower. The reported
    /// bitrate is therefore the highest this setting can ask for.
    /// </summary>
    private int CustomWidth => CustomHeight * 16 / 9;

    /// <summary>
    /// The heights on offer. The game displays in 16:9 and the width
    /// follows: a single list is therefore enough to describe the
    /// resolution.
    /// </summary>
    public IReadOnlyList<IntChoice> HeightChoices { get; } =
    [
        new("1280 x 720", 720),
        new("1920 x 1080", 1080),
        new("2560 x 1440", 1440),
        new("3840 x 2160", 2160),
    ];

    /// <summary>
    /// The frame rates on offer. No 120: measured on the game, it
    /// delivers thirty-eight, and asking for more would only divide
    /// the bits granted to each frame that actually exists.
    /// </summary>
    public IReadOnlyList<IntChoice> FpsChoices { get; } =
    [
        new(Strings.Format("FpsChoice", 30), 30),
        new(Strings.Format("FpsChoice", 45), 45),
        new(Strings.Format("FpsChoice", 60), 60),
    ];

    /// <summary>
    /// The detail levels on offer, in bits per pixel per frame.
    ///
    /// The numbers are shown: they do not speak to everyone, but
    /// they speak to whoever chose this tier, and they make the
    /// tiers comparable with each other. The trade reference for
    /// well-made H.264 sits around 0.10, which is what "Standard"
    /// aims for.
    /// </summary>
    public IReadOnlyList<DoubleChoice> FinesseChoices { get; } =
    [
        new(Strings.Get("FinesseThrifty"), 0.06),
        new(Strings.Get("FinesseStandard"), 0.09),
        new(Strings.Get("FinesseFine"), 0.12),
        new(Strings.Get("FinesseVeryFine"), 0.16),
    ];

    /// <summary>
    /// Two codecs, not four. AV1 and VP8 only have a software
    /// encoder on the reference phone, as measured by
    /// "scrcpy --list-encoders": offering them would cost far more
    /// than they give back.
    /// </summary>
    public IReadOnlyList<TextChoice> CodecChoices { get; } =
    [
        new("H.264", "h264"),
        new(Strings.Get("CodecH265"), "h265"),
    ];

    /// <summary>
    /// The language of the interface. The empty value follows the
    /// display language of Windows, which is what the application
    /// does when told nothing.
    ///
    /// Languages are named in their own language: that is the
    /// convention, and it is the only way to be read by whoever does
    /// not understand the one currently displayed.
    /// </summary>
    public IReadOnlyList<TextChoice> LanguageChoices { get; } =
    [
        new("Windows", string.Empty),
        new("English", "en"),
        new("Français", "fr"),
        new("Español", "es"),
    ];

    [ObservableProperty]
    private string _language = string.Empty;

    /// <summary>
    /// True when the chosen language is not the one displayed, until
    /// the application has been restarted. The windows read their
    /// texts when built: retranslating them on the fly would require
    /// rebuilding all of them, for a setting touched once.
    /// </summary>
    [ObservableProperty]
    private bool _languageRestartNeeded;

    /// <summary>
    /// The language actually displayed, set at startup and unchanged
    /// since: it is the one the windows carry.
    /// </summary>
    private readonly string _languageInForce = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    partial void OnLanguageChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        // The message used to appear on every change and never go
        // back down: going back to the starting language, that is
        // giving up, still left the invitation to restart, for an
        // application that had nothing left to change.
        //
        // What matters is not that the setting was touched, but that
        // the choice differs from what is displayed. "Windows" is
        // resolved the same way as at startup, so choosing it while
        // Windows already speaks that language asks for nothing
        // either.
        LanguageRestartNeeded = AppLanguage.NeedsRestart(
            value, CultureInfo.InstalledUICulture.Name, _languageInForce);

        _ = SaveAsync(settings => settings.Language = AppLanguage.Serves(value) ? value : string.Empty);
    }

    // Shortcuts tab, read only

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    // Miscellaneous

    public string ProductName => ProductInfo.Name;

    public string Version => ProductInfo.Version;

    /// <summary>Display shortcut, recalled in clear in the window.</summary>
    [ObservableProperty]
    private string _toggleShortcutText = "Ctrl + P";

    /// <summary>
    /// What the update banner announces. Empty, it does not appear.
    /// </summary>
    [ObservableProperty]
    private string _updateText = string.Empty;

    /// <summary>
    /// True when the application updates itself on its own. It then
    /// downloads the release in the background and installs it on
    /// quitting, never in the middle of a session.
    /// </summary>
    [ObservableProperty]
    private bool _updatesAutomatic = true;

    partial void OnUpdatesAutomaticChanged(bool value)
    {
        // The guard was missing here, the only one among all the
        // settings: reading the preferences immediately rewrote the
        // file with what had just been found in it. No visible
        // consequence, but it is a write for nothing every time the
        // panel opens.
        if (_loading)
        {
            return;
        }

        _ = SaveAsync(settings => settings.UpdatesAutomatic = value);
    }

    /// <summary>
    /// True when closing a game window also stops the game on the
    /// phone. False, the game survives its window and it reopens
    /// without having to log back in.
    /// </summary>
    [ObservableProperty]
    private bool _stopAppOnClose = true;

    partial void OnStopAppOnCloseChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = SaveAsync(settings => settings.StopAppOnClose = value);
    }

    /// <summary>
    /// True when the keyboard is presented to the phone as a
    /// plugged-in physical keyboard. The on-screen keyboard of some
    /// manufacturer overlays swallows the characters, and the window
    /// then answers the mouse without writing anything.
    /// </summary>
    [ObservableProperty]
    private bool _simulatedPhysicalKeyboard;

    /// <summary>
    /// Mouse presented to the phone as a plugged-in mouse.
    ///
    /// Last resort, and it only appears on a device whose refusal
    /// has been observed: see <see cref="ShowsSimulatedMouse" />.
    /// </summary>
    [ObservableProperty]
    private bool _simulatedPhysicalMouse;

    /// <summary>
    /// True when a device refuses input simulation, so when this
    /// mouse has a purpose.
    ///
    /// **Hidden the rest of the time, and that is the whole point.**
    /// A simulated mouse captures the machine's cursor; offering it
    /// to whoever does not have the fault would be selling a cure
    /// worse than the disease.
    /// </summary>
    public bool ShowsSimulatedMouse => _launcher.AnyInputRefused;

    partial void OnSimulatedPhysicalMouseChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetSimulatedPhysicalMouseAsync(value));
    }

    partial void OnSimulatedPhysicalKeyboardChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // The keyboard mode is a scrcpy startup argument: without
        // reopening, the setting would look dead until the next
        // session.
        _ = ApplyStartupSettingAsync(() => _settings.SetSimulatedPhysicalKeyboardAsync(value));
    }

    /// <summary>
    /// Rearrangement shortcut, shown next to the button. Empty when
    /// no shortcut is bound to the action.
    /// </summary>
    [ObservableProperty]
    private string _rearrangeShortcutText = string.Empty;

    /// <summary>Side by side shortcut, shown next to the button.</summary>
    [ObservableProperty]
    private string _tileShortcutText = string.Empty;

    /// <summary>Quest tracking shortcut, shown next to the button.</summary>
    [ObservableProperty]
    private string _questsShortcutText = string.Empty;

    /// <summary>Exit shortcut, shown under the Quit button.</summary>
    [ObservableProperty]
    private string _quitShortcutText = string.Empty;

    /// <summary>
    /// Apparent distance in the game, fixed when a session opens.
    /// </summary>
    [ObservableProperty]
    private GameZoom _zoom = GameZoom.Normal;

    /// <summary>True while the windows close and reopen.</summary>
    [ObservableProperty]
    private bool _isReopening;

    /// <summary>
    /// Send the phone's sound out to the PC.
    ///
    /// This is the sound of the whole device, not of one account:
    /// Android cannot isolate it by application. A single window per
    /// phone therefore carries it, otherwise the same stream would
    /// arrive in several copies.
    /// </summary>
    [ObservableProperty]
    private bool _audioEnabled;

    public string Disclaimer =>
        Strings.Get("Disclaimer");

    /// <summary>Loads the state of the settings into the window.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            // From the very first read: a fault can occur before any
            // write, and the report must already know what to
            // redact.
            RememberNames(settings);

            GameAnchor = settings.GameAnchor;

            var presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(true);

            Quality = settings.Quality;
            Zoom = settings.GameZoom;
            UpdatesAutomatic = settings.UpdatesAutomatic;
            StopAppOnClose = settings.StopAppOnClose;
            SimulatedPhysicalKeyboard = settings.SimulatedPhysicalKeyboard;
            SimulatedPhysicalMouse = settings.SimulatedPhysicalMouse;
            Language = settings.Language;
            AudioEnabled = settings.AudioEnabled;
            ReadCustomQuality(settings);

            SizePercent = settings.CustomSizePercent > 0
                ? settings.CustomSizePercent
                : presets.PercentageAt(settings.SizeIndex);

            await RefreshHotkeysAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }

        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rereads the shortcuts, after a change in the editor.</summary>
    public async Task RefreshHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);

        Hotkeys.Clear();
        foreach (var binding in hotkeys.Bindings)
        {
            Hotkeys.Add(new HotkeyRowViewModel(binding));
        }

        ToggleShortcutText = hotkeys.For(HotkeyAction.ToggleConfigurator)?.DisplayText ?? "Ctrl + P";

        // The actions on the bottom bar recall their shortcut, and
        // follow it when it is changed in the editor.
        RearrangeShortcutText = hotkeys.For(HotkeyAction.Rearrange)?.DisplayText ?? string.Empty;
        TileShortcutText = hotkeys.For(HotkeyAction.Tile)?.DisplayText ?? string.Empty;
        QuestsShortcutText = hotkeys.For(HotkeyAction.Quests)?.DisplayText ?? string.Empty;
        QuitShortcutText = hotkeys.For(HotkeyAction.Quit)?.DisplayText ?? string.Empty;

    }

    /// <summary>
    /// Refreshes what changes on its own: devices and states.
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();
        RefreshConnection();
    }

    private readonly IUsbEnumerationInspector _usb;

    /// <summary>
    /// What the application can say about the connection, in one
    /// sentence.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCableHelp))]
    [NotifyPropertyChangedFor(nameof(ConnectionIsHealthy))]
    [NotifyPropertyChangedFor(nameof(ShowsConnection))]
    private ConnectionVerdict _connection = ConnectionVerdict.NoDevice;

    /// <summary>
    /// False as long as the connection has never been checked.
    ///
    /// Without it, the block started from "no phone connected" and
    /// showed it when the panel opened, even before the first poll,
    /// only to contradict itself a second later. That was not a
    /// state of the connection, it was the absence of a measurement
    /// presented as a finding.
    ///
    /// Showing nothing is the only honest answer to "I do not know
    /// yet": an empty block teaches nothing, a block that is wrong
    /// undoes the trust granted to the ones that follow.
    /// </summary>
    private bool _connectionKnown;

    /// <summary>The sentence itself.</summary>
    public string ConnectionMessage => ConnectionCheck.Describe(Connection);

    /// <summary>
    /// True when the phone answers: the block then stays discreet.
    /// </summary>
    public bool ConnectionIsHealthy => Connection == ConnectionVerdict.Ready;

    /// <summary>
    /// True when the block has something to say: the connection has
    /// been checked, and what was found in it calls for an
    /// explanation.
    /// </summary>
    public bool ShowsConnection => _connectionKnown && ConnectionCheck.NeedsExplaining(Connection);

    /// <summary>True when the cable sheet has something to offer.</summary>
    public bool ShowsCableHelp => ConnectionCheck.NeedsCableHelp(Connection);

    /// <summary>
    /// Rereads the verdict.
    ///
    /// Windows's opinion is asked only when ADB sees nothing: that is
    /// the only case where it brings something, and an enumeration
    /// on every poll would cost without giving anything back.
    /// </summary>
    private void RefreshConnection()
    {
        var states = Instances.DeviceStates;

        var faults = states.Contains(AdbDeviceState.Device)
            ? []
            : _usb.Faults();

        var avant = Connection;
        var connuAvant = _connectionKnown;

        Connection = ConnectionCheck.Of(states, faults, toolsReady: true);
        _connectionKnown = true;

        OnPropertyChanged(nameof(ConnectionMessage));

        // The first check does not necessarily change the verdict,
        // but it changes the right to show it.
        if (!connuAvant)
        {
            OnPropertyChanged(nameof(ShowsConnection));
        }

        if (Connection != avant)
        {
            Serilog.Log.Information(
                "Liaison : {Verdict} ({Appareils} appareil(s) vu(s), {Defauts} défaut(s) USB).",
                Connection,
                states.Count,
                faults.Count);
        }
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    /// <summary>
    /// Writes the settings to a file, to carry them elsewhere.
    ///
    /// The file itself, as it is on disk: what gets read back is
    /// exactly what was written.
    /// </summary>
    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var path = _dialogs.AskWhereToSave(
            SettingsBackup.SuggestedFileName,
            Strings.Get("SettingsFileFilter"),
            Strings.Get("ExportSettings"));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var content = await _settings.ExportAsync().ConfigureAwait(true);

            await File.WriteAllTextAsync(path, content).ConfigureAwait(true);

            _dialogs.ShowInformation(Strings.Format("SettingsExported", path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowWarning(exception.Message);
        }
    }

    /// <summary>
    /// Restores settings written elsewhere, after confirmation.
    ///
    /// The action replaces everything: the accounts, the profiles,
    /// the shortcuts. It is therefore confirmed, and it plainly
    /// refuses a file it cannot read rather than apply half of it.
    /// </summary>
    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = _dialogs.AskWhichFileToRead(
            Strings.Get("SettingsFileFilter"),
            Strings.Get("ImportSettings"));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_dialogs.Confirm(Strings.Get("ImportSettingsConfirm"), Strings.Get("ImportSettings")))
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);

            var verdict = await _settings.ImportAsync(content).ConfigureAwait(true);

            var message = verdict switch
            {
                BackupVerdict.Usable => Strings.Get("SettingsImported"),
                BackupVerdict.TooNew => Strings.Get("SettingsFileTooNew"),
                BackupVerdict.Foreign => Strings.Get("SettingsFileForeign"),
                _ => Strings.Get("SettingsFileUnreadable"),
            };

            if (verdict == BackupVerdict.Usable)
            {
                _dialogs.ShowInformation(message);
            }
            else
            {
                _dialogs.ShowWarning(message);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowWarning(exception.Message);
        }
    }

    /// <summary>
    /// Stacks the windows onto the active one, or onto the first.
    /// </summary>
    [RelayCommand]
    private async Task RearrangeAsync() => await _launcher.StackOnActiveAsync().ConfigureAwait(true);

    /// <summary>
    /// Opens the chosen instance, without touching the others.
    /// </summary>
    [RelayCommand]
    private async Task LaunchInstanceAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.LaunchAsync([row.Instance]).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.ShowBanner(report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null);
    }

    /// <summary>Closes then reopens the instance, game included.</summary>
    [RelayCommand]
    private async Task RestartAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.RestartAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.ShowBanner(report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null);
    }

    [RelayCommand]
    private async Task StopAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _launcher.StopAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private async Task ArrangeAsync(CancellationToken cancellationToken)
    {
        var moved = await _launcher.ArrangeAsync(cancellationToken).ConfigureAwait(true);

        if (moved == 0)
        {
            Instances.ShowBanner(Strings.Get("NothingToRearrange"));
        }
    }

    /// <summary>
    /// Places the windows side by side, the active one on the right.
    /// </summary>
    [RelayCommand]
    private async Task TileAsync()
    {
        var placed = await _launcher.TileAsync().ConfigureAwait(true);

        if (placed == 0)
        {
            Instances.ShowBanner(Strings.Get("NothingToTile"));
        }
    }

    /// <summary>
    /// Gives the report the names the person has chosen, so it can
    /// redact them. Neither account names nor profile names can be
    /// guessed by a pattern, and nothing stops someone from putting
    /// their in-game nickname there.
    /// </summary>
    private void RememberNames(AppSettingsDocument document) =>
        _reporter.Names =
        [
            .. document.LaunchProfiles.Select(p => p.Name),
            .. document.Instances.Select(i => i.UserName),
            .. document.Instances.Select(i => i.CustomName ?? string.Empty),
        ];

    /// <summary>
    /// True when a fault has been recorded without being shown. The
    /// line that announces it can be ignored: it is not a dialog
    /// box, so a fault that repeats blocks nothing.
    /// </summary>
    public bool HasIncident => _reporter.Incidents > 0;

    /// <summary>
    /// Opens the report window, with or without an incident behind
    /// it.
    ///
    /// A problem is not always a crash: a window that will not open,
    /// a missing account, a guide that fails to load can also be
    /// told, and it must be possible to do so without waiting for a
    /// fault.
    /// </summary>
    [RelayCommand]
    private void ReportProblem()
    {
        var title = Strings.Get("ReportProblem");

        new Windows.ProblemWindow(_dialogs, title, _reporter.Compose(title), detail: null)
        {
            Logs = _paths.LogsDirectory,
        }.ShowDialog();
    }

    /// <summary>
    /// Opens the quest tracker, or closes it if it is already there.
    /// </summary>
    [RelayCommand]
    private void Quests() => QuestsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Requested from the tool button. The window is built by the
    /// application, not by this model: it does not need to know
    /// about windows.
    /// </summary>
    public event EventHandler? QuestsRequested;

    /// <summary>Opens today's Almanax.</summary>
    [RelayCommand]
    private void Almanax() => AlmanaxRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Requested from the tool button, same reason.</summary>
    public event EventHandler? AlmanaxRequested;

    /// <summary>Shows what the pending version brings.</summary>
    [RelayCommand]
    private void UpdateNotes() => UpdateNotesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Requested from the update banner, same reason.</summary>
    public event EventHandler? UpdateNotesRequested;

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenFolder(_paths.LogsDirectory);

    private Task SaveAsync(Action<AppSettingsDocument> mutate) =>
        _loading ? Task.CompletedTask : _settings.UpdateAsync(mutate);

    partial void OnGameAnchorChanged(WindowAnchor value)
    {
        // The rearrangement follows the loading guard, like saving
        // does: without it, reading the settings back at startup
        // would rearrange every window and erase their remembered
        // geometry before the user has even touched anything.
        if (_loading)
        {
            return;
        }

        _ = ApplyAnchorAsync(value);
    }

    /// <summary>
    /// Saves the chosen corner, then rearranges the windows.
    ///
    /// The write is awaited: the rearrangement reads the settings
    /// back to get the corner from them, and launching both at once
    /// made it read the old value back. Clicking a cell then did
    /// nothing visible.
    /// </summary>
    private async Task ApplyAnchorAsync(WindowAnchor value)
    {
        await SaveAsync(s => s.GameAnchor = value).ConfigureAwait(true);
        await _launcher.ArrangeAsync().ConfigureAwait(true);
    }

    partial void OnSizePercentChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _ = FollowSliderAsync(value);
        _ = PersistSizeSoonAsync(value);
    }

    /// <summary>
    /// Follows the slider as closely as possible. One move at a
    /// time: the notches arrive faster than the windows can move,
    /// and stacking them would leave the size trailing behind the
    /// slider. Only the last value received during a move is applied
    /// afterward.
    /// </summary>
    private async Task FollowSliderAsync(int percent)
    {
        if (_movingWindows)
        {
            _pendingPercent = percent;
            return;
        }

        _movingWindows = true;

        try
        {
            var next = percent;

            while (true)
            {
                await _launcher.ApplyPercentAsync(next, persist: false).ConfigureAwait(true);

                if (_pendingPercent is not { } queued)
                {
                    break;
                }

                _pendingPercent = null;
                next = queued;
            }
        }
        finally
        {
            _movingWindows = false;
        }
    }

    /// <summary>
    /// Writes the size once the slider has settled. Otherwise every
    /// notch would trigger a full rewrite of the settings file.
    /// </summary>
    private async Task PersistSizeSoonAsync(int percent)
    {
        _persistSize?.Cancel();
        _persistSize?.Dispose();

        var cancellation = new CancellationTokenSource();
        _persistSize = cancellation;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellation.Token).ConfigureAwait(true);

            await _launcher.ApplyPercentAsync(percent, persist: true, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The slider moved again: it is the next value that counts.
        }
    }

    partial void OnQualityChanged(StreamQuality value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetQualityAsync(value));
    }

    partial void OnZoomChanged(GameZoom value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetZoomAsync(value));
    }

    partial void OnCustomHeightChanged(int value) => SaveCustomQuality();

    partial void OnCustomFpsChanged(int value) => SaveCustomQuality();

    partial void OnCustomBitsPerPixelChanged(double value) => SaveCustomQuality();

    partial void OnCustomCodecChanged(string value) => SaveCustomQuality();

    /// <summary>
    /// Restores the four fine values from the settings. Called
    /// under the loading guard, like the quality and the distance.
    /// </summary>
    private void ReadCustomQuality(AppSettingsDocument document)
    {
        var custom = document.CustomQuality.Sanitized();

        CustomHeight = custom.MaximumDisplayHeight;
        CustomFps = custom.MaxFps;
        CustomBitsPerPixel = custom.BitsPerPixel;
        CustomCodec = custom.VideoCodec;
    }

    /// <summary>
    /// Remembers the four fine values, and only reopens the windows
    /// if the custom tier is the one in force.
    ///
    /// Setting them while another tier is checked changes nothing
    /// about the image: reopening in that case would make every
    /// window flicker for nothing.
    /// </summary>
    private void SaveCustomQuality()
    {
        if (_loading)
        {
            return;
        }

        var custom = new CustomQuality
        {
            MaximumDisplayHeight = CustomHeight,
            MaxFps = CustomFps,
            BitsPerPixel = CustomBitsPerPixel,
            VideoCodec = CustomCodec,
        }.Sanitized();

        if (Quality == StreamQuality.Custom)
        {
            _ = ApplyStartupSettingAsync(() => _settings.SetCustomQualityAsync(custom));
            return;
        }

        _ = SaveAsync(settings => settings.CustomQuality = custom);
    }

    partial void OnAudioEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetAudioEnabledAsync(value));
    }

    /// <summary>
    /// Saves a setting that only takes effect when a session opens,
    /// then reopens the windows so it can be seen.
    ///
    /// Without this, changing the quality or the distance showed
    /// nothing: these are scrcpy startup arguments, fixed for the
    /// whole session. The choice to reopen rather than wait for next
    /// time belongs to the user, who did not understand why the
    /// setting seemed dead.
    /// </summary>
    private async Task ApplyStartupSettingAsync(Func<Task> write)
    {
        if (IsReopening)
        {
            return;
        }

        IsReopening = true;

        try
        {
            await write().ConfigureAwait(true);

            var report = await _launcher.ReopenAsync().ConfigureAwait(true);

            if (report.Problems.Count > 0)
            {
                _dialogs.ShowWarning(string.Join(Environment.NewLine, report.Problems));
            }
        }
        finally
        {
            IsReopening = false;
        }
    }


}
