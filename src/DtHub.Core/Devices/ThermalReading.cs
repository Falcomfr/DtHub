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

        return new ThermalReading(value, Skin(dumpsys));
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
        var current = dumpsys.IndexOf("Current temperatures", StringComparison.OrdinalIgnoreCase);

        // An Android version that does not separate the two sections
        // falls back to the single reading, which is then correct.
        var skin = SkinPattern().Match(current >= 0 ? dumpsys[current..] : dumpsys);

        return skin.Success
            && double.TryParse(
                skin.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius)
            ? celsius
            : null;
    }

    [GeneratedRegex(@"Thermal Status:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusPattern();

    [GeneratedRegex(@"Temperature\{mValue=(-?[\d.]+),\s*mType=3\b", RegexOptions.IgnoreCase)]
    private static partial Regex SkinPattern();
}
