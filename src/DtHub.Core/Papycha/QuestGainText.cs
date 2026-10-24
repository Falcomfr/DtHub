using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>A quest's gain as the lists show it: a compact column and a hover with the details.</summary>
public static class QuestGainText
{
    /// <summary>"12,7 M", "86 k" or the number itself, in the interface's culture; "2,8 k" with <paramref name="fine"/>.</summary>
    public static string Compact(long value, bool fine = false)
    {
        var culture = Strings.Spoken;

        // From 999 500 up, "k" would round to "1000 k".
        return value >= 999_500 ? (value / 1_000_000.0).ToString("0.#", culture) + " M"
            : value >= 1_000 ? (value / 1_000.0).ToString(fine ? "0.#" : "0", culture) + " k"
            : value.ToString(culture);
    }

    /// <summary>
    /// The XP and kamas columns: an empty XP text when the quest gives none (known, unlike null), no kamas text when
    /// it gives none.
    /// </summary>
    public static (string Xp, string? Kamas) Columns(QuestGain gain)
    {
        ArgumentNullException.ThrowIfNull(gain);

        return (gain.Xp > 0 ? Compact(gain.Xp) : string.Empty, gain.Kamas > 0 ? Compact(gain.Kamas) : null);
    }

    /// <summary>"✦ 1,2 M XP/min · ◎ 2,8 k kamas/min", the rates the short lists rank on; null without a time.</summary>
    public static string? Rate(QuestGain gain, int minutes)
    {
        ArgumentNullException.ThrowIfNull(gain);

        return minutes <= 0 ? null
            : Strings.Format("ProfitRate", Compact((long)Math.Round((double)gain.Xp / minutes), fine: true), Compact((long)Math.Round((double)gain.Kamas / minutes), fine: true));
    }

    /// <summary>
    /// XP and kamas, said to be the whole quest's past one step (DT Hub does not know the step reached); then, for a
    /// doable quest, its tier with the side it is worth it for; then its time and what it asks for.
    /// </summary>
    public static string Tip(QuestGain gain, int tier = 0, bool byKamas = false, QuestEffort? effort = null, int steps = 1)
    {
        ArgumentNullException.ThrowIfNull(gain);

        var culture = Strings.Spoken;
        var lines = new List<string>
        {
            $"{Strings.Format("RewardXp", gain.Xp.ToString("N0", culture))} · {gain.Kamas.ToString("N0", culture)} {Strings.Get("RewardKamas")}",
        };

        if (steps > 1)
        {
            lines.Add(Strings.Format("RewardWholeQuest", steps));
        }

        if (tier > 0)
        {
            lines.Add(Strings.Get(tier switch { 3 => "ProfitTier3", 2 => "ProfitTier2", _ => "ProfitTier1" })
                + ", " + Strings.Get(byKamas ? "ProfitByKamas" : "ProfitByXp"));
        }

        if (effort is not null)
        {
            var time = QuestTime.Text(effort.Minutes);
            lines.Add(effort.Reasons.Count > 0
                ? Strings.Format("ProfitEffort", time, string.Join(", ", effort.Reasons))
                : Strings.Format("ProfitTime", time));
        }

        return string.Join("\n", lines);
    }
}
