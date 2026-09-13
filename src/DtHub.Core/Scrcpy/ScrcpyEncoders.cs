using System.Globalization;

namespace DtHub.Core.Scrcpy;

/// <summary>A video encoder as the device declares it.</summary>
/// <param name="Codec">The codec it serves, "h264" or "h265".</param>
/// <param name="Name">Its name, to pass to <c>--video-encoder</c>.</param>
/// <param name="Hardware">True if it is hardware.</param>
/// <param name="IsAlias">
/// True if it is only another name for an encoder already listed.
/// </param>
public readonly record struct VideoEncoder(string Codec, string Name, bool Hardware, bool IsAlias);

/// <summary>
/// What the device knows how to encode, and with what.
///
/// A software encoder on a phone gives a choppy image and a device
/// that heats up, for the same work. The question is therefore a real
/// one, and it does not have the same answer everywhere: on the
/// reference device, h264 and h265 each have a hardware encoder,
/// whereas av1 and vp8 only have software.
///
/// **scrcpy does the hardest part.** Its `--list-encoders` output
/// labels each entry itself, recorded character for character on a
/// Xiaomi 13T Pro running Android 16:
///
/// <code>
///     --video-codec=h264 --video-encoder=c2.mtk.avc.encoder     (hw) [vendor]
///     --video-codec=h264 --video-encoder=c2.android.avc.encoder (sw)
/// </code>
///
/// There is therefore nothing to guess and no API query to make: it
/// is enough to read.
/// </summary>
public static class ScrcpyEncoders
{
    private const string CodecMarker = "--video-codec=";
    private const string EncoderMarker = "--video-encoder=";

    /// <summary>
    /// The declared video encoders. Audio encoder lines are excluded
    /// on their own: they do not carry <c>--video-codec=</c>.
    /// </summary>
    public static IReadOnlyList<VideoEncoder> Parse(string? listing)
    {
        if (string.IsNullOrWhiteSpace(listing))
        {
            return [];
        }

        List<VideoEncoder> encoders = [];

        foreach (var line in listing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var codecAt = line.IndexOf(CodecMarker, StringComparison.Ordinal);
            var encoderAt = line.IndexOf(EncoderMarker, StringComparison.Ordinal);

            if (codecAt < 0 || encoderAt < 0)
            {
                continue;
            }

            var codec = Word(line, codecAt + CodecMarker.Length);
            var name = Word(line, encoderAt + EncoderMarker.Length);

            if (codec.Length == 0 || name.Length == 0)
            {
                continue;
            }

            encoders.Add(new VideoEncoder(
                codec,
                name,
                line.Contains("(hw)", StringComparison.OrdinalIgnoreCase),
                line.Contains("(alias for", StringComparison.OrdinalIgnoreCase)));
        }

        return encoders;
    }

    /// <summary>
    /// The encoder to force for this codec, or <c>null</c> when there
    /// is nothing to do.
    ///
    /// **We only force it when it is useful.** A device whose first
    /// listed encoder is already hardware needs nothing: forcing a
    /// name would bring no extra image and would only add a way to
    /// fail. A device that only has software has nothing to force
    /// either, there is no alternative.
    ///
    /// Aliases are excluded: they are other names for an encoder
    /// already listed, and choosing one would change nothing but the
    /// risk.
    /// </summary>
    public static string? Force(IReadOnlyList<VideoEncoder> encoders, string? codec)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        var forCodec = encoders
            .Where(e => !e.IsAlias && Matches(e.Codec, codec))
            .ToList();

        if (forCodec.Count == 0 || forCodec[0].Hardware)
        {
            return null;
        }

        return forCodec.FirstOrDefault(e => e.Hardware).Name is { Length: > 0 } hardware ? hardware : null;
    }

    /// <summary>
    /// True when this codec has no hardware encoder on this device, so
    /// the image will be choppy no matter what we do.
    ///
    /// Returns false when the list is empty: not knowing is not the
    /// same as knowing it is bad, and raising an alarm over ignorance
    /// would be worse than staying silent.
    /// </summary>
    public static bool SoftwareOnly(IReadOnlyList<VideoEncoder> encoders, string? codec)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        var forCodec = encoders.Where(e => Matches(e.Codec, codec)).ToList();

        return forCodec.Count > 0 && !forCodec.Exists(e => e.Hardware);
    }

    /// <summary>
    /// The codecs that have a hardware encoder, in the declared order.
    /// </summary>
    public static IReadOnlyList<string> HardwareCodecs(IReadOnlyList<VideoEncoder> encoders)
    {
        ArgumentNullException.ThrowIfNull(encoders);

        return [.. encoders.Where(e => e.Hardware).Select(e => e.Codec).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool Matches(string codec, string? wanted) =>
        string.Equals(codec, (wanted ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The word that follows a position, up to the next blank.
    /// </summary>
    private static string Word(string line, int start)
    {
        var end = start;

        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        return line[start..end].Trim();
    }
}
