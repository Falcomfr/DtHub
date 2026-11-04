// Instance members where the PC window has them: the shared launcher calls
// them on an instance.
#pragma warning disable CA1822

using System.Collections.ObjectModel;
using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Windows;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's tabbed frame (src/DtHub.App/Windows/TabbedGameWindow.xaml.cs),
/// by its contract with GameLauncher, done the way macOS allows.
///
/// On the PC each game window is re-parented into the frame. macOS forbids
/// housing another application's window, so here the frame is only its tab
/// strip, and the game windows of its tabs are stacked exactly below it, at
/// the same place and size: the selected one is brought to the front, the
/// others wait underneath. Moving the strip takes them along. What the
/// launcher calls the frame's outer rectangle is the strip plus the game
/// window below it.
/// </summary>
public sealed partial class TabbedGameWindow : Window
{
    /// <summary>The strip's height, in points, as laid out in the AXAML.</summary>
    private const int StripHeight = 34;

    private readonly IWindowController _windows;
    private ScreenRect _outer;
    private ScreenRect _beforeFullscreen;
    private bool _closing;
    private bool _following;

    /// <summary>For Avalonia's loader and previewer only.</summary>
    public TabbedGameWindow()
        : this(new DtHub.Mac.Platform.MacWindowController())
    {
    }

    public TabbedGameWindow(IWindowController windows)
    {
        _windows = windows;
        InitializeComponent();
        Tabs.ItemsSource = Items;
        Width = 640;
        Height = StripHeight;
        Title = DtHub.Core.ProductInfo.Name;

        // Dragging the strip moves the window; the games follow once it
        // has moved.
        PositionChanged += (_, _) => FollowStrip();
        Activated += (_, _) => RaiseSelected();
    }

    public ObservableCollection<GameTabViewModel> Items { get; } = [];

    public event EventHandler<(string Moved, string Onto, bool Before)>? Reordered;

    /// <summary>
    /// Raised when the frame closes, with the accounts it was housing, or
    /// when a tab's cross closes its account alone.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? CloseRequested;

    public nint Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    public new System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Threading.Dispatcher.UiThread;

    public string? ShortcutReminder { get; set; }

    public double? SelectedAspect => Selected is { Aspect: > 0 } tab ? tab.Aspect : null;

    /// <summary>
    /// What the frame adds around a game's picture: the strip, and the
    /// game window's own title bar below it.
    /// </summary>
    public (int Width, int Height)? Chassis => (0, StripHeight + DtHub.Mac.Platform.MacWindowController.TitleBarHeight);

    public bool IsFullscreen { get; private set; }

    public double Left => _outer.X;

    public double Top => _outer.Y;

    private GameTabViewModel? Selected => Items.FirstOrDefault(t => t.IsSelected);

    /// <summary>
    /// Takes a game window into a tab, or back into its waiting tab. Has no
    /// effect if it is already there.
    /// </summary>
    public bool Attach(
        string key, string title, string? iconPath, nint window, double aspect, string? colourBrushKey)
    {
        if (window == 0)
        {
            return false;
        }

        if (Find(key) is { } existing)
        {
            if (existing.Window != 0)
            {
                return false;
            }

            existing.Window = window;
            SetWaiting(existing, false);
            Select(Selected ?? existing);
            return true;
        }

        var tab = new GameTabViewModel(key, title, iconPath, window, aspect, colourBrushKey);
        tab.PropertyChanged += OnTabChanged;
        Items.Add(tab);

        // The first window gives the frame its place: the strip goes right
        // above where the game already is.
        if (Items.Count == 1 && _windows.GetWindowRect(window) is { } at)
        {
            _outer = new ScreenRect(at.X, at.Y - StripHeight, at.Width, at.Height + StripHeight);
            PlaceStrip();
        }

        Select(Items.Count == 1 ? tab : Selected ?? tab);
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

        if (tab.IsSelected && Items.Count > 0 && !_closing)
        {
            Select(Items[0]);
        }

        // A revealed window stays where it is, in front: it was a tab, it
        // is now a window of its own.
        if (reveal && tab.Window != 0)
        {
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

            if (!alive)
            {
                tab.Window = 0;
            }

            SetWaiting(tab, waiting.Contains(tab.Key) || tab.Window == 0);
        }
    }

