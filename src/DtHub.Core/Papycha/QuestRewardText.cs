using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>The one line under a quest's title: what finishing it gives at the player's level.</summary>
public static class QuestRewardText
{
    public static string Format(QuestRewardTotal total, int level, int bonusPercent, IReadOnlyDictionary<int, string> itemNames)
    {
        var head = Strings.Format("RewardLine", level);
        if (bonusPercent > 0)
        {
            head += " · " + Strings.Format("RewardBonus", bonusPercent);
        }

        var parts = new List<string> { Strings.Format("RewardXp", QuestGainText.Number(total.Experience)) };
        if (total.Kamas > 0)
        {
            parts.Add($"{QuestGainText.Number(total.Kamas)} {Strings.Get("RewardKamas")}");
        }

        // An unnamed item would be a silent gap in the line: hide the line instead.
        if (total.Items.Any(i => !itemNames.ContainsKey(i.ItemId)))
        {
            return "";
        }

        var gains = total.Items
            .Select(i => $"{QuestGainText.Number(i.Quantity)} × {itemNames[i.ItemId]}")
            .ToList();
        AddKind(gains, total.Spells, "RewardSpell");
        AddKind(gains, total.Titles, "RewardTitle");
        AddKind(gains, total.Emotes, "RewardEmote");
        AddKind(gains, total.Ornaments, "RewardOrnament");
        AddKind(gains, total.Companions, "RewardCompanion");
        AddKind(gains, total.Jobs, "RewardJob");
        if (gains.Count > 0)
        {
            parts.Add(string.Join(", ", gains));
        }

        return Strings.Format("RewardJoin", head, string.Join(" · ", parts));
    }

    /// <summary>
    /// What a quest gives besides XP and kamas, one per line: named items with their quantity, the unnamed ones
    /// counted, then the other kinds counted. Null when there is nothing else.
    /// </summary>
    public static string? Others(QuestRewardTotal total, IReadOnlyDictionary<int, string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(itemNames);

        var lines = total.Items.Where(i => itemNames.ContainsKey(i.ItemId))
            .Select(i => $"{QuestGainText.Number(i.Quantity)} × {itemNames[i.ItemId]}")
            .ToList();
        AddKind(lines, total.Items.Count(i => !itemNames.ContainsKey(i.ItemId)), "RewardItem");
        AddKind(lines, total.Spells, "RewardSpell");
        AddKind(lines, total.Titles, "RewardTitle");
        AddKind(lines, total.Emotes, "RewardEmote");
        AddKind(lines, total.Ornaments, "RewardOrnament");
        AddKind(lines, total.Companions, "RewardCompanion");
        AddKind(lines, total.Jobs, "RewardJob");

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static void AddKind(List<string> gains, int count, string key)
    {
        if (count > 0)
        {
            gains.Add($"{count} {Strings.Get(count == 1 ? key : key + "s")}");
        }
    }
}
