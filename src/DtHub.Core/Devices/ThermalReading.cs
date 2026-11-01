using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// What the device says about its heat.
///
/// This is the real limit of multi-accounting on a tablet, and it is
/// silent like the input injection refusal: nothing fails, everything
/// slows down. Noted on a competing product's support forum, where
/// several people describe the same thing: "dès que le SoC dépasse
/// 65 °, il coupe le multitâche et demande d'attendre que ça
/// refroidisse" (as soon as the SoC goes above 65 °, it cuts
/// multitasking and asks to wait for it to cool down).
///
/// The verdict is read from <c>Thermal Status</c>, the scale from
/// zero to six that every Android has exposed since version 10. The
/// named temperatures, on the other hand, vary from one manufacturer
/// to another and are only useful for the log: on the reference
/// phone, the processor shows 84 ° while the status is zero and
/// nothing is throttled. A number like that in a message would raise
/// a false alarm for nothing.
///
/// **Except the skin sensor's own level.** Xiaomi throttles on it and
/// leaves the global status at zero: on 2026-10-03 the reference phone
/// ran half an hour with its big cores at 1.1 and 1.3 GHz out of 3.0
/// and 3.35, its skin at 50.8 ° on level 3, and the global status said
/// 0. The higher of the two is the verdict. The processor's level is
/// still left out, for the reason above.
///
/// **And the global status can be a leftover.** It is not a measurement
/// but the last alert the HAL sent: on 2026-11-01 the reference phone,
/// idle and cold, still said 3 from its last game, every current sensor
/// at level 0 and tens of degrees under its thresholds. It is set aside
/// when the current temperatures, held against the HAL's own thresholds,
/// all say cool: that reads degrees, not the per-sensor level some
/// manufacturer may leave at zero.
/// </summary>
/// <param name="Status">
/// Thermal state, from 0 (nothing) to 6 (shutdown).
/// </param>
/// <param name="SkinCelsius">
/// Surface temperature, for the log, if it is given.
/// </param>
public sealed partial record ThermalReading(int Status, double? SkinCelsius)
{
    /// <summary>
    /// From here on, Android throttles and it can be noticed.
    /// </summary>
    public const int Throttling = 2;

    /// <summary>From here on, the throttling is heavy.</summary>
    public const int Severe = 3;

    /// <summary>
    /// How far under its first threshold a sensor must be for a leftover
    /// status to be ruled out: throttling lets go a few degrees under the
    /// point where it started.
    /// </summary>
    private const double Hysteresis = 5;

    /// <summary>
    /// True when the device throttles enough for it to be noticeable.
    /// </summary>
    public bool IsThrottling => Status >= Throttling;

    /// <summary>
    /// Reads the output of <c>dumpsys thermalservice</c>. Returns
    /// <c>null</c> as soon as the state is missing: knowing nothing
    /// about the heat is an ordinary case, and the caller does without
    /// it.
    /// </summary>
    public static ThermalReading? Parse(string? dumpsys)
    {
        if (string.IsNullOrWhiteSpace(dumpsys))
        {
            return null;
        }

        var status = StatusPattern().Match(dumpsys);

        if (!status.Success
            || !int.TryParse(status.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var global = IsLeftover(dumpsys) ? 0 : value;

        return new ThermalReading(Math.Max(global, SkinLevel(dumpsys)), Skin(dumpsys));
    }

    /// <summary>
    /// True when every current sensor that has a threshold sits well under
    /// it, so the global status can only be left over from an earlier
    /// heat. False whenever that cannot be told: a forced status, no
    /// current section, no thresholds.
    /// </summary>
    private static bool IsLeftover(string dumpsys)
    {
        var current = dumpsys.IndexOf("Current temperatures", StringComparison.OrdinalIgnoreCase);

        if (current < 0 || OverridePattern().IsMatch(dumpsys))
        {
            return false;
        }

        var thresholds = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (Match threshold in ThresholdPattern().Matches(dumpsys))
        {
            var lowest = threshold.Groups[2].Value.Split(',')
                .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : double.NaN)
                .Where(double.IsFinite)
                .DefaultIfEmpty(double.NaN)
                .Min();

            if (double.IsFinite(lowest))
            {
                thresholds[threshold.Groups[1].Value] = lowest;
            }
        }

        var compared = 0;

        foreach (Match sensor in CurrentPattern().Matches(dumpsys[current..]))
        {
            if (!thresholds.TryGetValue(sensor.Groups[2].Value, out var lowest)
                || !double.TryParse(sensor.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius))
            {
                continue;
            }

            if (celsius > lowest - Hysteresis)
            {
                return false;
            }

            compared++;
        }

        return compared > 0;
    }

    /// <summary>
    /// What there is to tell the player, or <c>null</c> if there is
    /// nothing to say.
    /// </summary>
    public string? Describe() => Status switch
    {
        >= Severe => Strings.Get("DeviceThrottlingSevere"),
        >= Throttling => Strings.Get("DeviceThrottling"),
        _ => null,
    };

    /// <summary>
    /// Surface temperature. Type 3 is the skin type in the Android API:
    /// the name, however, changes from one manufacturer to another.
    ///
    /// The output gives two, and taking the first would be wrong.
    /// Noted on the reference phone: the "Cached temperatures" section
    /// announced 48.5 ° while the "Current temperatures from HAL"
    /// section gave 34.4. It is the second one that reflects the
    /// instant.
    /// </summary>
    private static double? Skin(string dumpsys)
    {
        var skin = SkinPattern().Match(Current(dumpsys));

        return skin.Success
            && double.TryParse(
                skin.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius)
            ? celsius
            : null;
    }

    /// <summary>The current skin sensor's throttling level, 0 when it gives none.</summary>
    private static int SkinLevel(string dumpsys)
    {
        var level = SkinLevelPattern().Match(Current(dumpsys));

        return level.Success
            && int.TryParse(level.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    /// <summary>
    /// From the "Current temperatures" section on. An Android version that
    /// does not separate it from the cache falls back to the single
    /// reading, which is then correct.
    /// </summary>
    private static string Current(string dumpsys)
    {
        var current = dumpsys.IndexOf("Current temperatures", StringComparison.OrdinalIgnoreCase);

        return current >= 0 ? dumpsys[current..] : dumpsys;
    }

    [GeneratedRegex(@"Thermal Status:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"IsStatusOverride:\s*true", RegexOptions.IgnoreCase)]
    private static partial Regex OverridePattern();

    [GeneratedRegex(@"TemperatureThreshold\{mType=\d+,\s*mName=([^,}]+),\s*mHotThrottlingThresholds=\[([^\]]*)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ThresholdPattern();

    [GeneratedRegex(@"^\s*Temperature\{mValue=(-?[\d.]+),\s*mType=\d+,\s*mName=([^,}]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex CurrentPattern();

    [GeneratedRegex(@"Temperature\{mValue=(-?[\d.]+),\s*mType=3\b", RegexOptions.IgnoreCase)]
    private static partial Regex SkinPattern();

    [GeneratedRegex(@"Temperature\{mValue=-?[\d.]+,\s*mType=3\b[^}]*mStatus=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SkinLevelPattern();
}
