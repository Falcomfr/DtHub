namespace DtHub.Core.Hotkeys;

/// <summary>A session window, by its handle and its process.</summary>
public readonly record struct SessionWindow(nint Handle, int ProcessId);

/// <summary>
/// Decides whether the hotkeys stay armed, based on the foreground
/// window.
///
/// This is the only thing that keeps <c>RegisterHotKey</c> from
/// being global, and the README makes it a promise: "only the
/// combinations you configured are ever intercepted, and only while
/// a DT Hub window is focused". The rule used to live in the middle
/// of an asynchronous event handler, on a background thread, with no
/// test whatsoever: a regression would have hijacked Ctrl+Tab and
/// Ctrl+R system wide, browser included, without anything turning
/// red and without anyone being able to link the symptom back to DT
/// Hub.
/// </summary>
public static class HotkeyScope
{
    /// <summary>
    /// True if the foreground window is ours.
    ///
    /// A session is recognized by its handle or by its process. The
    /// process matters: the handle of a freshly reopened session is
    /// not resolved yet, and the hotkeys would then believe
    /// themselves outside their own territory until you clicked
    /// elsewhere and then back on a game window.
    /// </summary>
    /// <param name="foreground">Handle of the foreground window.</param>
    /// <param name="owner">Process that owns it, or zero if unknown.</param>
    /// <param name="sessions">The open game windows.</param>
    /// <param name="ours">
    /// True if this is a window of the application itself:
    /// configurator, guides, linked page, or tabbed frame.
    /// </param>
    public static bool Holds(
        nint foreground,
        int owner,
        IEnumerable<SessionWindow> sessions,
        bool ours)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        if (ours)
        {
            return true;
        }

        return sessions.Any(s => s.Handle == foreground || (owner != 0 && s.ProcessId == owner));
    }
}
