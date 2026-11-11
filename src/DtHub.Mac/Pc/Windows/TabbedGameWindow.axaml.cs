// Instance members where the PC window has them: the shared launcher calls
// them on an instance.
#pragma warning disable CA1822

using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Windows;
using DtHub.Mac.Platform;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's tabbed frame (src/DtHub.App/Windows/TabbedGameWindow.xaml.cs),
/// by its contract with GameLauncher, done the way macOS allows.
///
/// On the PC each game window is re-parented into the frame. macOS forbids
/// housing another application's window, so here the frame is only its tab
/// strip, and the game windows of its tabs are stacked exactly below it, at
/// the same place and size: the selected one is brought to the front, the
/// others wait underneath. Moving the strip takes them along, and moving or
/// resizing the selected game by its own title bar brings the strip and the
/// others along. What the launcher calls the frame's rectangle (its saved
/// place, its size, its screen) is the strip plus the game area below it:
/// <see cref="MacWindowController.GetWindowRect"/> answers that for the
/// frame's handle.
/// </summary>
public sealed partial class TabbedGameWindow : Window
{
    /// <summary>The strip's height, in points, as laid out in the AXAML.</summary>
    private const int StripHeight = 34;

    /// <summary>The PC frame's MinWidth and MinHeight, for the whole frame.</summary>
    private const int MinFrameWidth = 480;

    private const int MinFrameHeight = 320;

    /// <summary>The distance a pressed tab travels before it is dragged.</summary>
    private const double DragThreshold = 4;

    private readonly IWindowController _windows;
    private readonly MacWindowController? _mac;

    /// <summary>Where each docked window stood before the frame took it.</summary>
    private readonly Dictionary<nint, ScreenRect> _before = [];

    /// <summary>Brings the strip back to the game shown, if macOS refused it.</summary>
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Follows the selected game when the player moves or resizes it.</summary>
    private readonly DispatcherTimer _follow = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private ScreenRect _outer;
    private ScreenRect _beforeFullscreen;
    private ScreenRect _screen;

    /// <summary>Where the selected game was last placed or seen.</summary>
    private ScreenRect? _gameAt;

    /// <summary>A size the player gave the game, waiting one tick to hold still.</summary>
    private ScreenRect? _resizing;

    private nint _handle;
    private bool _shown;
    private bool _closing;
    private bool _following;
    private bool _adopting;
    private long _stripMovedAt;
    private string? _reminder;

    private GameTabViewModel? _pressed;
    private GameTabViewModel? _dragged;
    private Point _origin;

    /// <summary>For Avalonia's loader and previewer only.</summary>
    public TabbedGameWindow()
        : this(new MacWindowController())
    {
    }

