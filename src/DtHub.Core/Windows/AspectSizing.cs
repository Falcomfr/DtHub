namespace DtHub.Core.Windows;

/// <summary>
/// The shape rule for a window being resized with the mouse: which
/// edge drives what, and which one does not move.
///
/// Here rather than in the window itself: this is a calculation, and
/// a calculation can be verified without opening any interface. The
/// window simply wires it to the message Windows sends it during the
/// resize.
///
/// Correcting during the resize rather than after: correcting after
/// the fact made the bottom edge inert, since the height was
/// immediately recalculated from the width, and the window appeared
/// to resist the mouse.
/// </summary>
public static class AspectSizing
{
    /// <summary>
    /// The edge being dragged, as Windows designates it in WM_SIZING.
    /// </summary>
    public const int Left = 1;

    public const int Right = 2;
    public const int Top = 3;
    public const int TopLeft = 4;
    public const int TopRight = 5;
    public const int Bottom = 6;
    public const int BottomLeft = 7;
    public const int BottomRight = 8;

    /// <summary>
    /// Brings the proposed rectangle back to the desired shape.
    /// </summary>
    /// <param name="proposed">
    /// What Windows proposes, based on the mouse.
    /// </param>
    /// <param name="edge">The edge being dragged.</param>
    /// <param name="aspect">Width to height ratio of the image to fit.</param>
    /// <param name="chrome">What the frame takes up around it.</param>
    /// <param name="minimumWidth">Width below which we do not go.</param>
    public static ScreenRect Constrain(
        ScreenRect proposed,
        int edge,
        double aspect,
        (int Width, int Height) chrome,
        int minimumWidth)
    {
        if (aspect <= 0)
        {
            return proposed;
        }

        // Dragging the top or the bottom drives the width: it is the
        // height that the mouse just fixed, and the width that must
        // follow. The left edge does not move, otherwise the window
        // would slide sideways while being resized.
        if (edge is Top or Bottom)
        {
            var large = Math.Max(
                minimumWidth,
                (int)Math.Round((proposed.Height - chrome.Height) * aspect) + chrome.Width);

            return proposed with { Width = large };
        }

        // Everything else, sides and corners, drives the height from
        // the width.
        var width = Math.Max(minimumWidth, proposed.Width);
        var height = (int)Math.Round((width - chrome.Width) / aspect) + chrome.Height;

        // The edge opposite the one being dragged stays where it is.
        // Dragging a top corner therefore keeps the bottom in place,
        // and vice versa.
        return edge is TopLeft or TopRight
            ? proposed with
            {
                Y = proposed.Bottom - height,
                Width = width,
                Height = height,
            }
            : proposed with { Width = width, Height = height };
    }
}
