using DtHub.Core.Storage;

namespace DtHub.Core.Settings;

/// <summary>
/// Trade-off between image detail and the machine's load.
///
/// A tier removed from the code falls back to the next one up, never down:
/// nobody should see their image degrade without having asked for it.
/// </summary>
[JsonFallback(Maximum)]
public enum StreamQuality
{
    /// <summary>The lightest: for modest machines and phones.</summary>
    Low,

    /// <summary>Default setting.</summary>
    Medium,

    /// <summary>
    /// No mercy for the phone: 1440p, bitrate and frame rate at their maximum.
    /// Requires a recent device and, over Wi-Fi, a network that keeps up.
    ///
    /// The resolution used to be unbounded, and therefore followed the
    /// window's size, that is, the PC's screen. The same tier then cost twice
    /// as much on a 4K screen as on a 1080p one, without anything saying so. A
    /// tier must denote a load, not inherit the screen's.
    ///
    /// Three tiers, not four: between two neighbours too close together,
    /// nobody knows which to choose, and the difference is not visible.
    /// </summary>
    Maximum,

    /// <summary>
    /// Values chosen by hand.
    ///
    /// This is not a fourth rung on the ladder, and it therefore does not
    /// contradict the rule of three tiers stated just above: it is a detour
    /// off the road. The three tiers remain the ordinary route, the one where
    /// there is nothing to know; this one serves whoever already knows what
    /// they want.
    ///
    /// Added last, and never to be reordered: old settings files still carry
    /// these values as a number.
    /// </summary>
    Custom,
}

/// <summary>
/// The four values of the custom tier.
///
/// DPI is not one of them: it is already adjusted under the name "distance in
/// the game", which says what it does for the player rather than what it is.
/// Offering it here as well would make two settings for a single thing.
/// </summary>
public sealed record CustomQuality
{
    /// <summary>
    /// Starting values: those of the medium tier, the default one.
    /// </summary>
    public static readonly CustomQuality Default = new();

    /// <summary>Ceiling on the display's height, as for the tiers.</summary>
    public int MaximumDisplayHeight { get; init; } = 1080;

    public int MaxFps { get; init; } = 60;

    /// <summary>
    /// Image detail, in bits per pixel per frame.
    ///
    /// And not a bitrate in megabits, unlike what interfaces that drive only a
    /// single mirror offer. Here the display's resolution follows the window's
    /// size: an absolute bitrate would feast generously on a small window and
    /// starve a large one, exactly what the rest of the code has learned not
    /// to do anymore. Detail, on the other hand, keeps its meaning at any
    /// size, and it is already the unit used by the three tiers.
    /// </summary>
    public double BitsPerPixel { get; init; } = 0.09;

    /// <summary>
    /// H.264 or H.265, and nothing else.
    ///
    /// These are not the only codecs scrcpy accepts, but they are the only
    /// ones the reference phone encodes in hardware: as found with
    /// <c>scrcpy --list-encoders</c>, AV1 and VP8 only have a software encoder
    /// there. Offering them would be a trap, software encoding at sixty frames
    /// per second costing far more than it delivers.
    /// </summary>
    public string VideoCodec { get; init; } = "h264";

    /// <summary>
    /// Returns a copy with workable values. The settings file can be edited by
    /// hand: it gets corrected rather than refusing to start.
    /// </summary>
    public CustomQuality Sanitized() => this with
    {
        MaximumDisplayHeight = Math.Clamp(MaximumDisplayHeight, 240, 7680),
        MaxFps = Math.Clamp(MaxFps, 1, 240),

        // Wide bounds: 0.03 turns an image to mush, 0.30 goes far beyond what
        // the eye can distinguish. Between the two, it is the choice of
        // whoever adjusts it.
        BitsPerPixel = Math.Clamp(BitsPerPixel, 0.03, 0.30),
        VideoCodec = string.Equals(VideoCodec?.Trim(), "h265", StringComparison.OrdinalIgnoreCase)
            ? "h265"
            : "h264",
    };
}

