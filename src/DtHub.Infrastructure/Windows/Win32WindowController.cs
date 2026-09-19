using System.Runtime.InteropServices;

using DtHub.Core.Windows;

namespace DtHub.Infrastructure.Windows;

/// <summary>
/// Access to windows through the Windows API. All functions tolerate a
/// window disappearing between the moment it is found and the moment
/// it is acted upon: a session can close at any instant.
/// </summary>
public sealed partial class Win32WindowController : IWindowController
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };

            if (GetMonitorInfo(monitor, ref info))
            {
                monitors.Add(new MonitorInfo
                {
                    DeviceName = info.szDevice.TrimEnd('\0'),
                    Bounds = ToRect(info.rcMonitor),
                    WorkArea = ToRect(info.rcWork),
                    IsPrimary = (info.dwFlags & MonitorPrimary) != 0,
                });
            }

            return true;
        }, 0);

        return monitors;
    }

    public IReadOnlyList<WindowHandleInfo> FindWindows(int processId)
    {
        var windows = new List<WindowHandleInfo>();

        EnumWindows((handle, _unusedData) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            // The execution thread does not interest us; only the owning
            // process matters.
            var thread = GetWindowThreadProcessId(handle, out var owner);
            if (thread == 0 || owner != (uint)processId)
            {
                return true;
            }

            var length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return true;
            }

            var buffer = new char[length + 1];
            var written = GetWindowText(handle, buffer, buffer.Length);

            windows.Add(new WindowHandleInfo(handle, new string(buffer, 0, Math.Max(0, written)), processId));
            return true;
        }, 0);

        return windows;
    }

    public bool IsWindow(nint handle) => handle != 0 && IsWindowCore(handle);

    public int GetWindowProcessId(nint handle)
    {
        if (handle == 0)
        {
            return 0;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);

        return (int)processId;
    }

    public ScreenRect? GetWindowRect(nint handle)
    {
        // A minimized window returns a rectangle at (-32000, -32000).
        // Remembering it would destroy the kept geometry, and restoring
        // it would place the window off any screen.
        if (handle == 0 || IsIconic(handle) || !GetWindowRectCore(handle, out var rect))
        {
            return null;
        }

        return ToRect(rect);
    }

    public ScreenRect? GetClientRect(nint handle)
    {
        if (handle == 0 || !GetClientRectCore(handle, out var rect))
        {
            return null;
        }

        return ToRect(rect);
    }

    /// <summary>
    /// Footprint of an ordinary window's chrome, measured without any
    /// window existing.
    ///
    /// scrcpy opens a resizable window with a title bar, hence the most
    /// common style. The scaling of the targeted screen is taken into
    /// account: on a screen at 150 percent, the chrome is one and a half
    /// times thicker.
    /// </summary>
    public WindowFrame GetWindowChrome(string? monitorDeviceName)
    {
        var dpi = DpiOf(monitorDeviceName);

        var frame = new Rect { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };

        if (!AdjustWindowRectExForDpi(ref frame, OverlappedWindow, bMenu: false, 0, dpi))
        {
            return WindowFrame.None;
        }

        // The returned rectangle overflows the client area: its left and
        // top edges are negative, and their opposite is the thickness
        // being sought.
        return new WindowFrame(
            Left: -frame.Left,
            Top: -frame.Top,
            Width: frame.Right - frame.Left - 1000,
            Height: frame.Bottom - frame.Top - 1000);
    }

    /// <summary>
    /// Dots per inch of the targeted screen, or the system's by default.
    /// </summary>
    private uint DpiOf(string? monitorDeviceName)
    {
        var monitors = GetMonitors();

        var monitor = monitors.FirstOrDefault(
            m => string.Equals(m.DeviceName, monitorDeviceName, StringComparison.Ordinal));

        if (monitor is null && monitors.Count > 0)
        {
            monitor = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        }

        if (monitor is null)
        {
            return DefaultDpi;
        }

        var handle = MonitorFromPoint(
            new Point { X = monitor.Bounds.CenterX, Y = monitor.Bounds.CenterY }, MonitorDefaultToNearest);

        return handle != 0 && GetDpiForMonitor(handle, MonitorDpiEffective, out var x, out _) == 0
            ? x
            : DefaultDpi;
    }

    public void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false)
    {
        if (handle == 0)
        {
            return;
        }

        // A minimized window would ignore the move: it is restored first,
        // without stealing its focus.
        if (IsIconic(handle))
        {
            _ = ShowWindow(handle, ShowWindowRestore);
        }

        var flags = SwpNoActivate;
        if (!bringToFront)
        {
            flags |= SwpNoZOrder;
        }

        _ = SetWindowPos(handle, bringToFront ? HwndTop : 0, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    public void SetTitle(nint handle, string title)
    {
        if (handle != 0 && !string.IsNullOrEmpty(title))
        {
            _ = SetWindowTextW(handle, title);
        }
    }

    public void Raise(nint handle)
    {
        if (handle == 0 || !IsWindowCore(handle))
        {
            return;
        }

        _ = SetWindowPos(handle, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void RequestClose(nint handle)
    {
        if (handle == 0 || !IsWindowCore(handle))
        {
            return;
        }

        _ = PostMessage(handle, WmClose, 0, 0);
    }

    public void Focus(nint handle)
    {
        if (handle == 0)
        {
            return;
        }

        if (IsIconic(handle))
        {
            _ = ShowWindow(handle, ShowWindowRestore);
        }

        _ = SetForegroundWindow(handle);
    }

    /// <summary>Frame colour, Windows 11 build 22000 and later.</summary>
    private const int BorderColour = 34;

    /// <summary>Title bar colour, same vintage.</summary>
    private const int CaptionColour = 35;

    /// <summary>Hands the colour back to the system.</summary>
    private const uint ColourDefault = 0xFFFFFFFF;

    public void SetFrameColour(nint handle, int? colourRef)
    {
        if (handle == 0)
        {
            return;
        }

        // COLORREF, so 0x00BBGGRR and not RGB: getting the order wrong
        // gives a plausible colour, which is the worst kind of bug to
        // find. The default sentinel hands the frame back to Windows.
        var value = colourRef is { } c ? (uint)c : ColourDefault;

        // Both, deliberately. The border is a single pixel, which is
        // nothing across a room; the title bar is what tells two
        // windows apart at a glance. Neither is fatal: an older Windows
        // answers E_INVALIDARG and the rest of the application does not
        // care.
        _ = DwmSetWindowAttribute(handle, BorderColour, ref value, sizeof(uint));
        _ = DwmSetWindowAttribute(handle, CaptionColour, ref value, sizeof(uint));
    }

    public void SetBorderless(nint handle, bool borderless)
    {
        if (handle == 0)
        {
            return;
        }

        var style = GetWindowLong(handle, GwlStyle);
        if (style == 0)
        {
            return;
        }

        var updated = borderless
            ? style & ~(WsCaption | WsThickFrame)
            : style | WsCaption | WsThickFrame;

        if (updated == style)
        {
            return;
        }

        _ = SetWindowLong(handle, GwlStyle, updated);

        // Without this refresh, the non-client area stays drawn.
        _ = SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpNoActivate);
    }

    /// <summary>
    /// What a window was before being docked: enough to restore it
    /// identically.
    /// </summary>
    private readonly record struct DockedWindow(nint Parent, int Style, ScreenRect Rect);

    private readonly Dictionary<nint, DockedWindow> _docked = [];

    public bool Dock(nint child, nint host)
    {
        if (child == 0 || host == 0 || !IsWindow(child) || !IsWindow(host) || _docked.ContainsKey(child))
        {
            return false;
        }

        var style = GetWindowLong(child, GwlStyle);
        var rect = GetWindowRect(child);

        if (style == 0 || rect is not { } before)
        {
            return false;
        }

        // Kept before touching anything at all: a window restored with a
        // guessed style would no longer behave like the others.
        _docked[child] = new DockedWindow(GetParent(child), style, before);

        // The frame and title bar leave, WS_CHILD arrives. The order
        // matters: changing the style before attaching avoids a flicker
        // of a frameless main window.
        _ = SetWindowLong(child, GwlStyle, (style & ~(WsPopup | WsCaption | WsThickFrame)) | WsChild);
        _ = SetParent(child, host);
        _ = SetWindowPos(child, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpNoActivate);

        return true;
    }

    public bool Undock(nint child)
    {
        if (!_docked.Remove(child, out var before))
        {
            return false;
        }

        if (!IsWindow(child))
        {
            return false;
        }

        _ = SetParent(child, before.Parent);
        _ = SetWindowLong(child, GwlStyle, before.Style);

        // The geometry is restored last: set before the style, it would
        // be overridden by the frame's return.
        MoveWindow(child, before.Rect);
        _ = SetWindowPos(child, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpNoActivate);

        return true;
    }

    public bool IsDocked(nint child) => _docked.ContainsKey(child);

    public void SetVisible(nint handle, bool visible)
    {
        if (handle != 0 && IsWindow(handle))
        {
            _ = ShowWindow(handle, visible ? SwShowNoActivate : SwHide);
        }
    }

    /// <summary>
    /// Gives the keyboard back to a docked window.
    ///
    /// A single call is enough, and a measurement is what says so:
    /// compared side by side, the input queue of a free scrcpy window
    /// and ours are separate, whereas once the window is docked they
    /// become only one, without anything here having joined them. It is
    /// SetParent that attaches them. All that remained was to designate
    /// the window: the focus, for its part, stayed on the WPF window,
    /// and every keystroke with it.
    ///
    /// The shortest path turns out to be the safest one.
    /// <c>AttachThreadInput</c> would reset the state of the keys to
    /// zero on every call, losing the Ctrl of a Ctrl+V in progress, and
    /// undoing it would cut what the docking depends on.
    /// </summary>
    public bool GiveKeyboardFocus(nint child)
    {
        if (child == 0 || !IsWindow(child) || !_docked.ContainsKey(child))
        {
            return false;
        }

        _ = SetFocus(child);

        // We reread rather than trust the returned value: SetFocus
        // returns NULL both if it fails and if no window had focus, and
        // the second case is ours on the first docking. GetFocus queries
        // the calling thread's queue, the very one docking joined to the
        // game's: if the answer is the docked window, the keyboard goes
        // to it.
        return GetFocus() == child;
    }

    public nint GetForegroundWindow() => GetForegroundWindowCore();

    private static ScreenRect ToRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    // Windows API constants.
    private const uint MonitorPrimary = 0x00000001;
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int ShowWindowRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoActivate = 0x0010;
    private const nint HwndTop = 0;
    private const uint WmClose = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, nint rect, nint data);

    private delegate bool EnumWindowsProc(nint handle, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    /// <summary>
    /// Style of an ordinary resizable window: WS_OVERLAPPEDWINDOW.
    /// </summary>
    private const uint OverlappedWindow = 0x00CF0000;

    /// <summary>Windows' reference density, one hundred percent.</summary>
    private const uint DefaultDpi = 96;

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>
    /// MDT_EFFECTIVE_DPI: the density as the application sees it.
    /// </summary>
    private const int MonitorDpiEffective = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(
        ref Rect rect,
        uint style,
        [MarshalAs(UnmanagedType.Bool)] bool bMenu,
        uint exStyle,
        uint dpi);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowCore(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint handle, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowTextW(nint handle, string title);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectCore(nint handle, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRectCore(nint handle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint handle);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint GetForegroundWindowCore();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint handle, int command);

    /// <summary>
    /// Where a window is, in desktop pixels.
    ///
    /// The rectangle comes from GetWindowRect and not from
    /// WINDOWPLACEMENT's "normal" rectangle: the latter is expressed in
    /// the main screen's density, so that a window placed on a second
    /// screen at one hundred fifty percent would come back at two
    /// thirds of its size. Measured: 780 x 1140 on saving, 570 x 761 on
    /// reading back.
    ///
    /// From WINDOWPLACEMENT only the display state is kept, which does
    /// not depend on any scale.
    /// </summary>
    public WindowPlacement? GetPlacement(nint handle)
    {
        if (handle == 0 || !GetWindowRectCore(handle, out var rect))
        {
            return null;
        }

        var raw = new WindowPlacementRaw { length = Marshal.SizeOf<WindowPlacementRaw>() };

        return new WindowPlacement
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom,
            Maximized = GetWindowPlacement(handle, ref raw) && raw.showCmd == ShowMaximized,
        };
    }

    /// <summary>
    /// Puts a window back where it was, provided a screen is still
    /// there. Otherwise it keeps its default place, which is better
    /// than opening out of view.
    /// </summary>
    public bool SetPlacement(nint handle, WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        if (handle == 0
            || !WindowPlacementCalculator.IsReachable(
                placement,
                [.. GetMonitors().Select(m => m.WorkArea)]))
        {
            return false;
        }

        _ = SetWindowPos(
            handle,
            0,
            placement.Left,
            placement.Top,
            placement.Right - placement.Left,
            placement.Bottom - placement.Top,
            SwpNoZOrder | SwpNoActivate);

        if (placement.Maximized)
        {
            _ = ShowWindow(handle, ShowMaximized);
        }

        return true;
    }

    private const int ShowMaximized = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementRaw
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rect rcNormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint handle, ref WindowPlacementRaw placement);

    // DllImport and not LibraryImport: the latter wants unsafe code for
    // a parameter passed by reference, which this project does not
    // allow, and it is the form the other calls here already use.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window, int attribute, ref uint value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