    public TabbedGameWindow(IWindowController windows)
    {
        _windows = windows;
        _mac = windows as MacWindowController;
        InitializeComponent();
        Tabs.ItemsSource = Items;
        Items.CollectionChanged += (_, _) =>
        {
            Retitle();
            UpdateFollowing();
        };
        Width = 640;
        Height = StripHeight;
        Title = DtHub.Core.ProductInfo.Name;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Waiting.CloseRequested += (_, _) => OnWaitingClose();

        // Dragging the strip moves the window; the games follow once it
        // has moved.
        PositionChanged += (_, _) => FollowStrip();
        Activated += (_, _) => GiveKeyboard(attempts: 8);

        // Dragging a tab, on the model of the accounts list. Tunnelled, so
        // the tab's own press and release are seen before it takes them.
        Tabs.AddHandler(PointerPressedEvent, OnTabsPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tabs.AddHandler(PointerMovedEvent, OnTabsMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tabs.AddHandler(PointerReleasedEvent, OnTabsReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        Tabs.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);

        // The strip follows the game actually in front: macOS may refuse to
        // bring the selected one forward, and the player may click straight
        // on a game below.
        _watch.Tick += (_, _) => AdoptFront();
        _follow.Tick += (_, _) => FollowGame();
        Opened += (_, _) =>
        {
            _watch.Start();

            // A player in a game clicks a tab: that click must pick the tab,
            // not only bring DT Hub forward.
            if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is { Handle: not 0 } native)
            {
                MacNative.dthub_accept_first_mouse(native.Handle);
            }
        };
        Closed += (_, _) =>
        {
            _watch.Stop();
            _follow.Stop();
            _mac?.SetHostRect(Handle, null);
        };
    }

    public ObservableCollection<GameTabViewModel> Items { get; } = [];

    /// <summary>Raised when the player has reordered the tabs by dragging one.</summary>
    public event EventHandler<(string Moved, string Onto, bool Before)>? Reordered;

    /// <summary>
    /// Raised when the frame closes, with the accounts it was housing, or
    /// when a tab's cross closes its account alone.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? CloseRequested;

    /// <summary>
    /// The strip's CGWindowID, kept once known: the launcher reads it from
    /// other threads, as the PC keeps its HWND for the same reason.
    /// </summary>
    public nint Handle
    {
        get
        {
            // libDtHubMac is macOS's: on Linux the frame stays inert rather than throw on every tick.
            if (_handle == 0
                && OperatingSystem.IsMacOS()
                && TryGetPlatformHandle() is { Handle: not 0 } native
                && MacNative.dthub_window_number(native.Handle) is > 0 and var number)
            {
                _handle = (nint)number;
            }

            return _handle;
        }
    }

    public new System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Threading.Dispatcher.UiThread;

    /// <summary>
    /// The shortcut reminder free windows carry in their title. The strip
    /// has no title bar, so it shows it at its right end.
    /// </summary>
    public string? ShortcutReminder
    {
        get => _reminder;
        set
        {
            _reminder = value;
            Retitle();
        }
    }

    public double? SelectedAspect => Selected is { Aspect: > 0 } tab ? tab.Aspect : null;

    /// <summary>
    /// What the frame adds around a game's picture: the strip, and the
    /// game window's own title bar below it.
    /// </summary>
    public (int Width, int Height)? Chassis => (0, Chrome);

    public bool IsFullscreen { get; private set; }

    public double Left => _outer.X;

    public double Top => _outer.Y;

    private static int Chrome => StripHeight + MacWindowController.TitleBarHeight;

    private GameTabViewModel? Selected => Items.FirstOrDefault(t => t.IsSelected);

    /// <summary>
    /// The frame only shows itself with its first game, where that game
    /// is or where the saved placement puts it: shown at once, the strip
    /// flashed at the default place and size first.
    /// </summary>
    public new void Show()
    {
    }

    /// <summary>A frame never shown is shown to be closed, so it closes like any other.</summary>
    public new void Close()
    {
        EnsureShown(null);
        base.Close();
    }

    /// <summary>
    /// The saved placement, the whole frame's: <see cref="WindowPlacements"/>
    /// hands it here rather than to the strip's window. The first game to
    /// arrive then keeps it instead of placing the frame around itself.
    /// </summary>
    public bool RestorePlacement(WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (!placement.IsSized)
        {
            return false;
        }

        var rect = AtLeastMinimum(new ScreenRect(
            placement.Left, placement.Top, placement.Right - placement.Left, placement.Bottom - placement.Top));
        var monitors = _windows.GetMonitors();

        // A screen since unplugged: back onto the main one, as Windows does.
        if (monitors.Count > 0 && !monitors.Any(m => Overlaps(m.Bounds, rect)))
        {
            rect = rect with { X = monitors[0].WorkArea.X, Y = monitors[0].WorkArea.Y };
        }

        Outer = KeepInside(rect, WorkArea(rect));
        PlaceStrip();
        PlaceGames();
        return true;
    }

    /// <summary>
    /// Takes a game window into a tab, or back into its waiting tab. Has no
    /// effect if it is already there.
    /// </summary>
    public bool Attach(
        string key, string title, string? iconPath, nint window, double aspect, string? colourBrushKey)
    {
        var existing = Find(key);

        if (window == 0 || existing is { Window: not 0 })
        {
            return false;
        }

        var at = _windows.GetWindowRect(window);
        EnsureShown(at);

        if (!_windows.Dock(window, Handle))
        {
            return false;
        }

        if (at is { Width: > 0 } before)
        {
            _before[window] = before;
        }

        if (existing is not null)
        {
            existing.Window = window;
            SetWaiting(existing, false);
            Select(Selected ?? existing);
            SettleLater();
            return true;
        }

        var tab = new GameTabViewModel(key, title, iconPath, window, aspect, colourBrushKey);
        tab.PropertyChanged += OnTabChanged;
        Items.Add(tab);

        Select(Items.Count == 1 ? tab : Selected ?? tab);
        SettleLater();
        return true;
    }

    public bool Detach(string key, bool reveal = true)
    {
        if (Find(key) is not { } tab)
        {
            return false;
        }

        tab.PropertyChanged -= OnTabChanged;
        Items.Remove(tab);

        ScreenRect? back = null;

        if (tab.Window != 0)
        {
            _ = _windows.Undock(tab.Window);
            back = _before.Remove(tab.Window, out var rect) ? rect : null;
        }

        if (tab.IsSelected && Items.Count > 0 && !_closing)
        {
            Select(Items[0]);
        }

        // A revealed window goes back where it stood before it became a
        // tab, as the PC's Undock gives it back its geometry, and in front:
        // it is a window of its own again.
        if (reveal && tab.Window != 0)
        {
            if (back is { } previous)
            {
                _windows.MoveWindow(tab.Window, previous);
            }

            _windows.Raise(tab.Window);
        }

        CloseIfEmpty();
        return true;
    }

    public void DetachAll(bool reveal = true)
    {
        foreach (var key in Items.Select(t => t.Key).ToList())
        {
            Detach(key, reveal);
        }
    }

    public void KeepOnly(IReadOnlyCollection<string> keys, IReadOnlySet<string> waiting)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(waiting);

        foreach (var tab in Items.ToList())
        {
            var alive = keys.Contains(tab.Key);

            if (!alive && !waiting.Contains(tab.Key))
            {
                Detach(tab.Key);
                continue;
            }

            if (!alive && tab.Window != 0)
            {
                _ = _windows.Undock(tab.Window);
                _ = _before.Remove(tab.Window);
                tab.Window = 0;
            }

            SetWaiting(tab, waiting.Contains(tab.Key) || tab.Window == 0);
        }
    }

