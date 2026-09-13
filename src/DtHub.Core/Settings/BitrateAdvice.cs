using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Settings;

/// <summary>
/// What a bitrate is worth, once weighed against what it must cover.
/// </summary>
public enum BitrateVerdict
{
    /// <summary>
    /// The image will fall apart as soon as the scene moves.
    /// </summary>
    Insufficient,

    /// <summary>Watchable, but fast movement will show artifacts.</summary>
    Tight,

    /// <summary>The good compromise.</summary>
    Comfortable,

    /// <summary>
    /// Beyond what the eye can make out: network spent for nothing.
    /// </summary>
    Generous,
}

/// <summary>
/// Judges a bitrate against what it must cover: a resolution and a
/// frame rate.
///
/// A bare bitrate means nothing. Sixteen megabits are generous at 720p
/// and miserable at 2160p, and that is exactly the mistake this project
/// fell into: the quality tiers had the bitrate backwards, "maximum"
/// receiving five and a half times fewer bits per pixel than "low".
/// Nobody had noticed, because the numbers taken one by one all seemed
/// reasonable.
///
/// The measure that matters is the <b>bit per pixel per frame</b>, the
/// one the encoder actually receives. The reference values published by
/// YouTube for well-made H.264 all sit around 0.10: 12 Mb/s at 1080p60
/// gives 0.096, 24 at 1440p60 gives 0.108, 53 at 2160p60 gives 0.106.
/// That is where the thresholds come from.
/// </summary>
public static class BitrateAdvice
{
    /// <summary>
    /// What H.265 gives for the same bitrate, compared to H.264.
    ///
    /// At equal quality, H.265 needs about a third fewer bits. Without
    /// accounting for it, the verdict would punish the right choice: a
    /// user who switches to H.265 would see their setting labeled as
    /// merely adequate just after improving it. The value is an
    /// accepted order of magnitude, not a measurement made here, and it
    /// only serves to nuance an assessment.
    /// </summary>
    public const double Hevc = 0.65;

    private const double Insufficient = 0.050;
    private const double Tight = 0.075;
    private const double Generous = 0.160;

    /// <summary>
    /// Reads a setting and returns what is needed to display it as is.
    ///
    /// Outlandish values do not throw: the panel calls this function on
    /// every keystroke, including on a half-erased field.
    /// </summary>
    public static BitrateReading Read(int width, int height, int fps, int kbps, string? codec = null)
    {
        if (width <= 0 || height <= 0 || fps <= 0 || kbps <= 0)
        {
            return new BitrateReading(0, 0, BitrateVerdict.Insufficient, string.Empty);
        }

        var raw = kbps * 1000.0 / ((double)width * height * fps);

        // Weighed against H.264, which is the threshold reference: at
        // equal bitrate, H.265 gives more, and the verdict must
        // reflect that.
        var effective = IsHevc(codec) ? raw / Hevc : raw;

        return new BitrateReading(raw, effective, Judge(raw, codec), Sentence(raw, Judge(raw, codec)));
    }

    /// <summary>
    /// What a chosen detail level will really give, once weighed
    /// against the resolution, the frame rate, and <b>the number of
    /// open windows</b>.
    ///
    /// This last point is specific to this application, and it is what
    /// sets the advice apart from a plain calculation: several open
    /// accounts mean several streams on the same link and the same
    /// encoder. Judging a single stream in isolation would say
    /// "comfortable" while the phone chokes.
    /// </summary>
    /// <param name="windows">
    /// Windows open on the phone. Zero and one give the same count:
    /// what is announced then is what the first one will cost.
    /// </param>
    /// <param name="ceilingKbps">
    /// Ceiling of the profile, which bounds each stream.
    /// </param>
    public static BitratePlan Plan(
        double bitsPerPixel,
        int width,
        int height,
        int fps,
        string? codec,
        int windows,
        int ceilingKbps)
    {
        if (bitsPerPixel <= 0 || width <= 0 || height <= 0 || fps <= 0)
        {
            return new BitratePlan(BitrateVerdict.Insufficient, 0, 0, string.Empty, string.Empty);
        }

        var pixels = (double)width * height * fps;

        var perWindow = Math.Clamp(
            (int)Math.Round(bitsPerPixel * pixels / 1000.0),
            QualityProfile.FloorKbps,
            Math.Max(QualityProfile.FloorKbps, ceilingKbps));

        var count = Math.Max(1, windows);

        // The detail level served can be lower than the one requested:
        // the ceiling trims down large resolutions. That is the one
        // that must be judged, not the one that was checked.
        var served = perWindow * 1000.0 / pixels;
        var verdict = Judge(served, codec);

        return new BitratePlan(
            verdict,
            perWindow,
            perWindow * count,
            Sentence(served, verdict),
            LinkSentence(perWindow, count));
    }

