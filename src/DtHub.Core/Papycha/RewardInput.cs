using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>What the level box accepts, anything else is refused unchanged; and the bonus range a setting may hold.</summary>
public static class RewardInput
{
    public static bool TryLevel(string? text, out int level) => TryRange(text, IsLevel, out level);

    public static bool IsLevel(int value) => value is >= 1 and <= 200;

    public static bool IsBonus(int value) => value is >= 0 and <= 1000;

    private static bool TryRange(string? text, Func<int, bool> valid, out int value) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && valid(value);
}
