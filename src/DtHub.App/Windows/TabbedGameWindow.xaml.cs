using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using DtHub.App.ViewModels;
using DtHub.Core.Windows;

namespace DtHub.App.Windows;

/// <summary>
/// The tabbed frame: several game windows housed in a single chassis, only one
/// of which shows at a time.
///
/// Windows are neither recreated nor redrawn here: they are docked as-is by
/// <see cref="IWindowController.Dock"/>, and the frame only places and shows
/// them. Measured on a real session, a docked scrcpy window keeps rendering
/// the picture and receiving the pointer.
/// </summary>
public partial class TabbedGameWindow : Window
{
    /// <summary>Windows asks a window being resized for its size.</summary>
    private const int WmSizing = 0x0214;

    private readonly IWindowController _windows;

    private GameTabViewModel? _pressed;
    private Point _origin;
    private bool _fitting;
    private bool _closing;

    public TabbedGameWindow(IWindowController windows)
    {
        _windows = windows;

        InitializeComponent();

        Tabs.ItemsSource = Items;
        Items.CollectionChanged += (_, _) => Retitle();
    }

    /// <summary>The tabs, in the order they appear.</summary>
    public ObservableCollection<GameTabViewModel> Items { get; } = [];

    /// <summary>Raised when the user has reordered the tabs.</summary>
    public event EventHandler<(string Moved, string Onto, bool Before)>? Reordered;

    /// <summary>
    /// Native handle of the frame, retained once and for all.
    ///
    /// It used to be recomputed on every read, which queries a WPF window and
    /// is only allowed on its own thread. The configurator and the guides
    /// window had already been fixed this way; this one had been forgotten,
    /// and it is now read from the foreground watcher, which does not live on
    /// that thread. This is exactly the path of the bug that had repeated
    /// itself two hundred sixty-four times.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// Docks a game window and adds its tab. Has no effect if it is already
    /// there.
    /// </summary>
    public bool Attach(
        string key, string title, string? iconPath, nint window, double aspect, string? colourBrushKey)
    {
        if (Find(key) is not null || window == 0)
        {
            return false;
        }

        if (!_windows.Dock(window, Handle))
        {
            return false;
        }

        var tab = new GameTabViewModel(key, title, iconPath, window, aspect, colourBrushKey);
        tab.PropertyChanged += OnTabChanged;
        Items.Add(tab);

        // The first one to arrive gets shown: a frame open on emptiness would
        // make no sense.
        Select(Items.Count == 1 ? tab : Items.First(t => t.IsSelected));

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Settle);

