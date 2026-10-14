using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Devices;

/// <summary>
/// The phone's own screen ratio, held landscape.
///
/// The game draws itself right at that ratio, and not always at another.
/// Measured on 14/10/2026: a Galaxy Tab S10 Ultra (16:10) drew it shifted
/// right with its panel cut in a 21:9 display, though Android gave it the
/// whole display, and right in 16:10; the 13T Pro (20:9) cut its panel in
/// 16:10, and was right in 20:9 and 21:9. The virtual display therefore
/// takes the phone's ratio, not the PC screen's.
/// </summary>
public static partial class PhoneScreen
{
    /// <summary>
    /// Long side over short side, from "wm size". An override, when set, is
    /// what the system and the game use. <c>null</c> if unreadable or
    /// absurd.
    /// </summary>
    public static double? Aspect(string? wmSize)
    {
        var match = SizePattern().Matches(wmSize ?? string.Empty).LastOrDefault();

        if (match is null)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var height)
            || width <= 0 || height <= 0)
        {
            return null;
        }

        var aspect = Math.Max(width, height) / (double)Math.Min(width, height);

        // Beyond 3, no real screen, even a Fold's cover: a hand-typed
        // override would ask for a display a few pixels tall.
        return aspect <= 3 ? aspect : null;
    }

    // "Physical size: 1220x2712", then "Override size: ..." when one is set.
    [GeneratedRegex(@"size: (\d+)x(\d+)")]
    private static partial Regex SizePattern();
}
