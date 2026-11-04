using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using DtHub.Core.Hotkeys;
using DtHub.Core.Windows;

namespace DtHub.Mac.Platform;

/// <summary>
/// Linux's stand-in for <see cref="MacWindowController"/>, whose calls go
/// through the macOS-only libDtHubMac.dylib. It knows the screens, through
/// Avalonia, and moves nothing: scrcpy places each game window from its own
/// arguments, and the grid, tabs and full screen do nothing after launch.
/// shortcut: an X11 implementation (XMoveResizeWindow) would bring them
/// back on Linux; Wayland allows no outside placement at all.
/// </summary>
internal sealed class PassiveWindowController : IWindowController
{
    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        Dispatcher.UIThread.CheckAccess() ? Monitors() : Dispatcher.UIThread.Invoke(Monitors);

    public IReadOnlyList<WindowHandleInfo> FindWindows(int processId) => [];

    public bool IsWindow(nint handle) => false;

    public WindowPlacement? GetPlacement(nint handle) => null;

    public bool SetPlacement(nint handle, WindowPlacement placement) => false;

    public int GetWindowProcessId(nint handle) => 0;

    public ScreenRect? GetWindowRect(nint handle) => null;

    public ScreenRect? GetClientRect(nint handle) => null;

    public void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false)
    {
    }

    public WindowFrame GetWindowChrome(string? monitorDeviceName) =>
        new(0, MacWindowController.TitleBarHeight, 0, MacWindowController.TitleBarHeight);

    public void Focus(nint handle)
    {
    }

    public void RequestClose(nint handle)
    {
    }

    public void Raise(nint handle)
    {
    }

    public void SetTitle(nint handle, string title)
    {
    }

    public void SetBorderless(nint handle, bool borderless)
    {
    }

    public void SetFrameColour(nint handle, int? colourRef)
    {
    }

    public nint GetForegroundWindow() => 0;

    public bool Dock(nint child, nint host) => false;

    public bool Undock(nint child) => false;

    public bool IsDocked(nint child) => false;

    public void SetVisible(nint handle, bool visible)
    {
    }

    public bool GiveKeyboardFocus(nint child) => false;

    public bool IsModifierKeyDown() => false;

    public void ReleaseModifierKeys(nint handle, bool alt)
    {
    }

    private static List<MonitorInfo> Monitors()
    {
        var screens = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Screens;

        if (screens is null)
        {
            return [];
        }

        return
        [
            .. screens.All.Select((screen, index) => new MonitorInfo
            {
                DeviceName = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Bounds = Points(screen.Bounds, screen.Scaling),
                WorkArea = Points(screen.WorkingArea, screen.Scaling),
                IsPrimary = screen.IsPrimary,
            }),
        ];
    }

    private static ScreenRect Points(Avalonia.PixelRect rect, double scale) =>
        new(
            (int)Math.Round(rect.X / scale),
            (int)Math.Round(rect.Y / scale),
            (int)Math.Round(rect.Width / scale),
            (int)Math.Round(rect.Height / scale));
}

/// <summary>
/// Linux's stand-in for <see cref="MacHotkeyRegistrar"/> (Carbon, macOS
/// only): the shortcuts are kept and none is registered with the system.
/// shortcut: an X11 XGrabKey implementation would bring them on Linux.
/// </summary>
internal sealed class PassiveHotkeyRegistrar : IHotkeyRegistrar
{
#pragma warning disable CS0067 // Never raised: nothing is registered.
    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler<nint>? ForegroundWindowChanged;

    public event EventHandler<nint>? MouseCaptured;
#pragma warning restore CS0067

    public bool IsEnabled { get; private set; }

    public Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys) =>
        Task.FromResult<IReadOnlyList<HotkeyAction>>([]);

    public Task SetEnabledAsync(bool enabled)
    {
        IsEnabled = enabled;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
