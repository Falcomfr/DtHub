using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Settings;
using DtHub.Core.Windows;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// The configurator: a floating window, above the game windows, that the
/// shortcut shows or hides. Closing it does not quit the application.
/// </summary>
public partial class ConfiguratorWindow : Window
{
    private readonly ConfiguratorViewModel _viewModel;
    private readonly GameLauncher _launcher;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };

    private bool _quitting;

    public ConfiguratorWindow(
        ConfiguratorViewModel viewModel,
        GameLauncher launcher,
        WindowPlacements placements)
    {
        _viewModel = viewModel;
        _launcher = launcher;
        _placements = placements;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        // The polling follows visibility, not loading. It used to never stop:
        // the close button hides instead of closing, so OnClosing returned
        // control before Stop, and Toggle hides without going through there
        // at all. A hidden panel therefore kept polling the phone.
        //
        // What this cost, measured: one tick triggers up to seven calls to
        // adb.exe, at fifty-five milliseconds each. Over a four-hour evening
        // with the panel hidden, that is thousands of launches for a window
        // nobody is looking at, each one waking up the phone's package
        // manager.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is not true)
            {
                _poll.Stop();
                return;
            }

            _poll.Start();

            // Before anything is awaited: what each account is doing, and
            // for how long, is read from state the launcher already holds
            // and costs nothing. Left to the sweep below, the session
            // times would be up to a tick stale on the one frame the
            // reader is actually looking at.
            _viewModel.Instances.RefreshRunningState();

            // And right away, without waiting for the first tick: that one
            // comes three seconds later, and during those three seconds the
            // panel would have nothing to say about the connection, when
            // that is the very first thing one comes here to read.
            await PollSafelyAsync().ConfigureAwait(true);
        };

        _poll.Interval = _viewModel.Instances.PollInterval;
        _poll.Tick += async (_, _) =>
        {
            // The rate follows the quality tier, which can change while
            // running: set once and for all at startup, it kept its original
            // value until the next run, and the setting had no effect at
            // all.
            if (_poll.Interval != _viewModel.Instances.PollInterval)
            {
                _poll.Interval = _viewModel.Instances.PollInterval;
            }

            await PollSafelyAsync().ConfigureAwait(true);
        };

        // The dead time after a connection absorbs the burst: Windows
        // broadcasts the change several times while it enumerates, and it
        // has to be allowed to finish anyway before ADB has anything to see.
        // One second, one single poll.
        _afterDeviceChange.Tick += async (_, _) =>
        {
            _afterDeviceChange.Stop();

            Log.Information("Changement de périphériques signalé par Windows : balayage.");

            await PollSafelyAsync().ConfigureAwait(true);
        };
    }

    private readonly DispatcherTimer _afterDeviceChange = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// A bounded poll, and it is the pace that demands it. The poll only
    /// catches ADB failures; any other would escape an "async void" lambda,
    /// reach the dispatcher's safety net, and open an error window every
    /// three seconds. A lasting failure would then make the application
    /// unusable by its own message. The log keeps it, the next tick
    /// retries.
    /// </summary>
    private async Task PollSafelyAsync()
    {
        try
        {
            await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le balayage du panneau a échoué.");
        }
    }

    /// <summary>
    /// Native handle, kept once and for all. It is read from the hotkey
    /// thread, which is not allowed to query a WPF window.
    /// </summary>
    public nint Handle { get; private set; }

    private readonly WindowPlacements _placements;

    /// <summary>
    /// True when the window has recovered a remembered placement. The
    /// default placement, which moves it away from the game block, then
    /// gives way to it.
    /// </summary>
    public bool Placed { get; private set; }

    /// <summary>
    /// Puts the window back where it was, and says whether it was.
    /// </summary>
    public bool RestorePlacement(AppSettingsDocument document)
    {
        Placed = _placements.Restore(this, WindowPlacements.Configurator, document);

        return Placed;
    }

    /// <summary>Remembers where the window is.</summary>
    public Task SavePlacementAsync() =>
        _placements.SaveAsync(this, WindowPlacements.Configurator);

    /// <summary>
    /// Plug in a phone, and the poll starts right away.
    ///
    /// The periodic poll follows the panel's visibility, for the good
    /// reasons stated above: one tick triggers up to seven launches of
    /// adb.exe, and thousands per evening for a window nobody is looking
    /// at. But with the panel hidden, nothing was watching either: a
    /// plugged-in cable was only seen once the panel came back. Recorded
    /// on a real case, a connection left no log line at all.
    ///
    /// This message costs nothing as long as nothing happens: Windows
    /// broadcasts it, we query nobody. It therefore gives the hidden panel
    /// exactly what it was missing, without taking back what the
    /// measurement had removed.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && (int)wParam == DbtDevNodesChanged)
        {
            _afterDeviceChange.Stop();
            _afterDeviceChange.Start();
        }

        return 0;
    }

    /// <summary>Shows or hides the window, depending on its state.</summary>
    public void Toggle()
    {
        if (_shown)
        {
            Vanish();
            return;
        }

        Reveal();
    }

    /// <summary>
    /// Whether the panel is meant to be on screen.
    ///
    /// **Intent, not state.** During the hundred and twenty
    /// milliseconds of a vanish the window is still visible, so a second
    /// Ctrl+P read from <see cref="UIElement.IsVisible" /> would start a
    /// second vanish instead of bringing the panel back.
    /// </summary>
    private bool _shown;

    /// <summary>
    /// Shows the panel, or brings back one that is on its way out.
    /// </summary>
    public void Reveal()
    {
        _shown = true;

        // Stopping raises the vanish's completion, which is why that
        // handler asks whether the panel is still meant to be away:
        // without the question it would hide the window we are in the
        // middle of asking for.
        Vanishing.Stop(this);

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        Activate();
        ((Storyboard)Resources["Reveal"]).Begin(this, true);
    }

    /// <summary>
    /// Takes the panel away, and only hides it once it has gone.
    ///
    /// **Animate first, hide last.** Visibility is what starts and stops
    /// the device sweep, so hiding before the animation would leave
    /// nothing to look at while it played, and the poll would keep
    /// running a hundred and twenty milliseconds longer either way,
    /// which against a three second interval is nothing.
    /// </summary>
    public void Vanish()
    {
        _shown = false;
        Vanishing.Begin(this, true);
    }

    /// <summary>
    /// Hides the panel at once, with no animation.
    ///
    /// For the startup path that shows the window only to make it lay
    /// itself out, and then takes it back before anyone has seen it:
    /// that one must not be visible at all, let alone fade.
    /// </summary>
    public void HideNow()
    {
        _shown = false;
        Vanishing.Stop(this);
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Hide();
    }

    /// <summary>
    /// Shows the window invisibly, so that it lays itself out and its
    /// scaling can be measured, without anything appearing.
    /// </summary>
    public void PrepareHidden()
    {
        Opacity = 0;
        Show();
    }

    /// <summary>
    /// The storyboard that takes the panel away, with its completion
    /// wired once.
    ///
    /// The completion is gated twice. Stopping a storyboard raises it
    /// too, so without the first question it would hide the window that
    /// had just been asked for; and a quit in mid-animation would call
    /// Hide on a window already closed.
    /// </summary>
    private Storyboard Vanishing
    {
        get
        {
            if (_vanishing is null)
            {
                _vanishing = (Storyboard)Resources["Vanish"];

                _vanishing.Completed += (_, _) =>
                {
                    if (!_shown && !_quitting)
                    {
                        Hide();
                    }
                };
            }

            return _vanishing;
        }
    }

    private Storyboard? _vanishing;

    /// <summary>
    /// Places the window in a free corner, opposite the game block, so
    /// that it does not cover the game windows.
    /// </summary>
    public void PlaceAwayFrom(WindowAnchor gameAnchor)
    {
        // A remembered placement wins: it comes from a deliberate move,
        // whereas this one is only a reasonable default.
        if (Placed)
        {
            return;
        }

        var work = _launcher.WorkArea();
        if (work is not { } area)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * scale.DpiScaleX);
        var height = (int)Math.Round(Height * scale.DpiScaleY);

        var rect = WindowLayoutCalculator.Place(area, width, height, WindowAnchors.Opposite(gameAnchor));

        // A small margin keeps the window from sticking to the screen edge.
        const int margin = 12;

        Left = (rect.X + (rect.X > area.X ? -margin : margin)) / scale.DpiScaleX;
        Top = (rect.Y + margin) / scale.DpiScaleY;
    }

    /// <summary>
    /// Quits the application. The session state is saved beforehand, and
    /// the wait is necessary: otherwise the shutdown would race against
    /// the write.
    /// </summary>
    private async void OnQuit(object sender, RoutedEventArgs e)
    {
        _quitting = true;

        await ((App)Application.Current).RequestQuitAsync().ConfigureAwait(true);
    }

    private void OnHide(object sender, RoutedEventArgs e) => Vanish();

    private void OnSleepHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<SleepHelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>
    /// What to do when the picture comes through but nothing responds. The
    /// symptom is silent by nature: no error is raised, and without this
    /// door the person has nothing to hold on to.
    /// </summary>
    private void OnInputHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<InputHelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>Opens the hotkey editor, then rereads what changed.</summary>
    private async void OnEditHotkeys(object sender, RoutedEventArgs e)
    {
        var editor = AppHost.Services.GetRequiredService<HotkeyEditorWindow>();
        editor.Owner = this;
        editor.ShowDialog();

        await _viewModel.RefreshHotkeysAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the panel on the devices tab. Used on first launch, where
    /// it is the only place that has anything to say.
    /// </summary>
    public void ShowDevices()
    {
        Reveal();

        TabDevices.IsChecked = true;
    }

    /// <summary>
    /// Opens the pairing window, as the button would.
    ///
    /// Deferred: called during startup, it would block what follows on its
    /// modal loop, and quest tracking as well as the update would wait
    /// until pairing a phone was finished.
    /// </summary>
    public void BeginPairing() =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => OnAddDevice(this, new RoutedEventArgs()));

    /// <summary>Opens the add window, then refreshes the list.</summary>
    private async void OnAddDevice(object sender, RoutedEventArgs e)
    {
        var dialog = AppHost.Services.GetRequiredService<AddDeviceWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();

        await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Message that Windows broadcasts when the device tree changes. It
    /// reaches top-level windows without prior registration, and a hidden
    /// window receives it just like the others.
    /// </summary>
    private const int WmDeviceChange = 0x0219;

    private const int DbtDevNodesChanged = 0x0007;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The close button hides, it does not quit: the game keeps running.
        if (!_quitting)
        {
            e.Cancel = true;
            Vanish();
            return;
        }

        // Stop the clock before the base call: a vanish still in flight
        // would otherwise raise its completion against a closed window.
        _vanishing?.Stop(this);

        _poll.Stop();
        base.OnClosing(e);
    }
}
