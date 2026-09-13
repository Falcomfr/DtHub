using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// Computes the position and size of windows. Pure functions: the
/// layout can be verified entirely without a real screen or window.
/// </summary>
public static class WindowLayoutCalculator
{
    /// <summary>
    /// Rectangle of a game window: a share of the usable area, at the
    /// Android virtual screen's aspect ratio, stuck to the requested
    /// position.
    /// </summary>
    /// <param name="monitor">The target screen.</param>
    /// <param name="sizePercent">Share of the usable area, in percent.</param>
    /// <param name="sourceAspectRatio">
    /// Width over height ratio of the source. Zero fills without any
    /// shape constraint.
    /// </param>
    /// <param name="anchor">Position within the grid.</param>
    public static ScreenRect Calculate(
        MonitorInfo monitor,
        int sizePercent,
        double sourceAspectRatio,
        WindowAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var fraction = Math.Clamp(sizePercent, 20, 100) / 100.0;

        var maxWidth = Math.Max(1, (int)Math.Round(work.Width * fraction));
        var maxHeight = Math.Max(1, (int)Math.Round(work.Height * fraction));

        var (width, height) = sourceAspectRatio > 0
            ? FitToAspect(maxWidth, maxHeight, sourceAspectRatio)
            : (maxWidth, maxHeight);

        return Place(work, width, height, anchor);
    }

    /// <summary>
    /// Sticks a rectangle of a given size to a grid position, inside
    /// an area.
    /// </summary>
    public static ScreenRect Place(ScreenRect area, int width, int height, WindowAnchor anchor)
    {
        var w = Math.Clamp(width, 1, Math.Max(1, area.Width));
        var h = Math.Clamp(height, 1, Math.Max(1, area.Height));

        var left = area.X;
        var centerX = area.X + ((area.Width - w) / 2);
        var right = area.Right - w;

        var top = area.Y;
        var middleY = area.Y + ((area.Height - h) / 2);
        var bottom = area.Bottom - h;

        var (x, y) = anchor switch
        {
            WindowAnchor.TopLeft => (left, top),
            WindowAnchor.TopCenter => (centerX, top),
            WindowAnchor.TopRight => (right, top),
            WindowAnchor.MiddleLeft => (left, middleY),
            WindowAnchor.Center => (centerX, middleY),
            WindowAnchor.MiddleRight => (right, middleY),
            WindowAnchor.BottomLeft => (left, bottom),
            WindowAnchor.BottomCenter => (centerX, bottom),
            WindowAnchor.BottomRight => (right, bottom),
            _ => (centerX, middleY),
        };

        return new ScreenRect(x, y, w, h);
    }

    /// <summary>
    /// Largest size that respects the requested ratio and fits within
    /// the given bounds.
    /// </summary>
    public static (int Width, int Height) FitToAspect(int maxWidth, int maxHeight, double aspectRatio)
    {
        if (aspectRatio <= 0)
        {
            return (maxWidth, maxHeight);
        }

        var widthFromHeight = (int)Math.Round(maxHeight * aspectRatio);

        return widthFromHeight <= maxWidth
            ? (Math.Max(1, widthFromHeight), maxHeight)
            : (maxWidth, Math.Max(1, (int)Math.Round(maxWidth / aspectRatio)));
    }

