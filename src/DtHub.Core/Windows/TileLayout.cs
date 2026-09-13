namespace DtHub.Core.Windows;

/// <summary>
/// Splitting the screen into two halves, for side-by-side tiling.
///
/// Pulled out of <see cref="WindowManagerService"/> so the tabbed
/// frame can be tiled like a game window: the service only knows
/// sessions, and the frame is not one. The calculation itself
/// depends on nothing other than the usable area and what is being
/// tiled.
/// </summary>
public static class TileLayout
{
    /// <summary>
    /// The half of the screen that goes to one window.
    ///
    /// The reference window takes the right side, everything else
    /// lands on the left: beyond two windows, the ones on the left
    /// stack up. This is the original rule, and it is worth what a
    /// screen split in two is worth.
    ///
    /// The height follows the source's aspect ratio, applied to the
    /// client area: that is what scrcpy fills, and ignoring it would
    /// leave black bars of the exact width of the frame chrome. With
    /// no known ratio, the half is taken over the full height.
    /// </summary>
    /// <param name="work">Usable area of the targeted screen.</param>
    /// <param name="onRight">True for the reference window.</param>
    /// <param name="aspectRatio">
    /// Aspect ratio of the source, zero if unknown.
    /// </param>
    /// <param name="chrome">
    /// Frame chrome size, borders and bars included.
    /// </param>
    public static ScreenRect Half(
        ScreenRect work,
        bool onRight,
        double aspectRatio,
        (int Width, int Height) chrome)
    {
        var half = work.Width / 2;

        var height = aspectRatio > 0
            ? Math.Min(
                  work.Height,
                  (int)Math.Round((half - chrome.Width) / aspectRatio) + chrome.Height)
            : work.Height;

        return new ScreenRect(
            onRight ? work.X + half : work.X,
            work.Y + ((work.Height - height) / 2),
            half,
            height);
    }
}
