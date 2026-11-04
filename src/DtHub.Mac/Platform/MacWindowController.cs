using DtHub.Core.Windows;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's window contract on macOS, so that the shared layout code
/// (grid, full screen, order) drives scrcpy's windows here too. A handle is
/// a CGWindowID; positions are points from the main screen's top left.
///
/// What macOS does not offer is done without, never faked: another
/// application's window cannot be re-parented into ours (no
/// <see cref="Dock"/>), nor retitled, nor tinted.
/// </summary>
internal sealed class MacWindowController : IWindowController
{
    /// <summary>
    /// The title bar macOS adds above an ordinary window, measured on
    /// macOS 27 with scrcpy 5.0.1: a 434-point picture gives a 466-point
    /// window. scrcpy places and sizes its window from the inside, as on
    /// the PC, so this is the whole frame.
    /// </summary>
    public const int TitleBarHeight = 32;

    /// <summary>
    /// How long a hide waits before minimizing: the launcher hides a window
    /// right before handing it to the tabbed frame, and minimizing then
    /// restoring it a moment later fails halfway through the Dock's
    /// animation.
    /// </summary>
    private static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Each docked window, with the frame that keeps it.</summary>
    private readonly Dictionary<nint, nint> _docked = [];

    /// <summary>
    /// What each tabbed frame covers, its strip and the games below it:
    /// the frame's own window is only the strip.
    /// </summary>
    private readonly Dictionary<nint, ScreenRect> _hosts = [];
    private readonly Dictionary<nint, CancellationTokenSource> _pendingHides = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<MonitorInfo> GetMonitors() =>
    [
        .. MacNative.Screens().Select((screen, index) => new MonitorInfo
        {
            DeviceName = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Bounds = Rect(screen.Frame),
            WorkArea = Rect(screen.Visible),
            IsPrimary = index == 0,
        }),
    ];

    public IReadOnlyList<WindowHandleInfo> FindWindows(int processId) =>
    [
        .. MacNative.WindowsOf(processId)
            .Select(id => new WindowHandleInfo((nint)id, MacNative.Title(id) ?? string.Empty, processId)),
    ];

    public bool IsWindow(nint handle) => handle != 0 && MacNative.dthub_window_exists(handle);

    public WindowPlacement? GetPlacement(nint handle) =>
        GetWindowRect(handle) is { } rect
            ? new WindowPlacement
            {
                Left = rect.X,
                Top = rect.Y,
                Right = rect.X + rect.Width,
                Bottom = rect.Y + rect.Height,
            }
            : null;

    /// <summary>
    /// Like Windows, brings a rectangle saved on a screen since unplugged
    /// back onto the main one rather than into the void.
    /// </summary>
    public bool SetPlacement(nint handle, WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (!placement.IsSized)
        {
            return false;
        }

        var rect = new ScreenRect(
            placement.Left, placement.Top, placement.Right - placement.Left, placement.Bottom - placement.Top);
        var monitors = GetMonitors();

        if (monitors.Count > 0)
        {
            rect = OntoScreen(rect, monitors);
        }

        return MacNative.dthub_move_window(handle, rect.X, rect.Y, rect.Width, rect.Height);
    }

    public int GetWindowProcessId(nint handle) => handle == 0 ? 0 : MacNative.dthub_window_pid(handle);

    /// <summary>
    /// A tabbed frame's rectangle is the whole of it, strip and game area,
    /// as on the PC where the game is inside it: the launcher saves, sizes
    /// and places the frame from this rectangle.
    /// </summary>
    public ScreenRect? GetWindowRect(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        lock (_gate)
        {
            if (_hosts.TryGetValue(handle, out var outer))
            {
                return outer;
            }
        }

        return MacNative.Frame(handle) is { } frame ? Rect(frame) : null;
    }

    /// <summary>
    /// The tabbed frame says what it covers, or null once it is gone. Read
    /// back by <see cref="GetWindowRect"/>, from any thread.
    /// </summary>
    public void SetHostRect(nint host, ScreenRect? outer)
    {
        if (host == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (outer is { Width: > 0, Height: > 0 } rect)
            {
                _hosts[host] = rect;
            }
            else
            {
                _ = _hosts.Remove(host);
            }
        }
    }

    public ScreenRect? GetClientRect(nint handle) =>
        GetWindowRect(handle) is { } rect
            ? new ScreenRect(0, 0, rect.Width, Math.Max(1, rect.Height - TitleBarHeight))
            : null;

