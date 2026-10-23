using System.Globalization;
using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>The one line under a quest's title: what finishing it gives at the player's level.</summary>
public static class QuestRewardText
{
    public static string Format(QuestRewardTotal total, int level, int bonusPercent, IReadOnlyDictionary<int, string> itemNames)
    {
        var culture = Strings.Spoken;
        var head = Strings.Format("RewardLine", level);
        if (bonusPercent > 0)
        {
            head += " · " + Strings.Format("RewardBonus", bonusPercent);
        }

        var parts = new List<string> { Strings.Format("RewardXp", total.Experience.ToString("N0", culture)) };
        if (total.Kamas > 0)
        {
            parts.Add($"{total.Kamas.ToString("N0", culture)} {Strings.Get("RewardKamas")}");
        }

        // An unnamed item would be a silent gap in the line: hide the line instead.
        if (total.Items.Any(i => !itemNames.ContainsKey(i.ItemId)))
        {
            return "";
        }

        var gains = total.Items
            .Select(i => $"{i.Quantity.ToString("N0", culture)} × {itemNames[i.ItemId]}")
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

    private static void AddKind(List<string> gains, int count, string key)
    {
        if (count > 0)
        {
            gains.Add($"{count} {Strings.Get(count == 1 ? key : key + "s")}");
        }
    }
}
