using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// The level range of a set of quests, as it is shown, or nothing
/// when what would be said about it would be false.
///
/// The site only provides the level for a hundred and seventeen
/// quests out of seven hundred and eighty-two, and none at all on
/// several zones. A range drawn from a single quest out of
/// twenty-three would pass for the zone's range.
/// </summary>
public static class QuestLevelRange
{
    /// <summary>
    /// The range, or <c>null</c> when too few quests carry a level.
    ///
    /// The threshold is three quests with a level, unless all of
    /// them have one: a zone of two quests that both carry their
    /// level gives an accurate range. The number of quests the
    /// range rests on is recalled whenever it is partial, so that
    /// what is being read is clear.
    /// </summary>
    public static string? Of(IReadOnlyList<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<int> levels = [.. quests.Where(q => q.Level > 0).Select(q => q.Level)];

        if (levels.Count == 0 || (levels.Count < 3 && levels.Count < quests.Count))
        {
            return null;
        }

        var low = levels.Min();
        var high = levels.Max();
        var span = low == high ? Text(low) : $"{Text(low)} - {Text(high)}";

        return levels.Count == quests.Count
            ? Strings.Format("LevelSpan", span)
            : Strings.Format("LevelSpanOf", span, Text(levels.Count));
    }

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);
}
