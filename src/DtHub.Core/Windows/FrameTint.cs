namespace DtHub.Core.Windows;

/// <summary>
/// The window border's share of an account's colour.
///
/// The title bar wore the colour first, and across a whole bar it was too
/// bright, "ça pique les yeux"; it went back to a plain dark bar and the
/// colour moved to the one pixel border. At full strength even that caught
/// the eye, said on 2026-10-03, so the border leans toward Windows' dark
/// title bar and keeps less than half of the tint: a little over half was
/// still "à peine trop vif". The same on free windows and on the frame.
/// </summary>
public static class FrameTint
{
    /// <summary>Windows' dark title bar, which the border leans toward.</summary>
    private const int Dark = 0x20;

    /// <summary>The tint's share in the border.</summary>
    private const double BorderShare = 0.45;

    /// <summary>
    /// The border colour for an account colour, both as COLORREF,
    /// 0x00BBGGRR: each channel is mixed the same way, so the order does not
    /// matter here.
    /// </summary>
    public static int Border(int colourRef) =>
        Mix(colourRef & 0xFF) | (Mix((colourRef >> 8) & 0xFF) << 8) | (Mix((colourRef >> 16) & 0xFF) << 16);

    private static int Mix(int channel) =>
        (int)Math.Round(Dark + (BorderShare * (channel - Dark)), MidpointRounding.AwayFromZero);
}
