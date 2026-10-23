using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>A quest's gain as the lists show it: a compact column and a hover with the details.</summary>
public static class QuestGainText
{
    /// <summary>"12,7 M", "86 k" or the number itself, in the interface's culture.</summary>
    public static string Compact(long value)
    {
        var culture = Strings.Spoken;

        // From 999 500 up, "k" would round to "1000 k".
        return value >= 999_500 ? (value / 1_000_000.0).ToString("0.#", culture) + " M"
            : value >= 1_000 ? (value / 1_000.0).ToString("0", culture) + " k"
            : value.ToString(culture);
    }

    public static string Column(QuestGain gain, RewardRanking ranking)
    {
        ArgumentNullException.ThrowIfNull(gain);

        return Compact(ranking == RewardRanking.Xp ? gain.Xp : gain.Kamas);
    }

    /// <summary>XP and kamas; then, for a doable quest, its tier and what it asks for.</summary>
    public static string Tip(QuestGain gain, int tier = 0, IReadOnlyList<string>? reasons = null)
    {
        ArgumentNullException.ThrowIfNull(gain);

        var culture = Strings.Spoken;
        var lines = new List<string>
        {
            $"{Strings.Format("RewardXp", gain.Xp.ToString("N0", culture))} · {gain.Kamas.ToString("N0", culture)} {Strings.Get("RewardKamas")}",
        };

        if (tier > 0)
        {
            lines.Add(Strings.Get(tier switch { 3 => "ProfitTier3", 2 => "ProfitTier2", _ => "ProfitTier1" }));
        }

        if (reasons is { Count: > 0 })
        {
            lines.Add(Strings.Format("ProfitEffort", string.Join(", ", reasons)));
        }

        return string.Join("\n", lines);
    }
}
