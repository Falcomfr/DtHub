namespace DtHub.Core.Papycha;

/// <summary>
/// What precedes and what follows a quest, and its rank within its
/// achievement.
/// </summary>
/// <param name="Previous">The quest before, or <c>null</c>.</param>
/// <param name="Next">The quest after, or <c>null</c>.</param>
/// <param name="Rank">
/// Rank within the achievement, starting at 1. Zero outside an
/// achievement.
/// </param>
/// <param name="Count">
/// Number of quests in the achievement. Zero outside an
/// achievement.
/// </param>
public readonly record struct QuestNeighbours(
    QuestSummary? Previous,
    QuestSummary? Next,
    int Rank,
    int Count)
{
    /// <summary>
    /// True when the achievement's list itself designated the next
    /// one, because the open quest is not the last in the list.
    ///
    /// Used to know who has the final say. The column the site
    /// publishes at the foot of the article is titled "Quêtes et
    /// jalons suivants" (Next quests and milestones): it states
    /// what this quest unlocks, that is, the prerequisite graph,
    /// not the order in which an achievement is read. The two
    /// often look alike and sometimes differ: in "Le théâtre des
    /// gobelins", the column for "Titi Gobelait le magobelin" names
    /// only "Manque de moule", which requires it, while the list
    /// first goes through "Un avenir de krotte de Trooll", which
    /// requires nothing. Following the column would skip a quest.
    /// </summary>
    public bool NextFromList => Rank > 0 && Rank < Count;

    /// <summary>
    /// True when the achievement's list itself designated the
    /// previous one, because the open quest is not its first.
    /// </summary>
    public bool PreviousFromList => Rank > 1;
}

/// <summary>
/// Decides a quest's neighbours.
///
/// Two sources, in this order. The achievement's list first: the
/// previous one is the one seen above, the next one the one below,
/// which is what you expect when browsing a list. The prerequisite
/// graph next, where the list stops: an achievement's first quest
/// has no previous one, its last has no next one, and yet the site
/// keeps going.
///
/// The computation lives in the core, not in the view: that is the
/// only layer the tests reach, the test project targeting net10.0
/// while the application targets net10.0-windows. It used to live
/// there, and nothing tested it.
/// </summary>
public static class QuestNeighbourhood
{
    /// <summary>
    /// This quest's neighbours, backed by the chain index if one
    /// exists.
    /// </summary>
    public static QuestNeighbours Of(
        QuestSummary? quest,
        IEnumerable<QuestSummary>? quests,
        QuestChainIndex? chain)
    {
        if (quest is null)
        {
            return default;
        }

        QuestSummary? previous = null;
        QuestSummary? next = null;
        var rank = 0;
        var count = 0;

        if (quest.SuccessName.Length > 0 && quests is not null)
        {
            List<QuestSummary> group =
            [
                .. QuestPlayOrder.Sorted(quests.Where(q =>
                    string.Equals(q.SuccessName, quest.SuccessName, StringComparison.Ordinal))),
            ];

            var index = group.FindIndex(q =>
                string.Equals(q.Url, quest.Url, StringComparison.Ordinal));

            if (index >= 0)
            {
                rank = index + 1;
                count = group.Count;

                if (index > 0)
                {
                    previous = group[index - 1];
                }

                if (index < group.Count - 1)
                {
                    next = group[index + 1];
                }
            }
        }

        if (chain is not null)
        {
            previous ??= chain.PreviousOf(quest);
            next ??= chain.NextOf(quest);

            // And if nothing hangs off this quest, the next series,
            // searched across the whole achievement: it does not
            // always start from its last quest.
            next ??= chain.NextSeriesOf(quest);
        }

        return new QuestNeighbours(previous, next, rank, count);
    }
}
