namespace DtHub.Core.Windows;

/// <summary>
/// Tells game windows that Ctrl, Shift and Alt are up.
///
/// **The fault, measured on 2026-09-27.** A shortcut such as Ctrl+Tab is
/// typed in a game window: that window gets the Ctrl press, DT Hub takes
/// the Tab and moves to the next window, and the Ctrl release goes there.
/// The window left behind keeps Ctrl down for as long as nothing presses it
/// again. Coming back to it by a click, a tab or Alt+Tab, every click was
/// then a Ctrl+click, which scrcpy turns into a two finger pinch: the map
/// zoomed on a drag, and the character no longer moved. Pressing and
/// letting go of Ctrl in that window freed it, and so did a release posted
/// by DT Hub.
///
/// **Alt, measured on 2026-10-03.** The same with Alt+Tab: the window got
/// the Left Alt press, the release went to the next program, and scrcpy,
/// whose shortcut key Left Alt is, kept every keystroke for itself. Nothing
/// reached the chat until Alt was pressed and let go in the window. Not in a
/// window whose mouse is captured: there Left Alt toggles the capture, and a
/// stray release could grab the mouse.
/// </summary>
public sealed class ModifierKeyRelease
{
    private readonly IWindowController _windows;

    public ModifierKeyRelease(IWindowController windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        _windows = windows;
    }

    /// <summary>
    /// After a shortcut: every game window, even though the key is still
    /// held, since holding it is how the shortcut was typed. The window
    /// left behind needs its release now or never.
    /// </summary>
    /// <param name="windows">The game windows.</param>
    /// <param name="capturingMouse">Those whose mouse is captured, which keep their Alt.</param>
    public void Release(IEnumerable<nint> windows, IReadOnlySet<nint>? capturingMouse = null)
    {
        ArgumentNullException.ThrowIfNull(windows);

        foreach (var window in windows.Where(w => w != 0).Distinct())
        {
            _windows.ReleaseModifierKeys(window, alt: capturingMouse?.Contains(window) != true);
        }
    }

    /// <summary>
    /// When one of ours comes to the front: only if nothing holds Ctrl,
    /// Shift or Alt. Arrived by Ctrl+Tab or Alt+Tab with the key still down,
    /// the window will get the real release when the player lets go.
    /// </summary>
    /// <returns>True if the releases were sent.</returns>
    public bool ReleaseUnlessHeld(IEnumerable<nint> windows, IReadOnlySet<nint>? capturingMouse = null)
    {
        if (_windows.IsModifierKeyDown())
        {
            return false;
        }

        Release(windows, capturingMouse);

        return true;
    }
}
