// Instance members throughout: this is the PC window's API, called on an
// instance by the shared launcher.
#pragma warning disable CA1822

using System.Collections.ObjectModel;

using Avalonia.Controls;

using DtHub.App.ViewModels;
using DtHub.Core.Windows;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's tabbed frame, by its contract with GameLauncher. On the PC it
/// re-parents each game window into itself; macOS forbids housing another
/// application's window, so <see cref="IWindowController.Dock"/> refuses
/// and <see cref="Attach"/> reports it, and the launcher keeps the game in
/// its own window, as it does on the PC when docking fails.
/// shortcut: no tabs on the Mac yet; a tab strip above the game windows,
/// showing only the selected one, is the planned stand-in.
/// </summary>
public sealed class TabbedGameWindow : Window
{
    private readonly IWindowController _windows;

    public TabbedGameWindow(IWindowController windows)
    {
        _windows = windows;
        Title = DtHub.Core.ProductInfo.Name;
    }

    public ObservableCollection<GameTabViewModel> Items { get; } = [];

#pragma warning disable CS0067 // Never raised until the Mac has tabs; the launcher subscribes.
    public event EventHandler<(string Moved, string Onto, bool Before)>? Reordered;

    public event EventHandler<IReadOnlyList<string>>? CloseRequested;
#pragma warning restore CS0067

    public nint Handle => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    public new System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Threading.Dispatcher.UiThread;

    public string? ShortcutReminder { get; set; }

    public double? SelectedAspect => null;

    public (int Width, int Height)? Chassis => null;

    public bool IsFullscreen { get; private set; }

    public double Left => Position.X;

    public double Top => Position.Y;

    public bool Attach(
        string key, string title, string? iconPath, nint window, double aspect, string? colourBrushKey) =>
        window != 0 && _windows.Dock(window, Handle);

    public bool Detach(string key, bool reveal = true) => false;

    public void DetachAll(bool reveal = true)
    {
    }

    public void KeepOnly(IReadOnlyCollection<string> keys, IReadOnlySet<string> waiting)
    {
    }

    public bool Holds(string key) => false;

    public void Show(string key)
    {
    }

    public void ShowAccountKeys(Func<string, string?> keyOf)
    {
    }

    public void Reorder(IReadOnlyList<string> keys)
    {
    }

    public void Rename(string key, string title)
    {
    }

    public void Restate(string key, string? status)
    {
    }

    public void Recolour(string key, string? colourBrushKey)
    {
    }

    public bool Cycle(bool forward) => false;

    public void ApplyRect(ScreenRect outer) =>
        _windows.MoveWindow(Handle, outer);

    public void SetFullscreen(bool on, ScreenRect target) => IsFullscreen = on;

    /// <summary>WPF's Activate says whether it worked.</summary>
    public new bool Activate()
    {
        base.Activate();
        return true;
    }
}