/// <summary>
/// What each quality changes, in a single place.
///
/// The heaviest cost is not the image: it is querying the phone, which lists
/// the installed profiles and packages. The low quality therefore lightens
/// both, without which it would only relieve half of the problem.
///
/// The three tiers bound the resolution, and not just the lowest one. Without
/// a resolution bound, medium and maximum would have been indistinguishable.
///
/// <para>
/// The bitrate is not fixed per tier: it is calculated. A fixed bitrate per
/// tier gave the scale backwards, measured in bits per pixel per frame, which
/// is what an encoder actually receives:
/// </para>
///
/// <code>
/// Low       1280x720 at  30 fps,  2500 kb/s  ->  0.090 bpp
/// Medium   1920x1080 at  60 fps,  6000 kb/s  ->  0.048 bpp
/// Maximum  3840x2160 at 120 fps, 16000 kb/s  ->  0.016 bpp
/// </code>
///
/// <para>
/// The resolution and frame rate were multiplied by fifteen from bottom to
/// top, the bitrate by only six: the "maximum" tier received five and a half
/// times fewer bits per pixel than the "low" tier, and therefore rendered a
/// coarser image in motion. This is the opposite of what it promises.
/// </para>
///
/// <para>
/// The bitrate now follows the resolution and frame rate actually retained, at
/// a given number of bits per pixel per frame. The benchmark for well-made
/// H.264 sits around 0.10 bpp at any resolution: YouTube asks for 12 Mb/s at
/// 1080p60, 24 at 1440p60 and 53 at 2160p60, that is 0.096, 0.108 and 0.106.
/// </para>
/// </summary>
public sealed record QualityProfile(
    int MaxFps,
    double BitsPerPixel,
    int CeilingKbps,
    int MaximumDisplayHeight,
    TimeSpan DevicePoll,
    TimeSpan InstanceRediscovery,
    TimeSpan WindowWatch)
{
    /// <summary>
    /// Bitrate floor. Below this threshold, a small window would render mush
    /// that nobody wants, and the saving would not be felt on anything.
    /// </summary>
    public const int FloorKbps = 1500;

    /// <summary>
    /// Bitrate to request for a given resolution, in kb/s.
    ///
    /// The ceiling matters as much as the calculation, and for a reason that
    /// cannot be guessed from the code alone: two open accounts are two
    /// streams on the same link. A phone on 2.4 GHz Wi-Fi advertises one
    /// hundred and forty-four megabits of raw link, of which about half is
    /// achieved in practice. Asking for fifty megabits per session would not
    /// give a magnificent image, but losses and stutter.
    /// </summary>
    public int BitrateFor(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Math.Clamp(FloorKbps, FloorKbps, CeilingKbps);
        }

        var wanted = BitsPerPixel * width * height * MaxFps / 1000.0;

        return Math.Clamp((int)Math.Round(wanted), FloorKbps, CeilingKbps);
    }

    /// <summary>
    /// Settings for a given quality.
    ///
    /// <paramref name="custom"/> is only read for
    /// <see cref="StreamQuality.Custom"/>, and then takes its default values
    /// if missing: a settings file that announces the custom tier without
    /// carrying its values must yield a session that opens, not an exception.
    /// </summary>
    public static QualityProfile For(StreamQuality quality, CustomQuality? custom = null) => quality switch
    {
        StreamQuality.Custom => Personalised(custom ?? CustomQuality.Default),

        // The light tier is light through resolution and frame rate, not
        // through a degraded image: capping bits per pixel would give blur
        // without relieving either the phone or the PC.
        StreamQuality.Low => new QualityProfile(
            MaxFps: 30,
            BitsPerPixel: 0.08,
            CeilingKbps: 4000,
            MaximumDisplayHeight: 720,
            DevicePoll: TimeSpan.FromSeconds(6),
            InstanceRediscovery: TimeSpan.FromSeconds(60),
            WindowWatch: TimeSpan.FromSeconds(1)),

        // Sixty frames and not one hundred and twenty: measured on the game,
        // it renders thirty-eight. The one hundred and twenty only served to
        // halve the bits granted to each frame that actually exists.
        StreamQuality.Maximum => new QualityProfile(
            MaxFps: 60,
            BitsPerPixel: 0.11,
            CeilingKbps: 25000,

            // 1440p, and not the window's resolution: see
            // StreamQuality.Maximum. Above the medium tier, which bounds at
            // 1080p, and the gap stays clear.
            MaximumDisplayHeight: 1440,
            DevicePoll: TimeSpan.FromSeconds(2),
            InstanceRediscovery: TimeSpan.FromSeconds(15),
            WindowWatch: TimeSpan.FromMilliseconds(500)),

        _ => new QualityProfile(
            MaxFps: 60,
            BitsPerPixel: 0.09,
            CeilingKbps: 12000,
            MaximumDisplayHeight: 1080,
            DevicePoll: TimeSpan.FromSeconds(3),
            InstanceRediscovery: TimeSpan.FromSeconds(30),
            WindowWatch: TimeSpan.FromMilliseconds(500)),
    };

    /// <summary>
    /// The profile built from values chosen by hand.
    ///
    /// The polling rates are those of the medium tier: they weigh on the
    /// phone, not on the image, and therefore do not concern whoever is
    /// adjusting their video. Bits per pixel are of no use there, since the
    /// bitrate is given directly.
    /// </summary>
    private static QualityProfile Personalised(CustomQuality custom)
    {
        var wanted = custom.Sanitized();

        return For(StreamQuality.Medium) with
        {
            MaxFps = wanted.MaxFps,
            MaximumDisplayHeight = wanted.MaximumDisplayHeight,
            BitsPerPixel = wanted.BitsPerPixel,

            // The ceiling stays, and it is that of the highest tier. It
            // protects not from the user but from the link: several open
            // accounts are several streams on the same Wi-Fi, and finely
            // tuning one window says nothing about what the others will ask
            // for at the same time.
            CeilingKbps = For(StreamQuality.Maximum).CeilingKbps,
        };
    }
}
