namespace DtHub.Core.Windows;

/// <summary>
/// Thickness of the frame that Windows draws around a window's
/// client area: the left border, the title bar, and what the two
/// add to the width and the height.
///
/// The distinction is not cosmetic. scrcpy sizes **and positions**
/// its window from the inside: giving it the outer corner made it
/// spawn with a border too far left and a title bar too high, and
/// the placement that followed would then reposition it on screen.
/// Measured on the development device: requested at (186, 284),
/// the frame appeared at (175, 239).
/// </summary>
public readonly record struct WindowFrame(int Left, int Top, int Width, int Height)
{
    /// <summary>No frame: borderless window, or full screen.</summary>
    public static WindowFrame None => default;

    /// <summary>
    /// Rectangle to request for the client area so that the
    /// window, frame included, occupies exactly
    /// <paramref name="outer"/>.
    /// </summary>
    public ScreenRect ClientOf(ScreenRect outer) => new(
        outer.X + Left,
        outer.Y + Top,
        Math.Max(1, outer.Width - Width),
        Math.Max(1, outer.Height - Height));
}
