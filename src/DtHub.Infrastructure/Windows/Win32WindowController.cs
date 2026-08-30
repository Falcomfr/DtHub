using System.Runtime.InteropServices;

using DtHub.Core.Windows;

namespace DtHub.Infrastructure.Windows;

/// <summary>
/// Accès aux fenêtres par l'API Windows. Toutes les fonctions sont tolérantes
/// à la disparition d'une fenêtre entre le moment où on la trouve et celui où
/// on agit dessus : une session peut se fermer à tout instant.
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

            // Le fil d'exécution ne nous intéresse pas ; seul le processus
            // propriétaire compte.
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
        // Une fenêtre réduite rend un rectangle en (-32000, -32000). Le
        // mémoriser détruirait la géométrie retenue, et le restaurer placerait
        // la fenêtre hors de tout écran.
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
    /// Encombrement du cadre d'une fenêtre ordinaire, mesuré sans qu'aucune
    /// fenêtre n'existe.
    ///
    /// scrcpy ouvre une fenêtre redimensionnable avec barre de titre, donc le
    /// style le plus courant. La mise à l'échelle de l'écran visé est prise en
    /// compte : sur un écran à 150 pour cent, le cadre est une fois et demie
    /// plus épais.
    /// </summary>
    public (int Width, int Height) GetWindowChrome(string? monitorDeviceName)
    {
        var dpi = DpiOf(monitorDeviceName);

        var frame = new Rect { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };

        if (!AdjustWindowRectExForDpi(ref frame, OverlappedWindow, bMenu: false, 0, dpi))
        {
            return (0, 0);
        }

        return (frame.Right - frame.Left - 1000, frame.Bottom - frame.Top - 1000);
    }

    /// <summary>Points par pouce de l'écran visé, ou ceux du système à défaut.</summary>
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

        // Une fenêtre réduite ignorerait le déplacement : on la restaure
        // d'abord, sans lui voler le focus.
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

        // Sans ce rafraîchissement, la zone non cliente reste dessinée.
        _ = SetWindowPos(handle, 0, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged | SwpNoActivate);
    }

    public nint GetForegroundWindow() => GetForegroundWindowCore();

    private static ScreenRect ToRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    // Constantes de l'API Windows.
    private const uint MonitorPrimary = 0x00000001;
    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
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

    /// <summary>Style d'une fenêtre ordinaire redimensionnable : WS_OVERLAPPEDWINDOW.</summary>
    private const uint OverlappedWindow = 0x00CF0000;

    /// <summary>Densité de référence de Windows, cent pour cent.</summary>
    private const uint DefaultDpi = 96;

    private const uint MonitorDefaultToNearest = 2;

    /// <summary>MDT_EFFECTIVE_DPI : la densité telle que la voit l'application.</summary>
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
