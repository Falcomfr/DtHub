using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;

namespace DtHub.Core.Settings;

/// <summary>
/// Apparent distance in the game: more or less terrain visible, at
/// equal window size.
///
/// The fallback is "close", not the original setting: the only tier
/// ever removed is "very close", and it had been decided it would
/// merge into this one, which took over its value. It is this
/// fallback that applies the decision, since a migration can no
/// longer do it: the tolerant converter has already replaced the
/// unknown value by the time the migration runs.
/// </summary>
[JsonFallback(Close)]
public enum GameZoom
{
    /// <summary>
    /// The most terrain possible, at the edge of legibility.
    /// </summary>
    Widest,

    /// <summary>Lots of terrain, a small interface.</summary>
    Wide,

    /// <summary>Original setting.</summary>
    Normal,

    /// <summary>The least terrain possible, the largest interface.</summary>
    Close,
}

/// <summary>
/// Translates a zoom into a display density.
///
/// Android expresses layouts in density-independent points: a
/// resolution of 1080 pixels at 240 dpi makes 720 points tall, and
/// it is this number of points, not the number of pixels, that
/// decides the size of the game's interface and the portion of
/// terrain visible. Adjusting the density therefore changes the
/// apparent distance without touching the sharpness of the image.
///
/// The density is computed from the resolution in use, rather than
/// fixed once and for all: the resolution follows the window's size,
/// and a constant density would have made the zoom vary with it,
/// which was the original flaw. At a constant point height, a small
/// window now shows the same thing as a large one, just smaller.
/// </summary>
public static class ZoomProfile
{
    /// <summary>
    /// Height of the layout, in density-independent points.
    ///
    /// The normal value, 720 points, is the one a 1080-pixel display
    /// at 240 dpi gave until now: the original setting stays the
    /// original setting, and it is around it that the others are
    /// placed.
    ///
    /// Both ends go as far as the mechanism allows, so much so that
    /// the last step, from "normal" to "close", is wider than the
    /// others. Four tiers whose two ends are useful are worth more
    /// than five where two look alike.
    /// </summary>
    public static int LayoutHeightFor(GameZoom zoom) => zoom switch
    {
        GameZoom.Widest => 1120,
        GameZoom.Wide => 900,
        GameZoom.Close => 460,
        _ => 720,
    };

    /// <summary>
    /// Density to request for a given resolution. Clamped to the
    /// values Android accepts, otherwise the display is refused.
    /// </summary>
    public static int DpiFor(int displayHeight, GameZoom zoom)
    {
        var layout = LayoutHeightFor(zoom);

        if (displayHeight <= 0 || layout <= 0)
        {
            return 240;
        }

        // 160 dpi is, by Android's definition, the density where one
        // point equals one pixel.
        //
        // The bounds are those of ScrcpyOptions, not twins of them:
        // the value passes back through them before reaching scrcpy,
        // and two different ceilings used to silently clip anything
        // that fell between the two. This ceiling lets the closest
        // tier keep its promise up to a window 2300 pixels tall.
        return Math.Clamp(
            (int)Math.Round(displayHeight * 160.0 / layout),
            ScrcpyOptions.MinDisplayDpi,
            ScrcpyOptions.MaxDisplayDpi);
    }
}