    public void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false)
    {
        _ = MacNative.dthub_move_window(handle, rect.X, rect.Y, rect.Width, rect.Height);

        if (bringToFront && !IsDocked(handle))
        {
            _ = MacNative.dthub_raise_window(handle, activate: false);
        }
    }

    public WindowFrame GetWindowChrome(string? monitorDeviceName) => new(0, TitleBarHeight, 0, TitleBarHeight);

    /// <summary>
    /// A docked window is brought forward by its frame only, through
    /// <see cref="ShowDocked"/>: the launcher raises every window in list
    /// order, which on the PC leaves a hidden tab hidden, but here would
    /// put the last tab in front of the selected one.
    /// </summary>
    public void Focus(nint handle)
    {
        if (IsDocked(handle))
        {
            return;
        }

        ShowDocked(handle);
    }

    /// <summary>The frame's own way to bring its selected tab forward.</summary>
    public void ShowDocked(nint handle)
    {
        CancelHide(handle);

        if (MacNative.dthub_is_minimized(handle))
        {
            _ = MacNative.dthub_set_minimized(handle, minimized: false);
        }

        _ = MacNative.dthub_raise_window(handle, activate: true);
    }

    public void RequestClose(nint handle) => _ = MacNative.dthub_close_window(handle);

    public void Raise(nint handle)
    {
        if (!IsDocked(handle))
        {
            _ = MacNative.dthub_raise_window(handle, activate: false);
        }
    }

    /// <summary>Another application's title cannot be changed on macOS.</summary>
    public void SetTitle(nint handle, string title)
    {
    }

    /// <summary>
    /// A window's frame cannot be taken away from outside on macOS: scrcpy
    /// is asked for a borderless window when it starts instead.
    /// </summary>
    public void SetBorderless(nint handle, bool borderless)
    {
    }

    /// <summary>macOS draws no tintable border.</summary>
    public void SetFrameColour(nint handle, int? colourRef)
    {
    }

    /// <summary>
    /// A docked game in front counts as its frame, as on the PC where it
    /// is a child of the frame and the frame is the foreground window: the
    /// launcher compares the foreground with the frame's handle.
    /// </summary>
    public nint GetForegroundWindow()
    {
        var front = (nint)MacNative.dthub_front_window();

        lock (_gate)
        {
            return _docked.TryGetValue(front, out var host) ? host : front;
        }
    }

    /// <summary>
    /// macOS cannot house another application's window in ours: a docked
    /// window here is one the tabbed frame keeps stacked below its strip.
    /// It is only remembered, and left visible; the frame places it.
    /// </summary>
    public bool Dock(nint child, nint host)
    {
        if (child == 0 || host == 0 || !IsWindow(child))
        {
            return false;
        }

        lock (_gate)
        {
            _docked[child] = host;
        }

        CancelHide(child);

        if (MacNative.dthub_is_minimized(child))
        {
            _ = MacNative.dthub_set_minimized(child, minimized: false);
        }

        return true;
    }

    public bool Undock(nint child)
    {
        lock (_gate)
        {
            return _docked.Remove(child);
        }
    }

    public bool IsDocked(nint child)
    {
        lock (_gate)
        {
            return _docked.ContainsKey(child);
        }
    }

    /// <summary>
    /// Hidden is minimized into the Dock, as close as macOS comes, after a
    /// short wait that a show or a dock cancels. A docked window is left to
    /// its frame, which brings the selected tab's to the front.
    /// </summary>
    public void SetVisible(nint handle, bool visible)
    {
        if (handle == 0 || IsDocked(handle))
        {
            return;
        }

        CancelHide(handle);

        if (visible)
        {
            if (MacNative.dthub_is_minimized(handle))
            {
                _ = MacNative.dthub_set_minimized(handle, minimized: false);
            }

            return;
        }

        var pending = new CancellationTokenSource();

        lock (_gate)
        {
            _pendingHides[handle] = pending;
        }

        _ = Task.Delay(HideDelay, pending.Token).ContinueWith(
            waited =>
            {
                // Under the gate, so that a dock arriving now either comes
                // first (and the window is left alone) or finds it
                // minimized and restores it.
                lock (_gate)
                {
                    if (!_pendingHides.Remove(handle, out var current) || current != pending || _docked.ContainsKey(handle))
                    {
                        return;
                    }

                    _ = MacNative.dthub_set_minimized(handle, minimized: true);
                }

                pending.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    public bool GiveKeyboardFocus(nint child) => false;

    public bool IsModifierKeyDown() => MacNative.dthub_modifier_down();

    /// <summary>
    /// The stuck Ctrl the PC works around comes from Windows' input queues;
    /// nothing to release on macOS.
    /// </summary>
    public void ReleaseModifierKeys(nint handle, bool alt)
    {
    }

    private void CancelHide(nint handle)
    {
        CancellationTokenSource? pending;

        lock (_gate)
        {
            _ = _pendingHides.Remove(handle, out pending);
        }

        if (pending is not null)
        {
            pending.Cancel();
            pending.Dispose();
        }
    }

    private static ScreenRect Rect(double[] values) =>
        new((int)Math.Round(values[0]), (int)Math.Round(values[1]), (int)Math.Round(values[2]), (int)Math.Round(values[3]));

    private static ScreenRect Rect((double X, double Y, double Width, double Height) frame) =>
        new((int)Math.Round(frame.X), (int)Math.Round(frame.Y), (int)Math.Round(frame.Width), (int)Math.Round(frame.Height));

    /// <summary>
    /// As Windows does for a restored placement: the window comes back
    /// whole inside the usable area of the screen it overlaps most, or of
    /// the main screen if its own is gone. A panel saved half off the edge
    /// came back half off the edge.
    /// </summary>
    internal static ScreenRect OntoScreen(ScreenRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        var screen = monitors
            .Select(m => (Monitor: m, Shared: Shared(m.Bounds, rect)))
            .OrderByDescending(p => p.Shared)
            .First();
        var work = screen.Shared > 0 ? screen.Monitor.WorkArea : monitors[0].WorkArea;

        var width = Math.Min(rect.Width, work.Width);
        var height = Math.Min(rect.Height, work.Height);
        var x = Math.Clamp(rect.X, work.X, work.X + work.Width - width);
        var y = Math.Clamp(rect.Y, work.Y, work.Y + work.Height - height);

        return new ScreenRect(x, y, width, height);
    }

    private static long Shared(ScreenRect a, ScreenRect b) =>
        (long)Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X))
        * Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));

    private static bool Overlaps(ScreenRect a, ScreenRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
