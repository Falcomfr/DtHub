namespace DtHub.Core.IPhone;

/// <summary>
/// Which iOS accounts keep their tab in the frame, and which of those wait
/// for their game, as an Android account waiting for its phone does.
/// </summary>
public static class IosTabs
{
    /// <summary>
    /// <paramref name="windows"/>: each iOS account's window, its state, and
    /// whether its handle still lives. <c>Shown</c> keeps the window docked;
    /// <c>Waiting</c> keeps the tab with the waiting view: device unplugged, a
    /// failed stream about to start again, or a window being replaced by a new one.
    /// </summary>
    public static (IReadOnlyList<string> Shown, IReadOnlyList<string> Waiting) Split(
        IEnumerable<(string Key, IosWindowState? State, bool Live)> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        List<string> shown = [];
        List<string> waiting = [];

        foreach (var (key, state, live) in windows)
        {
            if (live)
            {
                shown.Add(key);
            }

            if (state is IosWindowState.Unplugged or IosWindowState.Failed
                || (!live && state is IosWindowState.Opening or IosWindowState.Open))
            {
                waiting.Add(key);
            }
        }

        return (shown, waiting);
    }
}
