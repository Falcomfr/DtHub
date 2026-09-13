using System.Globalization;
using System.Text.RegularExpressions;

using DtHub.Core.Localization;

namespace DtHub.Core.Devices;

/// <summary>
/// What the device says about its battery.
///
/// This is the limit nobody talks about, and yet it is the one
/// that stops long sessions. The game uses thirty to fifty percent
/// per hour on a phone, and over Wi-Fi the device is not plugged
/// in: a three-hour session ends with a dead phone, in the middle
/// of a dungeon.
///
/// The application already read heat and connection, but not this.
///
/// **Nothing is said while the device is charging.** A plugged-in
/// phone rarely drops, and a banner announcing forty percent while
/// the charger is connected talks without saying anything. What
/// matters is not the level, it is the level dropping.
/// </summary>
/// <param name="Percent">Charge level, from 0 to 100.</param>
/// <param name="Charging">
/// True if the device is charging, or full while on mains power.
/// </param>
/// <param name="Celsius">
/// Battery temperature, for the log, if it is reported.
/// </param>
public sealed partial record BatteryReading(int Percent, bool Charging, double? Celsius)
{
    /// <summary>Below this, the session will not last the evening.</summary>
    public const int Low = 20;

    /// <summary>Below this, only a few minutes remain.</summary>
    public const int Critical = 10;

    /// <summary>
    /// True when the level deserves mention, charging excluded.
    /// </summary>
    public bool IsLow => !Charging && Percent <= Low;

    /// <summary>
    /// The concern tier, or <c>null</c> when there is nothing to
    /// report.
    ///
    /// Two readers use it, the warning and the gauge's color: the
    /// thresholds are therefore decided once, here, and not on each
    /// side separately.
    /// </summary>
    public HealthSeverity? Concern => !Charging && Percent <= Critical
        ? HealthSeverity.Serious
        : IsLow
            ? HealthSeverity.Warning
            : null;

    /// <summary>
    /// The level alone, as it fits next to the device's name.
    /// </summary>
    public string Label => Strings.Format("BatteryPercent", Percent);

    /// <summary>
    /// The level in one sentence, for the tooltip.
    ///
    /// The charging state is stated here, and that is the point: a
    /// phone at twelve percent that triggers no warning must be
    /// able to explain why, without anyone having to dig for it.
    /// </summary>
    public string Summary => Charging
        ? Strings.Format("BatteryChargingAt", Percent)
        : Strings.Format("BatteryAt", Percent);

    /// <summary>
    /// Parses the output of <c>dumpsys battery</c>. Returns
    /// <c>null</c> as soon as the level is missing: knowing nothing
    /// is an ordinary case, and the caller does without it, as with
    /// temperature.
    /// </summary>
    public static BatteryReading? Parse(string? dumpsys)
    {
        if (string.IsNullOrWhiteSpace(dumpsys))
        {
            return null;
        }

        if (Number(dumpsys, LevelPattern()) is not { } level)
        {
            return null;
        }

        // The scale is a hundred everywhere it has been seen, but
        // it is reported explicitly: relying on a hundred would
        // mean assuming what the device already states.
        var scale = Number(dumpsys, ScalePattern()) ?? 100;

        if (scale <= 0)
        {
            return null;
        }

        var percent = (int)Math.Round(level * 100.0 / scale);

        return new BatteryReading(Math.Clamp(percent, 0, 100), IsCharging(dumpsys), Temperature(dumpsys));
    }

    /// <summary>
    /// What there is to say, or <c>null</c> when there is nothing
    /// to say.
    /// </summary>
    public string? Describe() => !Charging && Percent <= Critical
        ? Strings.Format("DeviceBatteryCritical", Percent)
        : IsLow
            ? Strings.Format("DeviceBatteryLow", Percent)
            : null;

    /// <summary>
    /// True if the device is receiving power.
    ///
    /// **The "... powered" lines have the final word**, because
    /// they state the connection itself. The numeric status is 2
    /// while charging and 5 when the battery is full, but it
    /// lingers: a device that was just unplugged keeps it for at
    /// least one more reading.
    ///
    /// Measured: an unplugged Mi 9T Pro reports all four power
    /// lines as false and "status: 2" at the same time. Trusting
    /// both equally would have meant silencing the low battery
    /// alert on an unplugged phone.
    ///
    /// The numeric status therefore only serves as a fallback, for
    /// a device that would write no power line at all.
    /// </summary>
    private static bool IsCharging(string dumpsys)
    {
        string[] plugs = ["AC powered", "USB powered", "Wireless powered", "Dock powered"];

        return plugs.Any(p => dumpsys.Contains(p, StringComparison.OrdinalIgnoreCase))
            ? plugs.Any(p => Flag(dumpsys, p))
            : Number(dumpsys, StatusPattern()) is 2 or 5;
    }

    private static bool Flag(string dumpsys, string label)
    {
        var at = dumpsys.IndexOf(label, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return false;
        }

        var line = dumpsys[at..];
        var end = line.IndexOf('\n', StringComparison.Ordinal);

        return (end < 0 ? line : line[..end]).Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The temperature, given in tenths of a degree.</summary>
    private static double? Temperature(string dumpsys) =>
        Number(dumpsys, TemperaturePattern()) is { } tenths ? tenths / 10.0 : null;

    private static int? Number(string dumpsys, Regex pattern)
    {
        var match = pattern.Match(dumpsys);

        return match.Success
               && int.TryParse(
                   match.Groups[1].Value,
                   NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"^\s*level:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex LevelPattern();

    [GeneratedRegex(@"^\s*scale:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ScalePattern();

    [GeneratedRegex(@"^\s*status:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"^\s*temperature:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TemperaturePattern();
}
