using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>What the level and XP bonus boxes accept; anything else is refused unchanged.</summary>
public static class RewardInput
{
    public static bool TryLevel(string? text, out int level) => TryRange(text, 1, 200, out level);

    public static bool TryBonus(string? text, out int percent) => TryRange(text, 0, 1000, out percent);

    private static bool TryRange(string? text, int min, int max, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value >= min && value <= max;
}
