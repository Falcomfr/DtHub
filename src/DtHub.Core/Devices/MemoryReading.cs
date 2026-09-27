using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>How tight Android says its memory is.</summary>
public enum MemoryPressure
{
    Normal = 0,
    Moderate = 1,
    Low = 2,
    Critical = 3,
}

/// <summary>
/// The phone's memory: Android's verdict on it, and what is still
/// available.
///
/// **Why it exists.** On 2026-09-27 a session lagged harder and harder
/// until one window went black, and DT Hub was suspected. It was not:
/// the phone, three days without a restart and four gigabytes into swap,
/// spent the last half hour starting and killing background apps, and
/// its own system calls ran slow. Nothing on screen said so. This is what
/// now does.
///
/// **Android's verdict, not a threshold of ours.** <c>am memory-factor
/// show</c> answers the level Android itself trims applications by. The
/// available figure alone would cry wolf: a phone keeps its memory full
/// of cache on purpose, and hands it back when asked.
/// </summary>
/// <param name="Level">The verdict, or the fallback from the share available.</param>
/// <param name="AvailableBytes">What Linux reports as available, cache it can drop included.</param>
/// <param name="TotalBytes">The phone's memory.</param>
public sealed partial record MemoryReading(MemoryPressure Level, long AvailableBytes, long TotalBytes)
{
    /// <summary>
    /// Share available below which, lacking Android's verdict, the memory
    /// is taken for low. Only Android 11 lacks it.
    /// </summary>
    public const double LowShare = 0.10;

    /// <summary>The same, for critical.</summary>
    public const double CriticalShare = 0.05;

    /// <summary>Available memory in gigabytes, one decimal.</summary>
    public double AvailableGigabytes => Math.Round(AvailableBytes / (1024.0 * 1024 * 1024), 1);

    /// <summary>
    /// True from low up. Moderate is where Android sits often and for long
    /// without anything lagging: naming it would cry wolf.
    /// </summary>
    public bool IsLow => Level >= MemoryPressure.Low;

    /// <summary>
    /// Reads the answer of <c>am memory-factor show</c> and the lines of
    /// <c>/proc/meminfo</c>. Returns <c>null</c> without the total and the
    /// available figure: the verdict alone could not be told in gigabytes.
    /// </summary>
    public static MemoryReading? Parse(string? factor, string? meminfo)
    {
        if (Kilobytes(meminfo, "MemTotal") is not { } total || total <= 0
            || Kilobytes(meminfo, "MemAvailable") is not { } available)
        {
            return null;
        }

        return new MemoryReading(
            Verdict(factor) ?? FromShare(available / (double)total),
            available * 1024,
            total * 1024);
    }

    /// <summary>What the phone's line says, or nothing below low.</summary>
    public string? Describe() => Level switch
    {
        MemoryPressure.Critical => Strings.Format("DeviceMemoryCritical", AvailableGigabytes),
        MemoryPressure.Low => Strings.Format("DeviceMemoryLow", AvailableGigabytes),
        _ => null,
    };

    /// <summary>
    /// The first word of the answer, when it is one of the four levels.
    /// Android 11 answers an error line instead, which reads as none.
    /// </summary>
    private static MemoryPressure? Verdict(string? factor) =>
        factor?.Trim().Split(['\r', '\n', ' '], 2)[0].ToUpperInvariant() switch
        {
            "NORMAL" => MemoryPressure.Normal,
            "MODERATE" => MemoryPressure.Moderate,
            "LOW" => MemoryPressure.Low,
            "CRITICAL" => MemoryPressure.Critical,
            _ => null,
        };

    private static MemoryPressure FromShare(double share) =>
        share < CriticalShare ? MemoryPressure.Critical
        : share < LowShare ? MemoryPressure.Low
        : MemoryPressure.Normal;

    private static long? Kilobytes(string? meminfo, string field)
    {
        if (string.IsNullOrEmpty(meminfo))
        {
            return null;
        }

        var match = MeminfoLine().Matches(meminfo).FirstOrDefault(m => m.Groups[1].Value == field);

        return match is not null
            && long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^(\w+):\s+(\d+)\s*kB", RegexOptions.Multiline)]
    private static partial Regex MeminfoLine();
}
