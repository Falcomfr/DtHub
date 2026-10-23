using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>What the level and XP bonus boxes accept; anything else is refused unchanged.</summary>
public static class RewardInput
{
    public static bool TryLevel(string? text, out int level) => TryRange(text, IsLevel, out level);

    public static bool TryBonus(string? text, out int percent) => TryRange(text, IsBonus, out percent);

    public static bool IsLevel(int value) => value is >= 1 and <= 200;

    public static bool IsBonus(int value) => value is >= 0 and <= 1000;

    private static bool TryRange(string? text, Func<int, bool> valid, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && valid(value);
}
