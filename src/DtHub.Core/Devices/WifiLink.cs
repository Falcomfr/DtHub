using System.Globalization;

namespace DtHub.Core.Devices;

/// <summary>
/// What the phone says about its Wi-Fi link.
///
/// Read via <c>cmd wifi status</c>, which costs less than half a
/// second and a few hundred bytes, whereas <c>dumpsys wifi</c> returns
/// tens of thousands for the same figures.
///
/// Why read the link rather than measure a throughput: the
/// measurement was tried. Five eight-megabyte transfers over the same
/// link, with nothing else running, returned 7.7 then 11.8, 21.0,
/// 23.6 and 20.5 Mb/s. From single to triple from one instant to the
/// next. A single probe at startup would therefore have frozen the
/// quality on a roll of the dice, and a probe long enough to be
/// reliable would have cost several seconds at every launch. What the
/// link itself announces, however, is stable and free.
/// </summary>
/// <param name="LinkSpeedMbps">
/// Speed announced in the phone-to-PC direction.
/// </param>
/// <param name="FrequencyMhz">
/// Channel frequency, which gives the band.
/// </param>
/// <param name="Standard">Negotiated standard, as-is: "11n", "11ac"…</param>
/// <param name="Rssi">Received power in dBm, negative.</param>
/// <param name="RetryShare">
/// Share of retransmitted frames, between 0 and 1.
/// </param>
public sealed record WifiLink(
    int LinkSpeedMbps,
    int FrequencyMhz,
    string Standard,
    int Rssi,
    double RetryShare)
{
    /// <summary>
    /// The crowded band, shared with the neighbors and microwave
    /// ovens.
    /// </summary>
    public bool Is24GHz => FrequencyMhz is >= 2400 and < 2500;

    /// <summary>
    /// Reads the state returned by <c>cmd wifi status</c>.
    ///
    /// Returns <c>null</c> as soon as anything essential is missing:
    /// Wi-Fi off, device on USB, output from an Android version that
    /// names things differently. Knowing nothing is an ordinary case,
    /// not a fault, and the caller knows how to do without it.
    /// </summary>
    public static WifiLink? Parse(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        // "Tx Link speed" and not "Link speed": this is the direction
        // that carries the video. The leading comma excludes "Max
        // Supported Tx Link speed", which announces the hardware's
        // ceiling and not the current link.
        var speed = Number(status, ", Tx Link speed: ") ?? Number(status, "Link speed: ");
        var frequency = Number(status, "Frequency: ");

        if (speed is not > 0 || frequency is not > 0)
        {
            return null;
        }

        var success = Number(status, "successfulTxPackets: ") ?? 0;
        var retried = Number(status, "retriedTxPackets: ") ?? 0;

        return new WifiLink(
            speed.Value,
            frequency.Value,
            Text(status, "Wi-Fi standard: ") ?? "?",
            Number(status, "RSSI: ") ?? 0,
            success + retried > 0 ? retried / (double)(success + retried) : 0);
    }

    /// <summary>
    /// The first integer that follows a label, sign included.
    /// </summary>
    private static int? Number(string text, string label)
    {
        var at = text.IndexOf(label, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + label.Length;
        var end = start;

        if (end < text.Length && text[end] == '-')
        {
            end++;
        }

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return int.TryParse(
            text.AsSpan(start, end - start),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    /// <summary>The word that follows a label, up to the comma.</summary>
    private static string? Text(string text, string label)
    {
        var at = text.IndexOf(label, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var start = at + label.Length;
        var end = text.IndexOf(',', start);

        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }
}