        return true;
    }

    /// <summary>
    /// Raised when the frame closes, with the accounts it was
    /// housing.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? CloseRequested;

    /// <summary>
    /// Takes a window back out of the frame and gives it back its previous
    /// state.
    /// </summary>
    /// <param name="key">The account to take back out.</param>
    /// <param name="reveal">
    /// False to leave it hidden. Used when the frame closes: the window must
    /// leave the frame so it does not die with it, but showing it for a split
    /// second before closing it would only make it flicker.
    /// </param>
    public bool Detach(string key, bool reveal = true)
    {
        if (Find(key) is not { } tab)
        {
            return false;
        }

        tab.PropertyChanged -= OnTabChanged;
        Items.Remove(tab);

        if (reveal)
        {
            _windows.SetVisible(tab.Window, true);
        }

        _windows.Undock(tab.Window);

        if (tab.IsSelected && Items.Count > 0)
        {
            Select(Items[0]);
        }

        CloseIfEmpty();

        return true;
    }

    /// <summary>Takes all windows back out.</summary>
    public void DetachAll(bool reveal = true)
    {
        foreach (var key in Items.Select(t => t.Key).ToList())
        {
            Detach(key, reveal);
        }
    }

    /// <summary>
    /// Keeps only the tabs on this list, and takes the others back out.
    ///
    /// A session can die without going through us: the game window closed by
    /// hand, the phone unplugged. The tab then stayed in the bar and pointed
    /// to a window that was gone; worse, reopening the account no longer
    /// docked it, the ghost tab making it look as if it were already there.
    /// </summary>
    public void KeepOnly(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in Items.Select(t => t.Key).Where(k => !keys.Contains(k)).ToList())
        {
            Detach(key);
        }
    }

    /// <summary>True if this account is housed here.</summary>
    public bool Holds(string key) => Find(key) is not null;

    /// <summary>Shows this account's tab, if it is housed here.</summary>
    public void Show(string key)
    {
        if (Find(key) is { } tab)
        {
            Select(tab);
        }
    }

    /// <summary>
    /// Sorts the tabs in this order, that of the accounts list.
    /// </summary>
    public void Reorder(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        for (var wanted = 0; wanted < keys.Count; wanted++)
        {
            var present = Items.ToList().FindIndex(t => string.Equals(t.Key, keys[wanted], StringComparison.Ordinal));

            if (present >= 0 && present != wanted && wanted < Items.Count)
            {
                Items.Move(present, wanted);
            }
        }
    }

    /// <summary>
    /// Closes the frame as soon as it no longer houses anything.
    ///
    /// An empty frame has nothing to show and does not say what it is waiting
    /// for. Its position is handed back to the launcher before it goes, which
    /// remembers it.
    /// </summary>
    private void CloseIfEmpty()
    {
        if (Items.Count > 0 || _closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    /// <summary>
    /// Renames a tab that is already housed. Has no effect if this account is
    /// not in the frame, which is the ordinary case for a free window.
    ///
    /// The frame's title is then refreshed: it carries the name of the shown
    /// tab, which may be the one that was just renamed.
    /// </summary>
    public void Rename(string key, string title)
    {
        if (Find(key) is not { } tab)
        {
            return;
        }

        tab.Title = title;
        Retitle();
    }

    /// <summary>
    /// Changes the mark of a tab that is already housed. Has no effect
    /// if this account is not in the frame, like renaming.
    /// </summary>
    public void Recolour(string key, string? colourBrushKey)
    {
        if (Find(key) is { } tab)
        {
            tab.ColourBrushKey = colourBrushKey;
        }
    }

    private GameTabViewModel? Find(string key) =>
        Items.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameTabViewModel.IsSelected)
            && sender is GameTabViewModel { IsSelected: true } tab)
        {
            Select(tab);
        }
    }

    /// <summary>
    /// Shows one window and hides the others.
    ///
    /// Hiding rather than detaching: coming back to a tab must be immediate,
    /// and a window detached then reattached would lose its place every time.
    /// </summary>
    private void Select(GameTabViewModel tab)
    {
        foreach (var other in Items)
        {
            other.IsSelected = ReferenceEquals(other, tab);
            _windows.SetVisible(other.Window, other.IsSelected);
        }

        Fit(tab);
        Place(tab);
        Retitle();

        // The keyboard does not follow the tab on its own. A docked window is
        // a child of the frame, hence out of reach of the foreground: without
        // this call, the picture shows and the mouse works, but nothing that
        // is typed arrives, paste included since scrcpy pastes by typing.
        _ = _windows.GiveKeyboardFocus(tab.Window);
    }

    /// <summary>
    /// Moves to the next tab, or the previous one. The traversal loops, like
    /// that of free windows, so that Ctrl+Tab means the same thing in both
    /// modes.
    /// </summary>
    /// <returns>False if there is nothing to cycle through.</returns>
    public bool Cycle(bool forward)
    {
        if (Items.Count < 2)
        {
            return false;
        }

        var courant = Items.FirstOrDefault(t => t.IsSelected) ?? Items[0];
        var pas = forward ? 1 : -1;
        var voulu = ((Items.IndexOf(courant) + pas) % Items.Count + Items.Count) % Items.Count;

        // Checking it is enough: the change goes through OnTabChanged, like a
        // click.
        Items[voulu].IsSelected = true;

        return true;
    }

    /// <summary>
    /// The active tab's aspect ratio, or <c>null</c> if the frame is empty or
    /// the display does not impose one.
    ///
    /// Published so that the geometry commands can compute a rectangle that
    /// gives the game its shape, as they do for a free window.
    /// </summary>
    public double? SelectedAspect => Aspect();

    /// <summary>
    /// The chassis's footprint, borders, title bar and tab bar included. This
    /// is what must be subtracted before applying an aspect ratio, since the
    /// ratio holds for the game area, not for the window.
    /// </summary>
    public (int Width, int Height)? Chassis => Chrome();

    /// <summary>True when the frame covers the whole screen.</summary>
    public bool IsFullscreen => _fullscreen;

    private bool _fullscreen;
    private WindowStyle _styleBefore = WindowStyle.SingleBorderWindow;
    private ResizeMode _resizeBefore = ResizeMode.CanResize;
    private ScreenRect _boundsBefore;

    /// <summary>
    /// Places the frame at the requested location and size, in desktop pixels.
    ///
    /// This is the only entry point for geometry commands. Nothing is ever
    /// applied to the docked window itself: it is a child of the frame, its
    /// coordinates are not those of the screen, and giving it back a border
    /// would fit it with a title bar inside the frame.
    /// </summary>
    public void ApplyRect(ScreenRect outer)
    {
        if (Handle == 0 || _fullscreen || outer.Width <= 0 || outer.Height <= 0)
        {
            return;
        }

        _windows.MoveWindow(Handle, outer);

        // Twice, the second time once the message loop has passed. Crossing
        // into a screen with a different density makes WPF rescale the size,
        // and the first placement lands in the density of the starting screen:
        // this is the trap already met when restoring a placement.
        //
        // The shape is only picked up again at that point: a rectangle coming
        // from elsewhere, that of a free window being stacked for instance,
        // has no reason to already have the game's shape.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                if (Handle == 0 || _fullscreen)
                {
                    return;
                }

                _windows.MoveWindow(Handle, outer);
                Settle();
            });
    }

    /// <summary>
    /// Gives the frame back the shape of the shown tab, once the layout has
    /// settled.
    ///
    /// Deferred on purpose. Restoring the frame's placement reapplies the
    /// saved size a second time through the dispatcher, at this same priority,
    /// hence after the <see cref="Fit"/> call from <see cref="Select"/>: the
    /// frame used to keep a shape that matched no tab, and the docked window
    /// was simply recentered with its black bars. Queued after it, the reapply
    /// gets the last word.
    /// </summary>
    private void Settle()
    {
        if (_fullscreen || Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        Fit(tab);
        Place(tab);
    }

    /// <summary>
    /// Makes the frame cover an entire rectangle, chassis removed, or gives it
    /// back its chassis and its place.
    ///
    /// The style is removed by WPF, the rectangle placed by us. This is the
    /// only split that holds up: removing the border styles through Win32
    /// would put the window at odds with its own non-client area manager,
    /// which rewrites them at the slightest occasion; and letting WPF place
    /// the geometry through <c>Maximized</c> would not cover the taskbar,
    /// whereas full screen for free windows takes the screen's entire bounds.
    /// The state therefore stays <c>Normal</c>, which is why <see cref="Fit"/>
    /// must back off on the flag rather than on the state.
    ///
    /// The style first, the geometry next: applied before, it would be undone
    /// by the chassis coming back.
    /// </summary>
    /// <param name="on">True to cover, false to give back.</param>
    /// <param name="target">
    /// The screen's bounds going in, the rectangle to give back coming out.
    /// </param>
    public void SetFullscreen(bool on, ScreenRect target)
    {
        if (Handle == 0 || on == _fullscreen)
        {
            return;
        }

        _fullscreen = on;

        if (on)
        {
            // Where we came from, kept here and not with the caller: it is the
            // frame that knows what it occupied, and the caller cannot read it
            // back once the placement is done.
            _boundsBefore = _windows.GetWindowRect(Handle) ?? default;
            _styleBefore = WindowStyle;
            _resizeBefore = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = _styleBefore;
            ResizeMode = _resizeBefore;

            if (target.Width <= 0 || target.Height <= 0)
            {
                target = _boundsBefore;
            }
        }

        if (target.Width > 0 && target.Height > 0)
        {
            _windows.MoveWindow(Handle, target);
        }

        if (Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        // Only on the way out: the frame takes back the game's shape, which it
        // must absolutely not do while it covers the screen.
        if (!on)
        {
            Fit(tab);
        }

        Place(tab);
    }

    private void OnHostResized(object sender, SizeChangedEventArgs e)
    {
        if (Items.FirstOrDefault(t => t.IsSelected) is { } tab)
        {
            Place(tab);
        }
    }

    /// <summary>
    /// The hosting area, in pixels and in the coordinates of the frame's
    /// client area, that is, the ones a docked window expects.
    ///
    /// The points are asked from WPF rather than computed: converting the
    /// density-independent units ourselves turned out wrong on a screen at one
    /// hundred fifty percent, and the window landed one hundred pixels too low
    /// and too far right.
    /// </summary>
    private ScreenRect? HostArea()
    {
        if (Handle == 0 || !IsVisible || Accueil.ActualWidth <= 0 || Accueil.ActualHeight <= 0)
        {
            return null;
        }

        var racine = PointToScreen(new Point(0, 0));
        var coin = Accueil.PointToScreen(new Point(0, 0));
        var end = Accueil.PointToScreen(new Point(Accueil.ActualWidth, Accueil.ActualHeight));

        return new ScreenRect(
            (int)Math.Round(coin.X - racine.X),
            (int)Math.Round(coin.Y - racine.Y),
            (int)Math.Round(end.X - coin.X),
            (int)Math.Round(end.Y - coin.Y));
    }

    /// <summary>
    /// What the chassis takes up around the game area: borders, title bar, and
    /// tab bar. Constant, hence measurable on the current size and reusable
    /// for any other.
    /// </summary>
    private (int Width, int Height)? Chrome()
    {
        if (_windows.GetWindowRect(Handle) is not { } outer
            || HostArea() is not { } zone
            || zone.Width <= 0
            || zone.Height <= 0)
        {
            return null;
        }

        return (outer.Width - zone.Width, outer.Height - zone.Height);
    }

    /// <summary>
    /// The shown tab's aspect ratio, or <c>null</c> if there is
    /// none.
    /// </summary>
    private double? Aspect() =>
        Items.FirstOrDefault(t => t.IsSelected) is { Aspect: > 0 } tab ? tab.Aspect : null;

    /// <summary>
    /// Keeps the picture's shape while the window is being resized: the frame
    /// can be grabbed from any edge, like a free game window. The rule lives
    /// in <see cref="AspectSizing"/>, where it can be checked without opening
    /// a UI.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmSizing || Aspect() is not { } rapport || Chrome() is not { } chrome)
        {
            return 0;
        }

        var voulu = Marshal.PtrToStructure<SizingRect>(lParam);

        var corrige = AspectSizing.Constrain(
            new ScreenRect(
                voulu.Left,
                voulu.Top,
                voulu.Right - voulu.Left,
                voulu.Bottom - voulu.Top),
            (int)wParam,
            rapport,
            chrome,
            (int)Math.Round(MinWidth * VisualTreeHelper.GetDpi(this).DpiScaleX));

        voulu.Left = corrige.X;
        voulu.Top = corrige.Y;
        voulu.Right = corrige.Right;
        voulu.Bottom = corrige.Bottom;

        Marshal.StructureToPtr(voulu, lParam, false);
        handled = true;

        return 1;
    }

    /// <summary>
    /// Gives the frame the picture's shape, when it is not the mouse deciding:
    /// when a tab arrives, or when switching to a tab whose display does not
    /// have the same shape.
    /// </summary>
    private void Fit(GameTabViewModel tab)
    {
        if (_fitting
            || _fullscreen
            || tab.Aspect <= 0
            || WindowState != WindowState.Normal
            || Chrome() is not { } chrome
            || HostArea() is not { } zone)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var wanted = ((zone.Width / dpi.DpiScaleX) / tab.Aspect) + (chrome.Height / dpi.DpiScaleY);

        // Two pixels of tolerance: rounding of the aspect ratio does not
        // justify resizing the window on every pass, which would make it shake
        // without ever settling.
        if (Math.Abs(wanted - Height) <= 2)
        {
            return;
        }

        var place = WorkAreaHeight(dpi);

        _fitting = true;

        try
        {
            if (wanted <= place)
            {
                Height = wanted;
                return;
            }

            // No more room in height: the width gives way instead. Trimming
            // the height further would give a squashed frame, and it has
            // already been found too short once.
            Height = place;
            Width = Math.Max(
                MinWidth,
                ((place - (chrome.Height / dpi.DpiScaleY)) * tab.Aspect) + (chrome.Width / dpi.DpiScaleX));
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>
    /// Usable height of the screen carrying the frame, taskbar excluded, in
    /// WPF units.
    /// </summary>
    private double WorkAreaHeight(DpiScale dpi)
    {
        var monitors = _windows.GetMonitors();

        if (monitors.Count == 0 || _windows.GetWindowRect(Handle) is not { } outer)
        {
            return SystemParameters.WorkArea.Height;
        }

        var screen = WindowLayoutCalculator.ChooseMonitor(monitors, outer.CenterX, outer.CenterY);

        return screen.WorkArea.Height / dpi.DpiScaleY;
    }

    /// <summary>
    /// Places the docked window over the whole hosting area.
    /// </summary>
    private void Place(GameTabViewModel tab)
    {
        if (HostArea() is not { } zone || zone.Width <= 0 || zone.Height <= 0)
        {
            return;
        }

        _windows.MoveWindow(tab.Window, zone);

        // Safety net: the frame normally already has the picture's shape, but
        // there is still the rounding, and the case where it is enlarged or
        // too small to conform to it. The window then comes out smaller than
        // requested and stuck to the top left; we recenter it, or else the
        // black remainder ends up entirely on one side.
        if (_windows.GetWindowRect(tab.Window) is not { } pris
            || (pris.Width >= zone.Width && pris.Height >= zone.Height))
        {
            return;
        }

        _windows.MoveWindow(
            tab.Window,
            new ScreenRect(
                zone.X + ((zone.Width - pris.Width) / 2),
                zone.Y + ((zone.Height - pris.Height) / 2),
                pris.Width,
                pris.Height));
    }

    private void Retitle() =>
        Title = Items.FirstOrDefault(t => t.IsSelected) is { } tab
            ? $"{Core.ProductInfo.Name}  ·  {tab.Title}"
            : $"{Core.ProductInfo.Name}  ·  onglets";

    // Dragging a tab, on the model of the accounts list.

    private void OnTabPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = (sender as FrameworkElement)?.DataContext as GameTabViewModel;
        _origin = e.GetPosition(this);
    }

    private void OnTabMoved(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        // Only the horizontal distance counts: the bar is horizontal, and the
        // vertical jitter of a plain click would otherwise turn into a drag.
        if (Math.Abs(e.GetPosition(this).X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        var porte = _pressed;
        _pressed = null;

        try
        {
            DragDrop.DoDragDrop(this, porte, DragDropEffects.Move);
        }
        finally
        {
            ClearDropHints();
        }
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (Carried(e) is null
            || (sender as FrameworkElement)?.DataContext is not GameTabViewModel onto)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        Hint(onto, Before(sender, e));
    }

    private void OnTabDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is GameTabViewModel onto)
        {
            onto.ClearDropHint();
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();

        if ((sender as FrameworkElement)?.DataContext is not GameTabViewModel onto
            || Carried(e) is not { } moved
            || ReferenceEquals(moved, onto))
        {
            return;
        }

        Rearrange(moved, onto, Before(sender, e));
    }

    // The rest of the bar, past the last tab: dropping there sorts it to the
    // end of the list. Without this you had to aim for a tab, and dropping
    // beside one did nothing without saying why.

    private void OnStripDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (Carried(e) is null || Items.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        Hint(Items[^1], before: false);
    }

    private void OnStripDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();
    }

    private void OnStripDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();

        if (Carried(e) is not { } moved
            || Items.Count == 0
            || ReferenceEquals(moved, Items[^1]))
        {
            return;
        }

        Rearrange(moved, Items[^1], before: false);
    }

    private static GameTabViewModel? Carried(DragEventArgs e) =>
        e.Data.GetData(typeof(GameTabViewModel)) as GameTabViewModel;

    private static bool Before(object sender, DragEventArgs e) =>
        sender is FrameworkElement cible
        && e.GetPosition(cible).X < cible.ActualWidth / 2;

    private void Hint(GameTabViewModel onto, bool before)
    {
        foreach (var tab in Items)
        {
            tab.DropBefore = before && ReferenceEquals(tab, onto);
            tab.DropAfter = !before && ReferenceEquals(tab, onto);
        }
    }

    private void ClearDropHints()
    {
        foreach (var tab in Items)
        {
            tab.ClearDropHint();
        }
    }

    /// <summary>
    /// Sorts the tab right away, then makes the accounts list follow.
    ///
    /// Right away, and not once the save comes back: the round trip through
    /// settings took a moment during which the tab stayed where it was, and
    /// the gesture looked like it had done nothing.
    /// </summary>
    private void Rearrange(GameTabViewModel moved, GameTabViewModel onto, bool before)
    {
        var depart = Items.IndexOf(moved);
        var cible = Items.IndexOf(onto);

        if (depart < 0 || cible < 0)
        {
            return;
        }

        var voulu = before ? cible : cible + 1;

        if (voulu > depart)
        {
            voulu--;
        }

        if (voulu != depart)
        {
            Items.Move(depart, voulu);
        }

        Reordered?.Invoke(this, (moved.Key, onto.Key, before));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    /// <summary>
    /// The frame takes back control: the keyboard returns to the active tab.
    ///
    /// Without this, coming back through Alt+Tab would give activation to the
    /// frame, which has nothing to capture, and keystrokes would land beside
    /// the game instead of on it.
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        _ = _windows.GiveKeyboardFocus(tab.Window);

        // Twice, and this is not a precaution taken lightly: WPF gives focus
        // back to its own tree while handling WM_SETFOCUS, which arrives after
        // activation. The first call covers the ordinary case, the second one
        // follows up behind it. Naming the same window twice costs nothing,
        // since no queue is touched.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (IsActive && Items.FirstOrDefault(t => t.IsSelected) is { } encore)
                {
                    _ = _windows.GiveKeyboardFocus(encore.Window);
                }
            });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing the frame closes what it contains. Windows used to come back
        // out free, and you would end up with as many scattered game windows
        // as you thought you had just closed: the gesture did not do what it
        // says.
        //
        // They still leave the frame first, though hidden: a docked window is
        // a child of it, and Windows would destroy its children along with it
        // without giving scrcpy time to stop cleanly. Showing them in the
        // meantime would only make them flicker.
        _closing = true;

        List<string> loges = [.. Items.Select(t => t.Key)];

        DetachAll(reveal: false);

        base.OnClosing(e);

        CloseRequested?.Invoke(this, loges);
    }

    /// <summary>
    /// The rectangle Windows passes in WM_SIZING. Named apart from
    /// <c>System.Windows.Rect</c>, with which it shares nothing but the idea.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SizingRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
