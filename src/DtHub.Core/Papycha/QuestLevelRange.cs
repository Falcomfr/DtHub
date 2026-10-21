using System.Globalization;

namespace DtHub.Core.Papycha;

/// <summary>
/// The bare level range of a set of quests: "100-200", a single number when
/// they all share it, <c>null</c> when none has a level.
/// </summary>
public static class QuestLevelRange
{
    public static string? Of(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<int> levels = [.. quests.Where(q => q.Level > 0).Select(q => q.Level)];

        if (levels.Count == 0)
        {
            return null;
        }

        var low = levels.Min();
        var high = levels.Max();

        return low == high ? Text(low) : $"{Text(low)}-{Text(high)}";
    }

    private static string Text(int value) => value.ToString(CultureInfo.CurrentCulture);
}
