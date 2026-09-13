namespace DtHub.Core.Windows;

/// <summary>
/// Decides whether a remembered placement can be given back to a
/// window.
///
/// A position recorded on a screen that has since been unplugged
/// would send the window into the void: it would be open, present
/// in the taskbar, and invisible. It is then better to let it open
/// at its default place.
///
/// Pure function: it can be checked with no screen or window.
/// </summary>
public static class WindowPlacementCalculator
{
    /// <summary>
    /// How much of the window must be visible to consider it
    /// recoverable, in pixels. Enough to place the cursor on its
    /// title bar and bring it back.
    /// </summary>
    public const int MinimumVisible = 120;

    /// <summary>
    /// True if the requested placement leaves enough of the window
    /// visible on one of the present screens.
    /// </summary>
    public static bool IsReachable(
        WindowPlacement? placement,
        IReadOnlyList<ScreenRect> screens,
        int minimum = MinimumVisible)
    {
        ArgumentNullException.ThrowIfNull(screens);

        if (placement is not { IsSized: true })
        {
            return false;
        }

        var wanted = new ScreenRect(
            placement.Left,
            placement.Top,
            placement.Right - placement.Left,
            placement.Bottom - placement.Top);

        foreach (var screen in screens)
        {
            var shared = wanted.Intersect(screen);

            // Both dimensions matter: a strip one pixel tall across
            // the full width cannot be grabbed any more than a
            // corner can.
            if (shared.Width >= Math.Min(minimum, wanted.Width)
                && shared.Height >= Math.Min(minimum, wanted.Height))
            {
                return true;
            }
        }

        return false;
    }
}