    /// <summary>The verdict's word, as it is displayed.</summary>
    public static string Label(BitrateVerdict verdict) => verdict switch
    {
        BitrateVerdict.Insufficient => Strings.Get("BitrateInsufficient"),
        BitrateVerdict.Tight => Strings.Get("BitrateTight"),
        BitrateVerdict.Comfortable => Strings.Get("BitrateComfortable"),
        _ => Strings.Get("BitrateGenerous"),
    };

    private static bool IsHevc(string? codec) =>
        codec is not null && codec.Trim().Equals("h265", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The verdict, once the detail level is brought back to what
    /// H.264 would have asked for the same output: that is the
    /// threshold reference.
    /// </summary>
    private static BitrateVerdict Judge(double bitsPerPixel, string? codec) =>
        (IsHevc(codec) ? bitsPerPixel / Hevc : bitsPerPixel) switch
        {
            < Insufficient => BitrateVerdict.Insufficient,
            < Tight => BitrateVerdict.Tight,
            <= Generous => BitrateVerdict.Comfortable,
            _ => BitrateVerdict.Generous,
        };

    /// <summary>
    /// What the link will receive. The total only appears from two
    /// windows onward: with just one, repeating it would say nothing
    /// more.
    /// </summary>
    private static string LinkSentence(int perWindowKbps, int windows)
    {
        var each = perWindowKbps / 1000.0;

        // Number formatting follows the country, not the language:
        // "8,4" in France, "8.4" in the United States, and these are
        // two separate Windows settings. Strings.Format takes care of
        // it.
        return windows <= 1
            ? Strings.Format("BitrateLinkOne", Arrondi(each))
            : Strings.Format("BitrateLinkMany", Arrondi(each), Arrondi(each * windows), windows);
    }

    /// <summary>
    /// The sentence shown under the settings. Explicit about locale:
    /// the decimal comma is not a detail when the number holds in
    /// three digits.
    /// </summary>
    private static string Sentence(double bitsPerPixel, BitrateVerdict verdict) =>
        Strings.Format(
            "BitrateSentence",
            bitsPerPixel.ToString("0.000", CultureInfo.CurrentCulture),
            Label(verdict));

    /// <summary>
    /// A megabit to the nearest decimal, in the country's format.
    /// </summary>
    private static string Arrondi(double megabits) =>
        megabits.ToString("0.#", CultureInfo.CurrentCulture);
}

/// <summary>
/// What <see cref="BitrateAdvice.Read"/> returns.
/// </summary>
/// <param name="BitsPerPixel">The raw measurement, as is.</param>
/// <param name="EffectiveBitsPerPixel">
/// The same value, brought back to what H.264 would have asked for
/// this rendering. It is the one that decides the verdict.
/// </param>
/// <param name="Verdict">The assessment.</param>
/// <param name="Summary">
/// The sentence to display, empty if the setting is incomplete.
/// </param>
public readonly record struct BitrateReading(
    double BitsPerPixel,
    double EffectiveBitsPerPixel,
    BitrateVerdict Verdict,
    string Summary);

/// <summary>
/// What <see cref="BitrateAdvice.Plan"/> returns.
/// </summary>
/// <param name="Verdict">
/// The assessment of the detail level actually served.
/// </param>
/// <param name="KbpsPerWindow">The bitrate requested for one window.</param>
/// <param name="TotalKbps">
/// What all the windows will request together.
/// </param>
/// <param name="Summary">
/// The detail level and its verdict, to display.
/// </param>
/// <param name="LinkSummary">What the link will receive, to display.</param>
public readonly record struct BitratePlan(
    BitrateVerdict Verdict,
    int KbpsPerWindow,
    int TotalKbps,
    string Summary,
    string LinkSummary);