    public bool Holds(string key) => Find(key) is not null;

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

    /// <summary>The strip and the games below it fill this rectangle.</summary>
    public void ApplyRect(ScreenRect outer)
    {
        if (IsFullscreen || outer.Width <= 0 || outer.Height <= StripHeight)
        {
            return;
        }

        _outer = outer;
        PlaceStrip();
        PlaceGames();
    }

    /// <summary>
    /// Full screen: the strip steps aside and the selected game fills the
    /// screen; leaving it puts everything back where it was.
    /// </summary>
    public void SetFullscreen(bool on, ScreenRect target)
    {
        if (on == IsFullscreen)
        {
            return;
        }

        IsFullscreen = on;

        if (on)
        {
            _beforeFullscreen = _outer;
            Hide();
            _outer = new ScreenRect(target.X, target.Y - StripHeight, target.Width, target.Height + StripHeight);
            PlaceGames();
        }
        else
        {
            base.Show();
            ApplyRect(_beforeFullscreen.Width > 0 ? _beforeFullscreen : _outer);
        }
    }

    /// <summary>WPF's Activate says whether it worked.</summary>
    public new bool Activate()
    {
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

        Waiting.IsVisible = tab.IsWaiting || tab.Window == 0;
        PlaceStrip();
        PlaceGames();
        Retitle();

        if (tab.ShowsGame)
        {
            _windows.Focus(tab.Window);
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

    /// <summary>
    /// The strip alone, or the whole frame while the selected tab waits for
    /// its phone: then there is no game below to cover the space.
    /// </summary>
    private void PlaceStrip()
    {
        if (Handle == 0 || _outer.Width <= 0 || IsFullscreen)
        {
            return;
        }

        var waits = Selected is { } tab && (tab.IsWaiting || tab.Window == 0);
        var rect = waits ? _outer : _outer with { Height = StripHeight };

        _following = true;
        _windows.MoveWindow(Handle, rect);
        _following = false;
    }

    /// <summary>
    /// Every tab's game at the same place below the strip, the selected one
    /// on top. A window that will not grow that far (scrcpy keeps the
    /// game's ratio) is centred, as the PC centres it in its host.
    /// </summary>
    private void PlaceGames()
    {
        if (_outer.Width <= 0)
        {
            return;
        }

        var zone = new ScreenRect(_outer.X, _outer.Y + StripHeight, _outer.Width, _outer.Height - StripHeight);

        foreach (var tab in Items.Where(t => t.ShowsGame).OrderBy(t => t.IsSelected))
        {
            _windows.MoveWindow(tab.Window, zone, bringToFront: tab.IsSelected);
        }
    }

    /// <summary>The strip was dragged: the games come along.</summary>
    private void FollowStrip()
    {
        if (_following || IsFullscreen || _windows.GetWindowRect(Handle) is not { } strip)
        {
            return;
        }

        if (strip.X == _outer.X && strip.Y == _outer.Y)
        {
            return;
        }

        _outer = _outer with { X = strip.X, Y = strip.Y };
        Dispatcher.BeginInvoke(PlaceGames);
    }

    private void RaiseSelected()
    {
        if (Selected is { ShowsGame: true } tab)
        {
            _windows.Raise(tab.Window);
        }
    }

    private void Retitle()
    {
        var tab = Selected;
        var parts = new[] { tab?.Title, tab?.Status, ShortcutReminder }.Where(p => !string.IsNullOrWhiteSpace(p));
        Title = string.Join(" · ", parts);
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

    private void OnWaitingClose(object? sender, RoutedEventArgs e)
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

    /// <summary>
    /// shortcut: tabs are not reordered by dragging on the Mac yet; the
    /// panel's order still applies through <see cref="Reorder"/>.
    /// </summary>
    internal void Moved(string moved, string onto, bool before) =>
        Reordered?.Invoke(this, (moved, onto, before));
}