    public bool Holds(string key) => Find(key) is not null;

    /// <summary>The PC's iPhone tabs only: no iPhone is wired on the Mac (D227).</summary>
    public event EventHandler<string>? TabLeft
    {
        add { }
        remove { }
    }

    /// <summary>The PC's iPhone tabs only: no iPhone is wired on the Mac (D227).</summary>
    public void UpdateAspect(string key, double aspect)
    {
    }

    /// <summary>The PC's iPhone tabs only: no iPhone is wired on the Mac (D227).</summary>
    public void Explain(string key, string? text)
    {
    }

    public void Show(string key)
    {
        if (Find(key) is { } tab)
        {
            Select(tab);
        }
    }

    public void ShowAccountKeys(Func<string, string?> keyOf)
    {
        ArgumentNullException.ThrowIfNull(keyOf);

        foreach (var tab in Items)
        {
            tab.AccountKey = keyOf(tab.Key);
        }
    }

    public void Reorder(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var tabbed = keys
            .Where(k => Items.Any(t => string.Equals(t.Key, k, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        for (var wanted = 0; wanted < tabbed.Count; wanted++)
        {
            var present = Items.ToList().FindIndex(t => string.Equals(t.Key, tabbed[wanted], StringComparison.Ordinal));

            if (present != wanted)
            {
                Items.Move(present, wanted);
            }
        }
    }

    public void Rename(string key, string title)
    {
        if (Find(key) is { } tab)
        {
            tab.Title = title;
            Retitle();
        }
    }

    public void Restate(string key, string? status)
    {
        if (Find(key) is { } tab && tab.Status != status)
        {
            tab.Status = status;
            Retitle();
        }
    }

    public void Recolour(string key, string? colourBrushKey)
    {
        if (Find(key) is { } tab)
        {
            tab.ColourBrushKey = colourBrushKey;
        }
    }

    public bool Cycle(bool forward)
    {
        if (Items.Count < 2)
        {
            return false;
        }

        var current = Selected ?? Items[0];
        var step = forward ? 1 : -1;
        var wanted = (((Items.IndexOf(current) + step) % Items.Count) + Items.Count) % Items.Count;
        Items[wanted].IsSelected = true;
        return true;
    }

    /// <summary>
    /// The strip and the games below it fill this rectangle, then take the
    /// selected game's shape, as the PC's ApplyRect settles.
    /// </summary>
    public void ApplyRect(ScreenRect outer)
    {
        if (IsFullscreen || outer.Width <= 0 || outer.Height <= StripHeight)
        {
            return;
        }

        Outer = AtLeastMinimum(outer);

        if (Selected is { } tab)
        {
            Fit(tab);
        }

        PlaceStrip();
        PlaceGames();
    }

    /// <summary>
    /// Full screen: the strip steps aside and the selected game fills the
    /// screen; leaving it puts everything back where it was. A tab waiting
    /// for its phone shows its waiting view over the whole screen instead.
    /// </summary>
    public void SetFullscreen(bool on, ScreenRect target)
    {
        if (on == IsFullscreen)
        {
            return;
        }

        if (on)
        {
            _beforeFullscreen = _outer;
            _screen = target;
            IsFullscreen = true;
            Strip.IsVisible = false;
            _outer = new ScreenRect(target.X, target.Y - StripHeight, target.Width, target.Height + StripHeight);
            PublishHost();
            PlaceGames();
            ShowWaitingView();
        }
        else
        {
            IsFullscreen = false;
            Strip.IsVisible = true;

            if (!IsVisible)
            {
                base.Show();
            }

            var back = target.Width > 0 && target.Height > 0 ? target : _beforeFullscreen;
            ApplyRect(back.Width > 0 ? back : _outer);
            ShowWaitingView();
        }
    }

    /// <summary>
    /// In full screen the strip is hidden: activating it would show it over
    /// the game, so the selected game is brought forward instead.
    /// </summary>
    public new bool Activate()
    {
        EnsureShown(null);

        if (IsFullscreen && Selected is { ShowsGame: true } tab)
        {
            BringForward(tab.Window, activate: true);
            return true;
        }

        base.Activate();
        return true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _closing = true;
        List<string> housed = [.. Items.Select(t => t.Key)];
        DetachAll(reveal: false);
        base.OnClosing(e);
        CloseRequested?.Invoke(this, housed);
    }

    private static bool Overlaps(ScreenRect a, ScreenRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static bool Near(ScreenRect a, ScreenRect b) =>
        Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1
        && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

    private static ScreenRect AtLeastMinimum(ScreenRect rect) =>
        rect with { Width = Math.Max(MinFrameWidth, rect.Width), Height = Math.Max(MinFrameHeight, rect.Height) };

    /// <summary>The rectangle moved, not resized, to stay inside the work area where it can.</summary>
    private static ScreenRect KeepInside(ScreenRect rect, ScreenRect work)
    {
        if (work.Width <= 0 || work.Height <= 0)
        {
            return rect;
        }

        var x = Math.Max(work.X, Math.Min(rect.X, work.X + work.Width - rect.Width));
        var y = Math.Max(work.Y, Math.Min(rect.Y, work.Y + work.Height - rect.Height));
        return rect with { X = x, Y = y };
    }

    /// <summary>
    /// The frame's rectangle, told to the window controller each time it
    /// changes so the launcher reads the whole frame for its handle.
    /// </summary>
    private ScreenRect Outer
    {
        set
        {
            _outer = value;
            PublishHost();
        }
    }

    private void PublishHost()
    {
        if (Handle != 0)
        {
            _mac?.SetHostRect(Handle, IsFullscreen ? _screen : _outer);
        }
    }

    /// <summary>
    /// Shows the strip the first time, already at its place: right above
    /// the first game when no saved placement gave it one.
    /// </summary>
    private void EnsureShown(ScreenRect? around)
    {
        if (_shown)
        {
            return;
        }

        if (_outer.Width <= 0 && around is { Width: > 0 } at)
        {
            Outer = AtLeastMinimum(new ScreenRect(at.X, at.Y - StripHeight, at.Width, at.Height + StripHeight));
        }

        if (_outer.Width > 0)
        {
            Width = _outer.Width;
        }

        // Before showing it when its window can already be reached, and
        // again once shown: either way it is in place before it is drawn.
        PlaceStrip();
        _shown = true;
        base.Show();
        PublishHost();
        PlaceStrip();
    }

    private GameTabViewModel? Find(string key) =>
        Items.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameTabViewModel.IsSelected)
            && sender is GameTabViewModel { IsSelected: true } tab)
        {
            Select(tab);
        }
    }

    private void Select(GameTabViewModel tab)
    {
        foreach (var other in Items)
        {
            other.IsSelected = ReferenceEquals(other, tab);
        }

        _gameAt = null;
        _resizing = null;

        Fit(tab);
        PlaceStrip();
        PlaceGames();
        Retitle();
        ShowWaitingView();
        UpdateFollowing();

        if (tab.ShowsGame && !_adopting)
        {
            BringForward(tab.Window, activate: true);
        }
    }

    private void SetWaiting(GameTabViewModel tab, bool waits)
    {
        if (tab.IsWaiting != waits)
        {
            tab.IsWaiting = waits;

            if (tab.IsSelected)
            {
                Select(tab);
            }
        }
    }

    private bool SelectedWaits => Selected is { } tab && !tab.ShowsGame;

    /// <summary>
    /// The waiting view, when the selected tab has no game to show: the
    /// strip's window grows over the game area (or the whole screen in full
    /// screen) and comes in front of the games stacked there.
    /// </summary>
    private void ShowWaitingView()
    {
        var waits = SelectedWaits;
        Waiting.IsVisible = waits;

        if (!_shown || _closing)
        {
            return;
        }

        if (IsFullscreen)
        {
            if (!waits)
            {
                Hide();
                return;
            }

            if (!IsVisible)
            {
                base.Show();
            }

            MoveOwn(_screen);
        }

        if (waits)
        {
            RaiseOwn();
        }
    }

    /// <summary>
    /// Gives the frame the selected tab's shape (PC Fit): the width stays,
    /// the height follows the picture; with no room left in height, the
    /// width gives way instead. Kept inside the work area.
    /// </summary>
    private void Fit(GameTabViewModel tab)
    {
        if (IsFullscreen || tab.Aspect <= 0 || _outer.Width <= 0)
        {
            return;
        }

        var work = WorkArea(_outer);
        var wanted = (int)Math.Round(_outer.Width / tab.Aspect) + Chrome;
        var rect = _outer;

        if (Math.Abs(wanted - _outer.Height) > 2)
        {
            rect = work.Height <= 0 || wanted <= work.Height
                ? rect with { Height = wanted }
                : rect with
                {
                    Height = work.Height,
                    Width = Math.Max(MinFrameWidth, (int)Math.Round((work.Height - Chrome) * tab.Aspect)),
                };
        }

        Outer = KeepInside(AtLeastMinimum(rect), work);
    }

    /// <summary>The work area of the screen the rectangle is mostly on.</summary>
    private ScreenRect WorkArea(ScreenRect rect)
    {
        var monitors = _windows.GetMonitors();

        return monitors.Count == 0
            ? default
            : WindowLayoutCalculator.ChooseMonitor(monitors, rect.CenterX, rect.CenterY).WorkArea;
    }

    /// <summary>
    /// The strip alone, or the whole frame while the selected tab waits for
    /// its phone: then there is no game below to cover the space.
    /// </summary>
    private void PlaceStrip()
    {
        if (_outer.Width <= 0 || IsFullscreen)
        {
            return;
        }

        MoveOwn(SelectedWaits ? _outer : _outer with { Height = StripHeight });

        if (SelectedWaits)
        {
            RaiseOwn();
        }
    }

    /// <summary>
    /// Every tab's game at the same place below the strip, the selected one
    /// on top. A window that will not grow that far (scrcpy keeps the
    /// game's ratio) is centred, as the PC centres it in its host.
    /// </summary>
    private void PlaceGames(bool skipSelected = false)
    {
        if (_outer.Width <= 0)
        {
            return;
        }

        var zone = new ScreenRect(_outer.X, _outer.Y + StripHeight, _outer.Width, _outer.Height - StripHeight);

        foreach (var tab in Items.Where(t => t.ShowsGame).OrderBy(t => t.IsSelected))
        {
            if (skipSelected && tab.IsSelected)
            {
                continue;
            }

            _windows.MoveWindow(tab.Window, zone);

            if (_windows.GetWindowRect(tab.Window) is { Width: > 0 } got
                && (got.Width < zone.Width - 1 || got.Height < zone.Height - 1))
            {
                _windows.MoveWindow(
                    tab.Window,
                    new ScreenRect(
                        zone.X + ((zone.Width - got.Width) / 2),
                        zone.Y + ((zone.Height - got.Height) / 2),
                        got.Width,
                        got.Height));
            }

            if (tab.IsSelected)
            {
                _gameAt = _windows.GetWindowRect(tab.Window);
            }
        }

        if (!skipSelected && Selected is { ShowsGame: true } selected && !_adopting)
        {
            BringForward(selected.Window, activate: false);
        }
    }

    /// <summary>The strip was dragged: the games come along.</summary>
    private void FollowStrip()
    {
        if (_following || IsFullscreen || Handle == 0 || MacNative.Frame(Handle) is not { } frame)
        {
            return;
        }

        var x = (int)Math.Round(frame.X);
        var y = (int)Math.Round(frame.Y);

        if (x == _outer.X && y == _outer.Y)
        {
            return;
        }

        _stripMovedAt = Environment.TickCount64;
        Outer = _outer with { X = x, Y = y };
        Dispatcher.BeginInvoke(() => PlaceGames());
    }

    /// <summary>
    /// The player moved or resized the selected game by its own title bar
    /// or edges: the strip and the other tabs follow it. A new size is
    /// taken once it has held still for a tick, then given the picture's
    /// shape as the PC's frame keeps it while it is resized. While the
    /// selected tab waits, its waiting view is kept in front instead.
    /// </summary>
    private void FollowGame()
    {
        if (!_shown || _closing || _dragged is not null)
        {
            return;
        }

        if (SelectedWaits)
        {
            KeepWaitingInFront();
            return;
        }

        if (IsFullscreen
            || Selected is not { ShowsGame: true } tab
            || _windows.GetWindowRect(tab.Window) is not { Width: > 0, Height: > 0 } seen)
        {
            return;
        }

        KeepStripWithGame();

        if (_gameAt is not { } expected)
        {
            _gameAt = seen;
            return;
        }

        if (Near(seen, expected))
        {
            _resizing = null;
            return;
        }

        // Moved only: everything follows at once, while it is dragged.
        if (seen.Width == expected.Width && seen.Height == expected.Height)
        {
            Outer = _outer with { X = _outer.X + seen.X - expected.X, Y = _outer.Y + seen.Y - expected.Y };
            _gameAt = seen;
            PlaceStrip();
            PlaceGames(skipSelected: true);
            return;
        }

        if (_resizing is not { } held || !Near(held, seen))
        {
            _resizing = seen;
            return;
        }

        _resizing = null;

        if (tab.Aspect > 0)
        {
            // The edge the player most likely dragged: the width drives the
            // height, unless only the height changed.
            var edge = seen.Width != expected.Width
                ? (seen.Y != expected.Y && seen.Bottom == expected.Bottom ? AspectSizing.TopLeft : AspectSizing.BottomRight)
                : AspectSizing.Bottom;

            var shaped = AspectSizing.Constrain(
                seen, edge, tab.Aspect, (0, MacWindowController.TitleBarHeight), MinFrameWidth);

            if (!Near(shaped, seen))
            {
                _windows.MoveWindow(tab.Window, shaped);
                seen = _windows.GetWindowRect(tab.Window) is { Width: > 0 } now ? now : shaped;
            }
        }

        Outer = new ScreenRect(seen.X, seen.Y - StripHeight, seen.Width, seen.Height + StripHeight);
        _gameAt = seen;
        PlaceStrip();
        PlaceGames(skipSelected: true);
    }

    /// <summary>The follow timer runs only while there is something to follow.</summary>
    private void UpdateFollowing()
    {
        if (Items.Count > 0 && !_closing)
        {
            _follow.Start();
        }
        else
        {
            _follow.Stop();
        }
    }

    /// <summary>
    /// The waiting view stays above the games stacked under it: a click on
    /// one of them, or a game that showed itself, would cover it.
    /// </summary>
    private void KeepWaitingInFront()
    {
        // Not over another application the player went to.
        if (Handle == 0 || !IsVisible || !PlayerStillHere())
        {
            return;
        }

        List<long> stacked = [(long)Handle, .. Items.Where(t => t.Window != 0).Select(t => (long)t.Window)];

        if (stacked.Count > 1 && MacNative.dthub_front_most_of([.. stacked], stacked.Count) != Handle)
        {
            RaiseOwn();
        }
    }

    /// <summary>
    /// The strip was activated: the keyboard goes to the selected game, as
    /// the PC's OnActivated gives it to the docked window. A moment later,
    /// so a press on the strip (a window or tab drag) is not cut short;
    /// again later while the strip is still being dragged.
    /// </summary>
    private void GiveKeyboard(int attempts)
    {
        if (Selected is not { ShowsGame: true })
        {
            return;
        }

        DispatcherTimer.RunOnce(
            () =>
            {
                if (_closing || !IsActive || Selected is not { ShowsGame: true } tab)
                {
                    return;
                }

                if (_dragged is not null || Environment.TickCount64 - _stripMovedAt < 250)
                {
                    if (attempts > 1)
                    {
                        GiveKeyboard(attempts - 1);
                    }

                    return;
                }

                BringForward(tab.Window, activate: true);
            },
            TimeSpan.FromMilliseconds(250));
    }

    /// <summary>
    /// A scrcpy window comes to the front by itself when it first shows,
    /// which can be after it was stacked: the selected tab's game is
    /// brought back once that has settled, twice for a slow window. Only if
    /// that tab is still the selected one and the player has not gone to
    /// another application meanwhile.
    /// </summary>
    private void SettleLater()
    {
        var tried = Selected;

        foreach (var settle in (int[])[600, 1500])
        {
            DispatcherTimer.RunOnce(
                () =>
                {
                    if (_closing
                        || Selected is not { ShowsGame: true } shown
                        || !ReferenceEquals(shown, tried)
                        || !PlayerStillHere())
                    {
                        return;
                    }

                    Fit(shown);
                    PlaceStrip();
                    PlaceGames();
                    BringForward(shown.Window, activate: true);
                },
                TimeSpan.FromMilliseconds(settle));
        }
    }

    /// <summary>The window in front is DT Hub's or one of the frame's games.</summary>
    private bool PlayerStillHere()
    {
        var front = (nint)MacNative.dthub_front_window();

        return front == 0
            || Items.Any(t => t.Window == front)
            || MacNative.dthub_window_pid(front) == Environment.ProcessId;
    }

    /// <summary>
    /// Docked windows ignore the launcher's raises (see
    /// <see cref="MacWindowController.Focus"/>): the frame goes past that
    /// to show its own selection.
    /// </summary>
    private void BringForward(nint window, bool activate)
    {
        if (_mac is { } mac)
        {
            if (activate)
            {
                mac.ShowDocked(window);
            }
            else
            {
                _ = MacNative.dthub_raise_window(window, activate: false);
            }
        }
        else if (activate)
        {
            _windows.Focus(window);
        }
        else
        {
            _windows.Raise(window);
        }
    }

    /// <summary>Our own window, through AppKit: the controller reads the frame's handle as the whole frame.</summary>
    private void MoveOwn(ScreenRect rect)
    {
        if (Handle == 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        _following = true;

        try
        {
            _ = MacNative.dthub_move_window(Handle, rect.X, rect.Y, rect.Width, rect.Height);
        }
        finally
        {
            _following = false;
        }
    }

    /// <summary>Our strip in front of the games, without taking activation.</summary>
    /// <summary>
    /// The strip is DT Hub's window, the games scrcpy's: a click in a game
    /// brings scrcpy's windows forward and leaves the strip under whatever
    /// application was in front, out of sight. Whenever one of the frame's
    /// games comes to the front, the strip comes with it, without taking
    /// the keyboard.
    /// </summary>
    private void KeepStripWithGame()
    {
        var front = (nint)MacNative.dthub_front_window();
        var inFront = front != 0 && Items.Any(t => t.ShowsGame && t.Window == front);

        if (inFront && front != _frontGame)
        {
            RaiseOwn();
        }

        _frontGame = inFront ? front : 0;
    }

    private nint _frontGame;

    private void RaiseOwn()
    {
        if (Handle != 0 && IsVisible)
        {
            _ = MacNative.dthub_raise_window(Handle, activate: false);
        }
    }

    /// <summary>
    /// macOS lets an application that is not in front bring another one
    /// forward only if it is itself active, which DT Hub is not while it
    /// reopens the games at startup. When the selected tab's game could not
    /// be brought forward, the strip selects the tab whose game is shown,
    /// rather than name a game hidden behind it.
    /// </summary>
    private void AdoptFront()
    {
        var games = Items.Where(t => t.ShowsGame).ToList();

        if (games.Count < 2 || SelectedWaits)
        {
            return;
        }

        var front = (nint)MacNative.dthub_front_most_of(
            [.. games.Select(t => (long)t.Window)], games.Count);

        if (front != 0 && games.FirstOrDefault(t => t.Window == front) is { IsSelected: false } shown)
        {
            _adopting = true;

            try
            {
                shown.IsSelected = true;
            }
            finally
            {
                _adopting = false;
            }
        }
    }

    /// <summary>
    /// The PC's title, "game · status  (shortcuts)", for the window lists;
    /// the borderless strip shows the status and the shortcuts itself.
    /// </summary>
    private void Retitle()
    {
        var title = Selected is { } tab
            ? tab.Status is { } status ? $"{tab.Title} · {status}" : tab.Title
            : DtHub.Core.ProductInfo.Name;

        Title = string.IsNullOrWhiteSpace(_reminder) ? title : $"{title}  ({_reminder.Trim()})";

        var parts = new[] { Selected?.Status, _reminder?.Trim() }.Where(p => !string.IsNullOrWhiteSpace(p));
        Hint.Text = string.Join("  ·  ", parts);
    }

    private void CloseIfEmpty()
    {
        if (Items.Count > 0 || _closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    private void OnStripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnTabClose(object? sender, RoutedEventArgs e)
    {
        _pressed = null;

        if ((sender as Control)?.DataContext is not GameTabViewModel tab)
        {
            return;
        }

        if (Items.Count == 1)
        {
            Close();
            return;
        }

        Detach(tab.Key, reveal: false);
        CloseRequested?.Invoke(this, [tab.Key]);
    }

    private void OnCloseAll(object? sender, RoutedEventArgs e) => Close();

    /// <summary>The waiting view's Close: closes that account, as its tab's cross does.</summary>
    private void OnWaitingClose()
    {
        if (Selected is not { } tab)
        {
            return;
        }

        if (Items.Count == 1)
        {
            Close();
            return;
        }

        Detach(tab.Key, reveal: false);
        CloseRequested?.Invoke(this, [tab.Key]);
    }

    // Dragging a tab, on the model of the PC frame and of the accounts list.

    private void OnTabsPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed = null;

        if (!e.GetCurrentPoint(Tabs).Properties.IsLeftButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        // A press on the cross is not the start of a drag. In Avalonia the
        // tab itself is a Button too (RadioButton derives from it): only a
        // plain button, the cross, counts.
        foreach (var node in source.GetSelfAndVisualAncestors())
        {
            if (node is Button and not Avalonia.Controls.Primitives.ToggleButton)
            {
                return;
            }

            if (ReferenceEquals(node, Tabs))
            {
                break;
            }
        }

        _pressed = (source as Control)?.DataContext as GameTabViewModel
            ?? source.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<GameTabViewModel>().FirstOrDefault();
        _origin = e.GetPosition(this);
    }

    private void OnTabsMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is { } pressed)
        {
            if (!e.GetCurrentPoint(Tabs).Properties.IsLeftButtonPressed)
            {
                _pressed = null;
                return;
            }

            // Only the horizontal distance counts: the strip is horizontal.
            if (Math.Abs(e.GetPosition(this).X - _origin.X) < DragThreshold)
            {
                return;
            }

            _pressed = null;
            _dragged = pressed;
        }

        if (_dragged is null)
        {
            return;
        }

        if (Hovered(e.GetPosition(Tabs)) is not { } target)
        {
            ClearDropHints();
            return;
        }

        foreach (var tab in Items)
        {
            tab.DropBefore = target.Before && ReferenceEquals(tab, target.Onto);
            tab.DropAfter = !target.Before && ReferenceEquals(tab, target.Onto);
        }
    }

    private void OnTabsReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressed = null;

        if (_dragged is not { } moved)
        {
            return;
        }

        var target = Hovered(e.GetPosition(Tabs));

        // The tab's own release would select it: the gesture was a drag.
        e.Handled = true;
        EndDrag();
        e.Pointer.Capture(null);

        if (target is { } drop && !ReferenceEquals(drop.Onto, moved))
        {
            Rearrange(moved, drop.Onto, drop.Before);
        }
    }

    private void EndDrag()
    {
        _dragged = null;
        ClearDropHints();
    }

    /// <summary>
    /// The tab under the pointer, and whether the pointer is in its left
    /// half. Past the last tab, the end of the strip: dropping there sorts
    /// the tab last, as on the PC.
    /// </summary>
    private (GameTabViewModel Onto, bool Before)? Hovered(Point position)
    {
        var surface = Tabs.GetVisualsAt(position)
            .SelectMany(visual => visual.GetSelfAndVisualAncestors())
            .OfType<Control>()
            .FirstOrDefault(control => control.Classes.Contains("dropSurface"));

        if (surface is { DataContext: GameTabViewModel onto })
        {
            var local = Tabs.TranslatePoint(position, surface) ?? default;
            return (onto, local.X < surface.Bounds.Width / 2);
        }

        return Items.Count > 0 && position.Y >= 0 && position.Y <= Strip.Bounds.Height
            ? (Items[^1], false)
            : null;
    }

    private void ClearDropHints()
    {
        foreach (var tab in Items)
        {
            tab.ClearDropHint();
        }
    }

    /// <summary>
    /// Sorts the tab right away, then makes the accounts list follow
    /// through <see cref="Reordered"/>.
    /// </summary>
    private void Rearrange(GameTabViewModel moved, GameTabViewModel onto, bool before)
    {
        var from = Items.IndexOf(moved);
        var to = Items.IndexOf(onto);

        if (from < 0 || to < 0)
        {
            return;
        }

        var wanted = before ? to : to + 1;

        if (wanted > from)
        {
            wanted--;
        }

        if (wanted != from)
        {
            Items.Move(from, wanted);
        }

        Reordered?.Invoke(this, (moved.Key, onto.Key, before));
    }
}
