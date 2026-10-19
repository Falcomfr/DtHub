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
    /// Height a game window is rendered at the least, within its quality and
    /// its screen.
    ///
    /// **Seen on 2026-10-17**: a 960x570 window rendered at 952x536 looked
    /// blurry at its own size, its text a few pixels tall, its colour at half
    /// resolution. Rendered at 1080 and shrunk by the PC, the way "Sharper
    /// image" does, it was sharp at the same bitrate. The interface keeps its
    /// size, the density following the height (ZoomProfile.DpiFor).
    /// </summary>
    public const int MinimumHeight = 1080;

    /// <summary>
    /// Fallback resolution below a given height, at the requested
    /// aspect ratio, or <c>null</c> if there is nothing more modest
    /// left to try.
    ///
    /// The aspect ratio is preserved. The fallback used to impose
    /// 16:9, so the window changed shape between the first attempt
    /// and the second on a 21:9 or 16:10 screen. The width stays within
    /// <see cref="FallbackWidth"/>.
    /// </summary>
    public static (int Width, int Height)? Below(int height, int aspectWidth, int aspectHeight)
    {
        // Within 1920 wide as well: the fallback is there for encoders capped
        // at 1920x1088, and a 20:9 phone's display at 1080 is 2400 wide.
        var widest = aspectWidth > 0 && aspectHeight > 0
            ? (int)(FallbackWidth * (double)aspectHeight / aspectWidth)
            : int.MaxValue;

        foreach (var step in FallbackHeights)
        {
            var capped = Math.Min(step, widest);

            if (capped < height)
            {
                return At(capped, aspectWidth, aspectHeight);
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
    /// The aspect ratio is the PC screen's, or the phone's own,
    /// <paramref name="phoneAspect"/>, for an account that asks for it (see
    /// PhoneScreen). The window keeps it, and the PC screen then only caps
    /// the size. The chosen quality can cap the height, which lightens the
    /// phone's encoder.
    ///
    /// <paramref name="sharpImage"/> asks for twice the window's height.
    /// The video carries colour at half resolution, so red text on grey,
    /// which differs from its background by colour alone, smears in a
    /// window of its own size. Rendered twice as large and shrunk by the
    /// PC, the colour arrives at the window's resolution. The screen and
    /// the tier still cap it.
    ///
    /// <paramref name="minimumHeight"/> renders a small window as a larger
    /// one, see <see cref="MinimumHeight"/>; the screen and the tier cap it too.
    /// </summary>
    public static (int Width, int Height) For(
        int clientHeight,
        int screenWidth,
        int screenHeight,
        int maximumHeight = int.MaxValue,
        bool sharpImage = false,
        double phoneAspect = 0,
        int minimumHeight = 0)
    {
        if (sharpImage)
        {
            clientHeight *= 2;
        }

        clientHeight = Math.Max(clientHeight, minimumHeight);

        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return (FallbackWidth, FallbackHeight);
        }

        var aspect = phoneAspect > 0 ? phoneAspect : (double)screenWidth / screenHeight;

        // Only for the phone's ratio: at the screen's own, dividing back
        // could truncate a pixel off its height.
        var fits = phoneAspect > 0 ? Math.Min(screenHeight, (int)(screenWidth / aspect)) : screenHeight;

        // The width from the aligned height: aligning both on their own
        // drifts the ratio.
        var height = Even(Math.Min(Choose(clientHeight, fits), Ceiling(maximumHeight, phoneAspect)));

        return (Even((int)Math.Round(height * aspect)), height);
    }

    /// <summary>
    /// Height the chosen quality really allows, before alignment.
    ///
    /// A tier names a load, the pixels of its height in 16:9. A longer
    /// phone gets the same pixels at its own ratio, so a lower height:
    /// 20:9 used to get 2400x1080 in the medium tier, a quarter more.
    /// </summary>
    public static int Ceiling(int maximumHeight, double phoneAspect)
    {
        var ceiling = Math.Max(360, maximumHeight);

        return phoneAspect > 16.0 / 9 && ceiling < int.MaxValue
            ? (int)(ceiling * Math.Sqrt(16.0 / 9 / phoneAspect))
            : ceiling;
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