    /// <summary>
    /// Screen designated by its name in the settings, falling back to
    /// the primary screen if the chosen one was unplugged.
    /// </summary>
    public static MonitorInfo ChooseMonitor(IReadOnlyList<MonitorInfo> monitors, string? preferredDeviceName)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException(Strings.Get("NoScreenDetected"));
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceName))
        {
            var preferred = monitors.FirstOrDefault(
                m => string.Equals(m.DeviceName, preferredDeviceName, StringComparison.Ordinal));

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Screen containing the given point, or the primary screen
    /// otherwise.
    /// </summary>
    public static MonitorInfo ChooseMonitor(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new InvalidOperationException(Strings.Get("NoScreenDetected"));
        }

        return monitors.FirstOrDefault(m => m.Bounds.Contains(x, y))
               ?? monitors.FirstOrDefault(m => m.IsPrimary)
               ?? monitors[0];
    }

    /// <summary>
    /// Rectangle transposed proportionally from one screen to another.
    /// </summary>
    public static ScreenRect Rescale(ScreenRect rect, ScreenRect from, ScreenRect to)
    {
        if (from.IsEmpty || to.IsEmpty)
        {
            return rect;
        }

        var scaleX = (double)to.Width / from.Width;
        var scaleY = (double)to.Height / from.Height;

        return new ScreenRect(
            to.X + (int)Math.Round((rect.X - from.X) * scaleX),
            to.Y + (int)Math.Round((rect.Y - from.Y) * scaleY),
            Math.Max(1, (int)Math.Round(rect.Width * scaleX)),
            Math.Max(1, (int)Math.Round(rect.Height * scaleY)));
    }

    /// <summary>
    /// Brings a rectangle entirely within an area, shrinking it if it
    /// is too large to fit.
    /// </summary>
    public static ScreenRect ClampInto(ScreenRect rect, ScreenRect area)
    {
        if (area.IsEmpty)
        {
            return rect;
        }

        var width = Math.Min(Math.Max(1, rect.Width), area.Width);
        var height = Math.Min(Math.Max(1, rect.Height), area.Height);

        return new ScreenRect(
            Math.Clamp(rect.X, area.X, area.Right - width),
            Math.Clamp(rect.Y, area.Y, area.Bottom - height),
            width,
            height);
    }

    /// <summary>
    /// Usable rectangle for a remembered geometry, or <c>null</c> when
    /// nothing sensible can be drawn from it: the caller then falls
    /// back to the placement computed from the anchor.
    ///
    /// The screen is recognized by its bounds as much as by its name,
    /// because that name is positional: unplugging a screen renumbers
    /// the following ones, and a window could end up restored onto
    /// the wrong one.
    /// </summary>
    public static ScreenRect? RestoreRemembered(
        ScreenRect remembered,
        string? monitorDeviceName,
        ScreenRect monitorBounds,
        IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (remembered.IsEmpty || monitors.Count == 0)
        {
            return null;
        }

        var named = monitors.FirstOrDefault(
            m => string.Equals(m.DeviceName, monitorDeviceName, StringComparison.Ordinal));

        // Same screen, same bounds: the rectangle still holds, provided
        // it actually falls on it. A window that was minimized at the
        // time of capture returns a rectangle at (-32000, -32000),
        // which must absolutely not be restored.
        if (named is not null && named.Bounds == monitorBounds && IsMostlyOn(remembered, named))
        {
            return remembered;
        }

        // Resolution or layout changed: we transpose proportionally.
        if (named is not null && named.Bounds != monitorBounds && !monitorBounds.IsEmpty)
        {
            return ClampInto(Rescale(remembered, monitorBounds, named.Bounds), UsableArea(named));
        }

        // The original screen has disappeared or changed rank. If a
        // screen still carries most of the window, it stays there.
        var host = monitors
            .OrderByDescending(m => m.Bounds.Intersect(remembered).Area)
            .First();

        return IsMostlyOn(remembered, host) ? ClampInto(remembered, UsableArea(host)) : null;
    }

    /// <summary>
    /// True if at least half the rectangle falls on this screen.
    /// </summary>
    private static bool IsMostlyOn(ScreenRect rect, MonitorInfo monitor) =>
        monitor.Bounds.Intersect(rect).Area * 2 >= rect.Area;

    private static ScreenRect UsableArea(MonitorInfo monitor) =>
        monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;

    /// <summary>
    /// Index of the next session, wrapping around. Works for two
    /// instances as well as ten, and starts over from the beginning
    /// when the current session is no longer in the list.
    /// </summary>
    public static int NextIndex(int count, int currentIndex)
    {
        if (count <= 0)
        {
            return -1;
        }

        return currentIndex is < 0 or int.MaxValue || currentIndex >= count - 1
            ? 0
            : currentIndex + 1;
    }

    /// <summary>Index of the previous session, wrapping around.</summary>
    public static int PreviousIndex(int count, int currentIndex)
    {
        if (count <= 0)
        {
            return -1;
        }

        return currentIndex <= 0 ? count - 1 : currentIndex - 1;
    }
}
