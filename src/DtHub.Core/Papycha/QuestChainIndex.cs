namespace DtHub.Core.Papycha;

/// <summary>
/// Links the quests that achievements do not link, by following their
/// prerequisites.
///
/// An achievement's list stops at its own edges: its first quest has
/// no previous one, its last has no next one, and a quest with no
/// achievement has neither. The site, though, keeps going: at Albuera,
/// "Bien débuter" leads to "Une arrivée mouvementée", which leads to
/// "Le début des problèmes", which opens the achievement "Médiation
/// expéditive". None of that could be read.
///
/// Measured across the seven hundred and eighty-two quests: one
/// hundred and sixty-eight gain a next one and one hundred and
/// ninety-seven a previous one. Thirteen and seventeen branch out, and
/// gain none: picking one at random would misrepresent what the site
/// publishes.
///
/// A prerequisite does not always name a quest, and it is
/// <see cref="Resolve"/> that untangles it. Reading them all as titles
/// left seventy-three labels out of five hundred and eighty-four with
/// no effect, and eight achievements ended in a dead end for lack of
/// the single link that led to the next one.
///
/// Table built once: the question comes up every time a quest is
/// opened, and scanning the whole catalogue each time would cost
/// seven hundred and eighty-two comparisons for one answer.
/// </summary>
public sealed class QuestChainIndex
{
    private readonly Dictionary<string, QuestSummary> _byTitle;
    private readonly Dictionary<string, List<QuestSummary>> _followers;
    private readonly Dictionary<string, IReadOnlyList<QuestSummary>> _bySuccess;
    private readonly Dictionary<string, QuestSummary> _lastOfSuccess;

    public QuestChainIndex(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<QuestSummary> all = [.. quests];

        // The matching is done on the normalized title, as in the
        // search: the site writes prerequisites by hand, with its own
        // apostrophes and accents.
        _byTitle = [];

        foreach (var quest in all)
        {
            _byTitle.TryAdd(QuestSearch.Normalize(quest.Title), quest);
        }

        // The quests of each achievement, in the order they are
        // played, matching the order used by the list: an unknown
        // rank used to sort to the front here, and to the back there.
        // A quest of unknown rank thus passed for the first of its
        // achievement, and "Une arrivée mouvementée" passed itself off
        // as the continuation of the previous series without opening
        // it.
        _bySuccess = all
            .Where(q => q.SuccessName.Length > 0)
            .GroupBy(q => q.SuccessName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<QuestSummary>)QuestPlayOrder.Sorted(g),
                StringComparer.Ordinal);

        // The last quest of each achievement, by normalized name. A
        // prerequisite that cites a whole achievement, "Succès Un
        // nouveau départ réalisé", designates the state where that
        // achievement is earned: the quest that closed it.
        _lastOfSuccess = [];

        foreach (var (name, group) in _bySuccess)
        {
            if (group.Count > 0)
            {
                _lastOfSuccess[QuestSearch.Normalize(name)] = group[^1];
            }
        }

        _followers = [];

        foreach (var quest in all)
        {
            foreach (var need in quest.Prerequisites)
            {
                if (Resolve(need) is not { } before)
                {
                    continue;
                }

                var key = QuestSearch.Normalize(before.Title);

                if (!_followers.TryGetValue(key, out var list))
                {
                    list = [];
                    _followers[key] = list;
                }

                list.Add(quest);
            }
        }
    }

    /// <summary>
    /// The quest that a prerequisite label designates, or <c>null</c>.
    ///
    /// The bare title and the milestone name the quest itself; the
    /// achievement names the one that closes it, since requiring it
    /// means requiring everything it contains. Without this reading,
    /// one prerequisite in eight linked to nothing.
    /// </summary>
    private QuestSummary? Resolve(string? need)
    {
        var target = PrerequisiteLabel.Of(need);

        if (target.Name.Length == 0)
        {
            return null;
        }

        var key = QuestSearch.Normalize(target.Name);

        if (key.Length == 0)
        {
            return null;
        }

        return target.IsSuccess
            ? _lastOfSuccess.GetValueOrDefault(key)
            : _byTitle.GetValueOrDefault(key);
    }

    /// <summary>
    /// The quest this one follows from, or <c>null</c> if its
    /// prerequisites name none or name several.
    /// </summary>
    public QuestSummary? PreviousOf(QuestSummary? quest)
    {
        if (quest is null)
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var need in quest.Prerequisites)
        {
            if (Resolve(need) is not { } found || Same(found, quest))
            {
                continue;
            }

            if (only is not null && !Same(only, found))
            {
                return null;
            }

            only = found;
        }

        return only;
    }

    /// <summary>
    /// The quest that follows from this one, or <c>null</c> if none
    /// names it as a prerequisite or if several do.
    /// </summary>
    public QuestSummary? NextOf(QuestSummary? quest)
    {
        if (quest is null
            || !_followers.TryGetValue(QuestSearch.Normalize(quest.Title), out var list))
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var candidate in list)
        {
            if (Same(candidate, quest))
            {
                continue;
            }

            if (only is not null && !Same(only, candidate))
            {
                return null;
            }

            only = candidate;
        }

        return only;
    }

    /// <summary>
    /// The quest that opens the next series, or <c>null</c> if there
    /// is not exactly one.
    ///
    /// The continuation of an achievement does not always hang off its
    /// last quest. Measured across the one hundred and fifteen
    /// achievements, the first quest of twelve of them has as a
    /// prerequisite a quest from the middle of the previous
    /// achievement: "Médiation expéditive" continues from its fifth
    /// quest out of six, so the sixth had no continuation at all. So
    /// the search runs through the whole current achievement, not just
    /// the single quest one starts from.
    ///
    /// Seven achievements gain a continuation from it. Two open
    /// several and receive none: designating one at random would
    /// misrepresent it.
    /// </summary>
    public QuestSummary? NextSeriesOf(QuestSummary? quest)
    {
        if (quest is null || quest.SuccessName.Length == 0)
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var member in _bySuccess.GetValueOrDefault(quest.SuccessName, []))
        {
            foreach (var candidate in _followers.GetValueOrDefault(
                QuestSearch.Normalize(member.Title), []))
            {
                // What remains within the achievement is not another
                // series, and the achievement's list has already said
                // so.
                if (string.Equals(candidate.SuccessName, quest.SuccessName, StringComparison.Ordinal)
                    || candidate.SuccessName.Length == 0
                    || !IsFirst(candidate))
                {
                    continue;
                }

                if (only is not null && !Same(only, candidate))
                {
                    return null;
                }

                only = candidate;
            }
        }

        return only;
    }

    /// <summary>
    /// True if the quest opens its achievement, in play order.
    /// </summary>
    private bool IsFirst(QuestSummary quest)
    {
        var group = _bySuccess.GetValueOrDefault(quest.SuccessName, []);

        return group.Count > 0 && Same(group[0], quest);
    }

    /// <summary>
    /// The quest carried by this title, or <c>null</c> if none. The
    /// matching is the same as the chain's: the site writes its
    /// cross-references by hand.
    /// </summary>
    public QuestSummary? Find(string? title) =>
        title is null ? null : _byTitle.GetValueOrDefault(QuestSearch.Normalize(title));

    private static bool Same(QuestSummary first, QuestSummary second) =>
        string.Equals(first.Url, second.Url, StringComparison.Ordinal);
}
