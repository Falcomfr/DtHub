using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Localization;
using DtHub.Core.Settings;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Adb;

using Serilog;

namespace DtHub.Mac.Views;

/// <summary>
/// The configurator, the PC's ConfiguratorWindow
/// (src/DtHub.App/Windows/ConfiguratorWindow.xaml.cs): a floating window
/// above the game windows. Closing it does not quit the application: the
/// cross hides it, and the Dock icon brings it back, as Ctrl+P and the
/// tray do on the PC.
/// </summary>
public partial class ConfiguratorWindow : Window
{
    // The PC's fade and slide, in milliseconds and pixels.
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(120);
    private const double Slide = 6;

    private readonly ConfiguratorViewModel _viewModel = null!;
    private readonly GameLauncher _launcher = null!;
    private readonly WindowPlacements _placements = null!;
    private readonly IDialogService _dialogs = null!;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _afterDeviceChange = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _quitting;

    /// <summary>
    /// Whether the panel is meant to be on screen: intent, not state.
    /// During the fade out the window is still visible, and a second
    /// request read from IsVisible would start a second vanish instead of
    /// bringing the panel back.
    /// </summary>
    private bool _shown;

    private int _vanishing;

    /// <summary>For the XAML previewer only.</summary>
    public ConfiguratorWindow() => AvaloniaXamlLoader.Load(this);

