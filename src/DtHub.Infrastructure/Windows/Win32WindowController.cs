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
    public WindowFrame GetWindowChrome(string? monitorDeviceName)
    {
        var dpi = DpiOf(monitorDeviceName);

        var frame = new Rect { Left = 0, Top = 0, Right = 1000, Bottom = 1000 };

        if (!AdjustWindowRectExForDpi(ref frame, OverlappedWindow, bMenu: false, 0, dpi))
        {
            return WindowFrame.None;
        }

        // Le rectangle rendu déborde du client : ses bords gauche et haut sont
        // négatifs, et leur opposé est l'épaisseur cherchée.
        return new WindowFrame(
            Left: -frame.Left,
            Top: -frame.Top,
            Width: frame.Right - frame.Left - 1000,
            Height: frame.Bottom - frame.Top - 1000);
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

    /// <summary>
    /// Ce qu'une fenêtre était avant d'être logée : de quoi la rendre à
    /// l'identique.
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

        // Retenu avant de toucher à quoi que ce soit : une fenêtre rendue avec
        // un style deviné ne se comporterait plus comme les autres.
        _docked[child] = new DockedWindow(GetParent(child), style, before);

        // Le cadre et la barre de titre partent, WS_CHILD arrive. L'ordre
        // compte : changer le style avant d'attacher évite un clignotement de
        // fenêtre principale sans cadre.
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

        // La géométrie est rendue en dernier : posée avant le style, elle
        // serait reprise par le retour du cadre.
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
    /// Rend le clavier à une fenêtre logée.
    ///
    /// Un seul appel suffit, et c'est une mesure qui le dit : comparées côte
    /// à côte, la file d'entrée d'une fenêtre scrcpy libre et la nôtre sont
    /// séparées, alors qu'une fois la fenêtre arrimée elles n'en font plus
    /// qu'une, sans que rien ici ne les ait jointes. C'est SetParent qui les
    /// attache. Il ne restait donc qu'à désigner la fenêtre : le focus, lui,
    /// demeurait sur la fenêtre WPF, et toutes les frappes avec lui.
    ///
    /// Le chemin le plus court se trouve être le plus sûr.
    /// <c>AttachThreadInput</c> remettrait l'état des touches à zéro à chaque
    /// appel, perdant le Ctrl d'un Ctrl+V en cours, et le défaire couperait
    /// ce dont l'arrimage dépend.
    /// </summary>
    public bool GiveKeyboardFocus(nint child)
    {
        if (child == 0 || !IsWindow(child) || !_docked.ContainsKey(child))
        {
            return false;
        }

        _ = SetFocus(child);

        // On relit plutôt que de croire la valeur rendue : SetFocus rend NULL
        // aussi bien s'il échoue que si aucune fenêtre n'avait le focus, et le
        // second cas est le nôtre au premier arrimage. GetFocus interroge la
        // file du fil appelant, celle-là même que l'arrimage a jointe à celle
        // du jeu : si la réponse est la fenêtre logée, le clavier lui va.
        return GetFocus() == child;
    }

    public nint GetForegroundWindow() => GetForegroundWindowCore();

    private static ScreenRect ToRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    // Constantes de l'API Windows.
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
    /// Où se trouve une fenêtre, en pixels du bureau.
    ///
    /// Le rectangle vient de GetWindowRect et non du rectangle « normal » de
    /// WINDOWPLACEMENT : celui-ci est exprimé dans la densité de l'écran
    /// principal, si bien qu'une fenêtre posée sur un second écran à cent
    /// cinquante pour cent revenait à deux tiers de sa taille. Mesuré :
    /// 780 x 1140 à l'enregistrement, 570 x 761 à la relecture.
    ///
    /// De WINDOWPLACEMENT on ne garde que l'état d'affichage, qui ne dépend
    /// d'aucune échelle.
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
    /// Remet une fenêtre où elle était, si tant est qu'un écran s'y trouve
    /// encore. Sinon elle garde sa place par défaut, ce qui vaut mieux que de
    /// s'ouvrir hors de vue.
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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
