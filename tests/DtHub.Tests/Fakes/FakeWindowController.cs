using DtHub.Core.Windows;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated desktop: screens, windows, and a record of what was done
/// to them. Lets us verify stacking without manipulating real windows.
/// </summary>
public sealed class FakeWindowController : IWindowController
{
    private readonly List<WindowHandleInfo> _windows = [];
    private readonly Dictionary<nint, ScreenRect> _rects = [];

    public FakeWindowController(params MonitorInfo[] monitors) =>
        Monitors = monitors.Length > 0 ? [.. monitors] : [PrimaryMonitor];

    /// <summary>
    /// 1920x1080 screen with a taskbar, the most common case.
    /// </summary>
    public static readonly MonitorInfo PrimaryMonitor = new()
    {
        DeviceName = @"\\.\DISPLAY1",
        Bounds = new ScreenRect(0, 0, 1920, 1080),
        WorkArea = new ScreenRect(0, 0, 1920, 1040),
        IsPrimary = true,
    };

    public List<MonitorInfo> Monitors { get; }

    /// <summary>Window currently in the foreground.</summary>
    public nint Foreground { get; set; }

    /// <summary>Windows switched to borderless mode.</summary>
    public HashSet<nint> Borderless { get; } = [];

    /// <summary>Order of focus calls, to verify the traversal.</summary>
    public List<nint> FocusCalls { get; } = [];

    /// <summary>Declares a window belonging to a process.</summary>
    public FakeWindowController AddWindow(nint handle, int processId, string title)
    {
        _windows.Add(new WindowHandleInfo(handle, title, processId));
        _rects[handle] = new ScreenRect(0, 0, 400, 400);
        return this;
    }

    /// <summary>Makes a window disappear, as when a session closes.</summary>
    public void RemoveWindow(nint handle)
    {
        _windows.RemoveAll(w => w.Handle == handle);
        _rects.Remove(handle);
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public IReadOnlyList<WindowHandleInfo> FindWindows(int processId) =>
        [.. _windows.Where(w => w.ProcessId == processId)];

    public bool IsWindow(nint handle) => _windows.Exists(w => w.Handle == handle);

    /// <summary>Simulated frame: title bar and borders.</summary>
    public WindowFrame Chrome { get; set; }

    public WindowFrame GetWindowChrome(string? monitorDeviceName) => Chrome;

    public ScreenRect? GetWindowRect(nint handle) =>
        _rects.TryGetValue(handle, out var rect) ? rect : null;

    public ScreenRect? GetClientRect(nint handle) =>
        _rects.TryGetValue(handle, out var rect)
            ? new ScreenRect(0, 0, rect.Width - Chrome.Width, rect.Height - Chrome.Height)
            : null;

    /// <summary>Rectangles placed, in order, to verify relayouts.</summary>
    public List<(nint Handle, ScreenRect Rect)> Moves { get; } = [];

    public void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false)
    {
        if (IsWindow(handle))
        {
            _rects[handle] = rect;
            Moves.Add((handle, rect));
        }
    }

    /// <summary>Titles rewritten, to verify the shortcut callback.</summary>
    public Dictionary<nint, string> Titles { get; } = [];

    public void SetTitle(nint handle, string title) => Titles[handle] = title;

    public void Focus(nint handle)
    {
        FocusCalls.Add(handle);
        Foreground = handle;
    }

    /// <summary>Stacking order, from most recently raised to oldest.</summary>
    public List<nint> RaiseCalls { get; } = [];

    public void Raise(nint handle) => RaiseCalls.Add(handle);

    /// <summary>Windows that were asked to close.</summary>
    public List<nint> CloseRequests { get; } = [];

    public void RequestClose(nint handle) => CloseRequests.Add(handle);

    public int GetWindowProcessId(nint handle) =>
        _windows.FirstOrDefault(w => w.Handle == handle).ProcessId;

    /// <summary>The frame colour asked of each window, last one wins.</summary>
    public Dictionary<nint, int?> FrameColours { get; } = [];

    public void SetFrameColour(nint handle, int? colourRef) => FrameColours[handle] = colourRef;

    public void SetBorderless(nint handle, bool borderless)
    {
        if (borderless)
        {
            Borderless.Add(handle);
        }
        else
        {
            Borderless.Remove(handle);
        }
    }

    public nint GetForegroundWindow() => Foreground;

    /// <summary>Placements kept, per window.</summary>
    public Dictionary<nint, WindowPlacement> Placements { get; } = [];

    public WindowPlacement? GetPlacement(nint handle) =>
        Placements.GetValueOrDefault(handle);

    public bool SetPlacement(nint handle, WindowPlacement placement)
    {
        if (placement is not { IsSized: true })
        {
            return false;
        }

        Placements[handle] = placement;

        return true;
    }

    /// <summary>
    /// The docked windows, by their host frame. Empty at first.
    /// </summary>
    public Dictionary<nint, nint> Docked { get; } = [];

    /// <summary>Hidden windows, to exercise switching between tabs.</summary>
    public HashSet<nint> Hidden { get; } = [];

    public bool Dock(nint child, nint host)
    {
        if (child == 0 || host == 0 || Docked.ContainsKey(child))
        {
            return false;
        }

        Docked[child] = host;

        return true;
    }

    public bool Undock(nint child)
    {
        if (KeyboardFocus == child)
        {
            KeyboardFocus = 0;
        }

        return Docked.Remove(child);
    }

    public bool IsDocked(nint child) => Docked.ContainsKey(child);

    public void SetVisible(nint handle, bool visible)
    {
        if (visible)
        {
            Hidden.Remove(handle);
        }
        else
        {
            Hidden.Add(handle);
        }
    }

    /// <summary>The window holding the keyboard, zero if none.</summary>
    public nint KeyboardFocus { get; private set; }

    /// <summary>Each keyboard hand-off, in order.</summary>
    public List<nint> KeyboardFocusCalls { get; } = [];

    public bool GiveKeyboardFocus(nint child)
    {
        if (child == 0 || !IsWindow(child) || !Docked.ContainsKey(child))
        {
            return false;
        }

        KeyboardFocusCalls.Add(child);
        KeyboardFocus = child;

        return true;
    }
}