    public ConfiguratorWindow(
        ConfiguratorViewModel viewModel,
        GameLauncher launcher,
        WindowPlacements placements,
        AdbDeviceWatch adbWatch,
        IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(adbWatch);

        _viewModel = viewModel;
        _launcher = launcher;
        _placements = placements;
        _dialogs = dialogs;

        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;

        Opened += async (_, _) =>
        {
            NativeHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        };

        _poll.Interval = _viewModel.Instances.PollInterval;
        _poll.Tick += async (_, _) =>
        {
            // The rate follows the quality tier, which can change while
            // running.
            if (_poll.Interval != _viewModel.Instances.PollInterval)
            {
                _poll.Interval = _viewModel.Instances.PollInterval;
            }

            await PollSafelyAsync().ConfigureAwait(true);
        };

        // The dead time after a change absorbs the burst; a sweep under
        // way read the devices before the change, so the timer asks again.
        _afterDeviceChange.Tick += async (_, _) =>
        {
            if (_viewModel.Instances.IsBusy)
            {
                return;
            }

            _afterDeviceChange.Stop();

            Log.Information("Changement de périphériques signalé par Windows : balayage.");

            await PollSafelyAsync().ConfigureAwait(true);
        };

        // What the PC learns from Windows' device broadcast, the Mac learns
        // from adb alone: a phone plugged, unplugged, joining over Wi-Fi.
        adbWatch.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            _afterDeviceChange.Stop();
            _afterDeviceChange.Start();
        });

        // For the life of the application, like this window.
        _ = adbWatch.RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Native handle, kept once and for all: it is read from the watcher's
    /// thread, which may not query the window.
    /// </summary>
    public nint NativeHandle { get; private set; }

    /// <summary>True when the window recovered a remembered placement.</summary>
    public bool Placed { get; private set; }

    private async Task OnVisibilityAsync(bool visible)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (!visible)
        {
            _poll.Stop();
            return;
        }

        _poll.Start();

        // What each account is doing is read from state the launcher
        // already holds, before anything is awaited.
        _viewModel.Instances.RefreshRunningState();

        // And right away, without waiting three seconds for the first tick.
        await PollSafelyAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// A bounded poll: any failure but adb's would escape an async void
    /// handler every three seconds. The log keeps it, the next tick
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

    /// <summary>Puts the window back where it was, and says whether it was.</summary>
    public bool RestorePlacement(AppSettingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // A maximized placement from an old PC settings file would reopen
        // the panel over the whole screen: its default place is better.
        if (document.WindowPlacements.TryGetValue(WindowPlacements.Configurator, out var saved)
            && saved.Maximized)
        {
            Placed = false;
            return Placed;
        }

        Placed = _placements.Restore(this, WindowPlacements.Configurator, document);
        return Placed;
    }

    /// <summary>Remembers where the window is.</summary>
    public Task SavePlacementAsync() => _placements.SaveAsync(this, WindowPlacements.Configurator);

    /// <summary>
    /// Places the window in a free corner, opposite the game block, so that
    /// it does not cover the game windows. A remembered placement wins.
    /// </summary>
    public void PlaceAwayFrom(WindowAnchor gameAnchor)
    {
        if (Placed || (Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen)
        {
            return;
        }

        // Avalonia gives the screen and the position in pixels: the
        // window's size is turned into pixels too.
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);

        var rect = WindowLayoutCalculator.Place(
            new ScreenRect(area.X, area.Y, area.Width, area.Height),
            width,
            height,
            WindowAnchors.Opposite(gameAnchor));

        // A small margin keeps the window from sticking to the screen edge.
        var margin = (int)Math.Round(12 * scale);

        Position = new PixelPoint(rect.X + (rect.X > area.X ? -margin : margin), rect.Y + margin);
    }

    /// <summary>Shows the window if hidden, hides it otherwise.</summary>
    public void Toggle()
    {
        if (_shown && WindowState != WindowState.Minimized)
        {
            Vanish();
            return;
        }

        Reveal();
    }

    /// <summary>Shows the panel, or brings back one on its way out.</summary>
    public void Reveal()
    {
        _shown = true;

        // A vanish in flight stops counting: its end checks this number.
        _vanishing++;

        if (!IsVisible)
        {
            SetChassis(opacity: 0, offset: Slide);
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // After the first frame, so the transition has a start to leave.
        Dispatcher.UIThread.Post(() => SetChassis(opacity: 1, offset: 0), DispatcherPriority.Render);
    }

    /// <summary>
    /// Takes the panel away, and only hides it once it has gone: hiding
    /// first would stop the sweep with nothing left to look at.
    /// </summary>
    public void Vanish()
    {
        _shown = false;
        var ticket = ++_vanishing;

        SetChassis(opacity: 0, offset: Slide);

        DispatcherTimer.RunOnce(
            () =>
            {
                // Gated twice: a reveal meanwhile wants the window, and a
                // quit meanwhile has closed it.
                if (ticket == _vanishing && !_shown && !_quitting)
                {
                    Hide();
                }
            },
            Fade);
    }

    /// <summary>Hides the panel at once, with no fade, for the startup path.</summary>
    public void HideNow()
    {
        _shown = false;
        _vanishing++;
        Hide();
    }

    /// <summary>Shows the window invisibly, so that it lays itself out.</summary>
    public void PrepareHidden()
    {
        SetChassis(opacity: 0, offset: Slide);
        Show();
    }

    /// <summary>Opens the panel on the devices tab, as on first launch.</summary>
    public void ShowDevices()
    {
        Reveal();

        if (this.FindControl<RadioButton>("TabDevices") is { } tab)
        {
            tab.IsChecked = true;
        }
    }

    private void SetChassis(double opacity, double offset)
    {
        if (this.FindControl<Border>("Chassis") is { } chassis)
        {
            chassis.Opacity = opacity;
            chassis.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateY({offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");
        }
    }

    /// <summary>Marks the quit as under way, so closing stops hiding instead.</summary>
    public void BeginQuit() => _quitting = true;

    /// <summary>
    /// Quits the application. The session state is saved first, and the
    /// wait matters: otherwise the shutdown races the write.
    /// </summary>
    private async void OnQuit(object? sender, RoutedEventArgs e)
    {
        _quitting = true;

        await MacApp.RequestQuitAsync().ConfigureAwait(true);
    }

    private void OnHide(object? sender, RoutedEventArgs e) => Vanish();

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// Visibility starts and stops the sweep.
    /// Minimized, the panel stays visible to the toolkit: its sweep would
    /// keep running for a window nobody sees. It stops as when hidden.
    /// </summary>
    protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        base.OnPropertyChanged(change);

        if (_viewModel is null)
        {
            return;
        }

        // The polling follows visibility: one tick launches up to seven adb
        // calls, and a hidden panel kept them coming all evening on the PC.
        if (change.Property == IsVisibleProperty)
        {
            await OnVisibilityAsync(IsVisible).ConfigureAwait(true);
            return;
        }

        if (change.Property != WindowStateProperty)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            _poll.Stop();
            return;
        }

        if (IsVisible && !_poll.IsEnabled)
        {
            _poll.Start();
            await PollSafelyAsync().ConfigureAwait(true);
        }
    }

    private void OnHeightDrag(object? sender, VectorEventArgs e) =>
        Height = Math.Clamp(Bounds.Height + e.Vector.Y, MinHeight, MaxHeight);

    /// <summary>
    /// The density applies as soon as it is picked, or typed and confirmed
    /// with Enter: its binding waits for the focus to leave, so a value
    /// being typed is not rewritten under the cursor.
    /// </summary>
    private void OnDensityPicked(object? sender, EventArgs e)
    {
        if (sender is ComboBox box)
        {
            BindingOperations.GetBindingExpressionBase(box, ComboBox.TextProperty)?.UpdateSource();
        }
    }

    private void OnDensityKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnDensityPicked(sender, e);
        }
    }

    /// <summary>The command runs on its own: the menu only has to close.</summary>
    private void OnArrangeChosen(object? sender, RoutedEventArgs e) =>
        this.FindControl<Button>("ArrangeMenuKey")?.Flyout?.Hide();

    /// <summary>What is installed may have changed since the last sweep.</summary>
    private async void OnPartnerMenuOpened(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.RefreshPartnerTilesAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _viewModel.Instances.ShowBanner(exception.Message);
        }
    }

    /// <summary>Closed, the menu forgets which tool was choosing its phone.</summary>
    private void OnPartnerMenuClosed(object? sender, EventArgs e) => _viewModel.PickingTile = null;

    /// <summary>A phone chosen in the tools menu: the menu closes.</summary>
    private void OnPartnerChosen(object? sender, RoutedEventArgs e) => ClosePartnerMenu();

    /// <summary>A tile: its action runs and the menu closes, or its phones show.</summary>
    private void OnToolTile(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PartnerTileViewModel tile && _viewModel.ChooseTool(tile))
        {
            ClosePartnerMenu();
        }
    }

    private void ClosePartnerMenu() => this.FindControl<Button>("PartnerToggle")?.Flyout?.Hide();

    /// <summary>Opens the hotkey editor on the PC, then rereads what changed.</summary>
    private void OnEditHotkeys(object? sender, RoutedEventArgs e) =>
        // shortcut: HotkeyEditorWindow is not on the Mac yet (nor are the
        // global shortcuts it would edit, see MacHotkeyRegistrar).
        _dialogs.ShowInformation(Strings.Get("MacNotYet"));

    /// <summary>Opens the pairing window on the PC, then sweeps again.</summary>
    private void OnAddDevice(object? sender, RoutedEventArgs e) =>
        // shortcut: AddDeviceWindow is not on the Mac yet; Wi-Fi pairing
        // goes through "adb pair" and "adb connect" in the Terminal.
        _dialogs.ShowInformation(Strings.Get("MacNotYet"));

    /// <summary>The header is the drag handle, the window having no title bar.</summary>
    private void OnHeaderDrag(object? sender, PointerPressedEventArgs e)
    {
        // A press on the header's own buttons is theirs.
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // The close hides, it does not quit: the game keeps running.
        if (!_quitting && !MacApp.IsShuttingDown)
        {
            e.Cancel = true;
            Vanish();
            return;
        }

        _poll.Stop();
        _afterDeviceChange.Stop();
        base.OnClosing(e);
    }
}
