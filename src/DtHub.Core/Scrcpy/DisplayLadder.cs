namespace DtHub.Core.Scrcpy;

/// <summary>
/// Virtual display resolution suited to a window's size.
///
/// The image is scaled to the window. A display always taken at the
/// screen's resolution would therefore render the game interface tiny
/// in a small window, shrunk by the same amount, and a display that is
/// always small would render it huge and blurry when enlarged. The
/// resolution follows the window.
///
/// In tiers, not down to the pixel: the resolution is fixed when the
/// session opens, and a scale that changes at every relaunch would be
/// confusing. The tier chosen is the first one above the window, so
/// the image is always shrunk, never enlarged, and stays sharp.
/// </summary>
public static class DisplayLadder
{
    /// <summary>
    /// Proposed heights. The gap from one tier to the next is at most
    /// a third of a tier: the scale jump from one relaunch to another
    /// stays small.
    /// </summary>
    private static readonly int[] Heights =
        [540, 720, 900, 1080, 1260, 1440, 1620, 1800, 1980, 2160];

    /// <summary>Fallback resolution when the screen is not known.</summary>
    public const int FallbackWidth = 1920;

    /// <summary>
    /// Height that most encoders accept.
    ///
    /// Video encoders announce a maximum resolution, and it varies
    /// from one device to another: 7680x4320 on a Xiaomi 13T, but only
    /// 1920x1088 on many entry-level or older devices. Beyond that,
    /// the session is refused. It is the only hardware dependency in
    /// the whole mechanism, and it serves as a fallback.
    /// </summary>
    public const int FallbackHeight = 1080;

    /// <summary>
    /// Heights to retry after a refusal, from the most generous to the
    /// safest.
    ///
    /// A single fallback is not enough. 1080 covers encoders capped at
    /// 1920x1088, which is the common case, but not those capped at
    /// 1280x720, found on old entry-level phones and on entry-level
    /// tablets. With a single fallback at 1080, those devices used to
    /// fail without anything ever being retried.
    ///
    /// At most two attempts: each try costs the full wait, and going
    /// any lower than 720 would give an image nobody wants.
    /// </summary>
    public static readonly int[] FallbackHeights = [1080, 720];

    /// <summary>
    /// Fallback resolution below a given height, at the requested
    /// aspect ratio, or <c>null</c> if there is nothing more modest
    /// left to try.
    ///
    /// The aspect ratio is preserved. The fallback used to impose
    /// 16:9, so the window changed shape between the first attempt
    /// and the second on a 21:9 or 16:10 screen.
    /// </summary>
    public static (int Width, int Height)? Below(int height, int aspectWidth, int aspectHeight)
    {
        foreach (var step in FallbackHeights)
        {
            if (step < height)
            {
                return At(step, aspectWidth, aspectHeight);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolution for a given height, at the requested aspect ratio.
    /// An unknown ratio falls back to the 16:9 of the fallback
    /// resolution.
    /// </summary>
    public static (int Width, int Height) At(int height, int aspectWidth, int aspectHeight)
    {
        if (aspectWidth <= 0 || aspectHeight <= 0)
        {
            return (Even(height * FallbackWidth / FallbackHeight), Even(height));
        }

        var aspect = (double)aspectWidth / aspectHeight;

        return (Even((int)Math.Round(height * aspect)), Even(height));
    }

    /// <summary>
    /// Resolution to request for a window whose client area is the
    /// given height, on a given screen.
    ///
    /// The aspect ratio is the screen's: that is the one the window
    /// keeps, and departing from it would leave a band. The chosen
    /// quality can cap the height, which lightens the phone's encoder.
    /// </summary>
    public static (int Width, int Height) For(
        int clientHeight,
        int screenWidth,
        int screenHeight,
        int maximumHeight = int.MaxValue)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return (FallbackWidth, FallbackHeight);
        }

        var aspect = (double)screenWidth / screenHeight;
        var height = Math.Min(Choose(clientHeight, screenHeight), Math.Max(360, maximumHeight));

        return (Even((int)Math.Round(height * aspect)), Even(height));
    }

    /// <summary>
    /// First tier above the window, never exceeding the screen: beyond
    /// that, the phone's encoder would be working for pixels nobody
    /// would see.
    /// </summary>
    private static int Choose(int clientHeight, int screenHeight)
    {
        foreach (var step in Heights)
        {
            if (step >= screenHeight)
            {
                break;
            }

            if (step >= clientHeight)
            {
                return step;
            }
        }

        return screenHeight;
    }

    /// <summary>Side alignment, in pixels.</summary>
    private const int SideAlignment = 8;

    /// <summary>
    /// Brings a side back to what the encoder will actually render.
    ///
    /// Odd-numbered sides are refused, but stopping there was not
    /// enough: the encoder trims down to the next multiple of eight
    /// below, and it does so silently. Recorded in the log, resolution
    /// requested against texture actually rendered by scrcpy:
    ///
    /// <code>
    /// 2560x1440  ->  2560x1440     already aligned
    /// 1920x1080  ->  1920x1080     already aligned
    /// 1576x886   ->  1576x880      six pixels lost
    /// 1426x802   ->  1424x800      two and two
    /// </code>
    ///
    /// The gap is not just cosmetic: the session's aspect ratio is
    /// calculated on the size **requested**, and is then used to
    /// correct the window's shape. Requesting a size that will not be
    /// received amounts to chasing a ratio that exists nowhere.
    /// </summary>
    private static int Even(int value) =>
        Math.Max(SideAlignment, value - (value % SideAlignment));
}
