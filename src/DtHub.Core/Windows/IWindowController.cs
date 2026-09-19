namespace DtHub.Core.Windows;

/// <summary>A top-level window belonging to a process.</summary>
public readonly record struct WindowHandleInfo(nint Handle, string Title, int ProcessId);

/// <summary>
/// Access to desktop windows. Isolated behind an interface so that the
/// layout logic stays testable without manipulating real windows.
/// </summary>
public interface IWindowController
{
    /// <summary>Connected screens, with their usable area.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>
    /// Visible top-level windows belonging to a process.
    /// </summary>
    IReadOnlyList<WindowHandleInfo> FindWindows(int processId);

    /// <summary>
    /// True if the handle still designates an existing window.
    /// </summary>
    bool IsWindow(nint handle);

    /// <summary>
    /// Where a window is, as Windows remembers it. Returns
    /// <c>null</c> if the window no longer exists.
    /// </summary>
    WindowPlacement? GetPlacement(nint handle);

    /// <summary>
    /// Puts a window back where it was.
    ///
    /// Windows takes care of bringing it back onto a screen that is
    /// present: a rectangle saved on a screen since unplugged does not
    /// send the window into the void, unlike a position set by hand.
    /// </summary>
    bool SetPlacement(nint handle, WindowPlacement placement);

    /// <summary>
    /// Owning process of a window, or zero if it has disappeared.
    ///
    /// Used to recognize our windows without depending on the handle we
    /// have kept: that of a freshly reopened session is not yet resolved,
    /// and the shortcuts would then believe themselves to be away from
    /// home.
    /// </summary>
    int GetWindowProcessId(nint handle);

    /// <summary>
    /// Current position and size, or <c>null</c> if the window has
    /// disappeared.
    /// </summary>
    ScreenRect? GetWindowRect(nint handle);

    /// <summary>
    /// Size of the client area, excluding title bar and borders. This is
    /// the one scrcpy fills: computing the ratio on the outer rectangle
    /// would leave black bars.
    /// </summary>
    ScreenRect? GetClientRect(nint handle);

    /// <summary>Moves and resizes a window.</summary>
    void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false);

    /// <summary>
    /// Chrome of an ordinary window on the given screen: borders and
    /// title bar, in both thickness and corner offset.
    ///
    /// It must be known before any window exists, in order to ask scrcpy
    /// for a display of the exact size of the client area. The game
    /// freezes its layout height at initialization: fixing it afterward
    /// does not make up for anything. The offset serves at the same
    /// moment and for the same reason: scrcpy also positions its window
    /// from the inside.
    /// </summary>
    WindowFrame GetWindowChrome(string? monitorDeviceName);

    /// <summary>
    /// Brings a window to the foreground and gives it keyboard focus.
    /// </summary>
    void Focus(nint handle);

    /// <summary>
    /// Politely requests the closing of a window, as a click on its close
    /// button would.
    ///
    /// This is what allows scrcpy to notify its server before leaving.
    /// Killing the client was enough as long as the connection was over
    /// USB; on a Wi-Fi connection, the server does not immediately see
    /// the broken socket, survives on the phone and keeps its virtual
    /// display open.
    /// </summary>
    void RequestClose(nint handle);

    /// <summary>
    /// Brings a window back to the top of the stack without giving it
    /// focus.
    ///
    /// This is what allows the order of the list to be followed by the
    /// order of the windows, hence by that of Alt+Tab, without tearing
    /// the keyboard away from the window where the user is currently
    /// playing.
    /// </summary>
    void Raise(nint handle);

    /// <summary>
    /// Changes a window's title. The shortcut reminder appears in it, and
    /// must follow a change made in the editor: scrcpy only sets its
    /// title at startup.
    /// </summary>
    void SetTitle(nint handle, string title);

    /// <summary>
    /// Removes or restores the border, for borderless full-screen mode.
    /// </summary>
    void SetBorderless(nint handle, bool borderless);

    /// <summary>
    /// Tints a window's frame and title bar, or gives them back the
    /// system's own colour with <c>null</c>.
    ///
    /// The window belongs to scrcpy and not to us, which was the open
    /// question: `build/sonde-bordure` settled it by reading the frame's
    /// pixels before and after, on a real game window. Windows 11 only,
    /// and a window stripped of its frame for full screen has nothing
    /// left to tint; both cases do nothing rather than fail.
    /// </summary>
    void SetFrameColour(nint handle, int? colourRef);

    /// <summary>Handle of the active window, across all processes.</summary>
    nint GetForegroundWindow();

    /// <summary>
    /// Docks a window inside another, like a tab in its frame.
    ///
    /// The window loses its frame and becomes a child: it no longer
    /// appears in the taskbar, no longer aligns on its own, and follows
    /// its host. Measured on a scrcpy window, it keeps rendering the
    /// picture and Windows still routes the pointer to it. The keyboard,
    /// however, does not follow on its own: <see cref="GiveKeyboardFocus"/>
    /// is needed.
    ///
    /// The original state is kept by the implementation, so that
    /// <see cref="Undock"/> can restore it exactly. Recomputing it would
    /// leave a window that no longer behaves like the others.
    /// </summary>
    /// <returns>False if the window or the host does not exist.</returns>
    bool Dock(nint child, nint host);

    /// <summary>
    /// Takes a window back out of its frame and restores its previous
    /// state, style, parent and geometry included. No effect if it was
    /// not docked.
    /// </summary>
    bool Undock(nint child);

    /// <summary>True if this window is currently docked in a frame.</summary>
    bool IsDocked(nint child);

    /// <summary>
    /// Shows or hides a docked window, to switch from one tab to another.
    /// </summary>
    void SetVisible(nint handle, bool visible);

    /// <summary>
    /// Gives the keyboard back to a docked window.
    ///
    /// <see cref="Dock"/> makes it the child of a frame held by another
    /// process. Windows then joins the two input queues, so that the
    /// keyboard becomes reachable; only the focus stays on the host
    /// window, and nothing that is typed reaches the game, paste included
    /// since scrcpy pastes by typing. This is the only gesture that was
    /// missing, and tabbed mode has lived without it until now: validated
    /// on the picture and on the mouse, never on the keyboard.
    /// </summary>
    /// <returns>
    /// False if the window has disappeared or is not docked.
    /// </returns>
    bool GiveKeyboardFocus(nint child);
}
