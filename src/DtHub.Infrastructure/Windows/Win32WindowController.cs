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

    public ScreenRect? GetWindowRect(nint handle)
    {
        if (handle == 0 || !GetWindowRectCore(handle, out var rect))
        {
            return null;
        }

        return ToRect(rect);
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint handle, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRectCore(nint handle, out Rect rect);

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
