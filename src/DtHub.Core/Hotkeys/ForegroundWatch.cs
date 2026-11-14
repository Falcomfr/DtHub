namespace DtHub.Core.Hotkeys;

/// <summary>
/// The last window seen in the foreground, by event or by reading.
///
/// Windows does not always announce a change: a tabbed frame made active while
/// DT Hub was not in front (opening or reopening its tabs), then clicked, goes
/// to the foreground without EVENT_SYSTEM_FOREGROUND (measured 2026-11-14). The
/// hotkeys stayed asleep and Ctrl+Tab dead until one clicked elsewhere and back.
/// </summary>
public sealed class ForegroundWatch
{
    private nint _last;

    /// <summary>True when the window differs from the last one seen.</summary>
    public bool Saw(nint window)
    {
        if (window == _last)
        {
            return false;
        }

        _last = window;
        return true;
    }
}
