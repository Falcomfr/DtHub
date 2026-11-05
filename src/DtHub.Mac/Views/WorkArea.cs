using Avalonia;
using Avalonia.Controls;

namespace DtHub.Mac.Views;

/// <summary>
/// WPF's SystemParameters.WorkArea for the web windows' first places: the
/// screen the window is on, without the menu bar and the Dock, in the
/// window's own units. Avalonia gives screens in pixels; the PC's sizes are
/// in points, as Width and Height are.
/// </summary>
internal static class WorkArea
{
    /// <summary>The area, or a common Mac screen when none is known yet.</summary>
    public static Rect Of(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if ((window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary) is not { } screen)
        {
            return new Rect(0, 0, 1440, 900);
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        return new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
    }

    /// <summary>Sizes and moves the window, its place given in the area's units.</summary>
    public static void Place(Window window, double left, double top, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Width = width;
        window.Height = height;

        var scale = (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary)?.Scaling ?? 1;
        window.Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
    }

    /// <summary>The window's centre, in the area's units.</summary>
    public static Point Centre(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var scale = (window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary)?.Scaling ?? 1;
        return new Point(
            (window.Position.X / scale) + (window.Bounds.Width / 2),
            (window.Position.Y / scale) + (window.Bounds.Height / 2));
    }
}
