namespace DtHub.Core.Papycha;

/// <summary>
/// The order in which an achievement's quests are played.
///
/// Prerequisites come first: a quest that requires another one from
/// the same achievement comes after it, whatever the rest says. This
/// is the only rule the reader can verify for themselves, and the
/// only one they notice when it is broken.
///
/// Failing a prerequisite, the previous order, which also breaks ties
/// between quests that no prerequisite separates: the place within
/// the achievement, computed at indexing time; then the chain rank,
/// for quests the map does not know; the title last, so the order is
/// total and always the same.
///
/// A zero does not mean "first" but "unknown", and is therefore
/// placed at the end. The quest chain used to sort it in place
/// however, so that a quest of unknown rank passed for the first of
/// its achievement and presented itself as the continuation of the
/// previous series.
///
/// The place computed at indexing time comes from a topological sort
/// done in Python, and thirteen quests in the catalog contradict it:
/// "L'île Céleste" ("The Celestial Island") holds rank 2 there while
/// "Le voyage vers Incarnam" ("The journey to Incarnam"), which it
/// requires, holds rank 3. Redoing the sort here, on the
/// prerequisites the application reads anyway, makes the list true
/// without depending on what the map retained.
/// </summary>
public static class QuestPlayOrder
{
    /// <summary>The quests arranged in the order they are played.</summary>
    public static IReadOnlyList<QuestSummary> Sorted(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<QuestSummary> all = [.. quests];

        if (all.Count < 2)
        {
            return all;
        }

        // The tiebreaker: the previous order, used as is when no
        // prerequisite separates two quests, and to break a cycle.
        var keys = new (int Play, int Chain, string Title, int Index)[all.Count];

        Dictionary<string, int> byTitle = new(StringComparer.Ordinal);

        for (var i = 0; i < all.Count; i++)
        {
            keys[i] = (
                all[i].PlayOrder == 0 ? int.MaxValue : all[i].PlayOrder,
                all[i].ChainStep == 0 ? int.MaxValue : all[i].ChainStep,
                all[i].Title,
                i);

            byTitle.TryAdd(QuestSearch.Normalize(all[i].Title), i);
        }

        var after = new List<HashSet<int>>(all.Count);
        var waiting = new int[all.Count];

        for (var i = 0; i < all.Count; i++)
        {
            after.Add([]);
        }

        for (var i = 0; i < all.Count; i++)
        {
            foreach (var need in all[i].Prerequisites)
            {
                var named = PrerequisiteLabel.Of(need);

                if (named.IsSuccess)
                {
                    // A quest that requires its own achievement
                    // requires it whole: it therefore comes after all
                    // the rest of the block. "En route pour Plantala"
                    // ("On the way to Plantala") is in this case, the
                    // only one among the seven hundred and
                    // eighty-two quests.
                    if (string.Equals(named.Name, all[i].SuccessName, StringComparison.OrdinalIgnoreCase))
                    {
                        for (var other = 0; other < all.Count; other++)
                        {
                            if (other != i && after[other].Add(i))
                            {
                                waiting[i]++;
                            }
                        }
                    }

                    // Any other achievement designates a different
                    // block: it orders nothing within this one.
                    continue;
                }

                if (!byTitle.TryGetValue(QuestSearch.Normalize(named.Name), out var from)
                    || from == i
                    || !after[from].Add(i))
                {
                    continue;
                }

                waiting[i]++;
            }
        }

        return Sort(all, keys, after, waiting);
    }

    /// <summary>
    /// The topological sort, with a fallback on cycles: we then take
    /// the smallest remaining quest in the sense of the tiebreaker
    /// and continue, so the order stays total rather than truncated.
    /// </summary>
    private static List<QuestSummary> Sort(
        List<QuestSummary> all,
        (int Play, int Chain, string Title, int Index)[] keys,
        List<HashSet<int>> after,
        int[] waiting)
    {
        // The title is compared using culture rules, as everywhere
        // the site is read: its titles are French and full of
        // accents. Tuple comparison takes care of this, and it is
        // the same one already used by the zone ordering.
        var order = Comparer<int>.Create((first, second) => keys[first].CompareTo(keys[second]));

        SortedSet<int> left = new(order);
        SortedSet<int> ready = new(order);

        for (var i = 0; i < all.Count; i++)
        {
            left.Add(i);

            if (waiting[i] == 0)
            {
                ready.Add(i);
            }
        }

        List<QuestSummary> plan = new(all.Count);

        while (left.Count > 0)
        {
            var at = ready.Count > 0 ? ready.Min : left.Min;

            _ = ready.Remove(at);
            _ = left.Remove(at);

            plan.Add(all[at]);

            foreach (var next in after[at])
            {
                if (left.Contains(next) && --waiting[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        return plan;
    }
}
