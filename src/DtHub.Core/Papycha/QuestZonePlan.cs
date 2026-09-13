namespace DtHub.Core.Papycha;

/// <summary>
/// A block in a zone's list: an achievement and its quests, or a lone quest
/// that no achievement claims.
/// </summary>
/// <param name="SuccessName">
/// The achievement's name, empty for a lone quest.
/// </param>
/// <param name="Quests">Its quests, in the order they are played.</param>
public sealed record QuestZoneBlock(string SuccessName, IReadOnlyList<QuestSummary> Quests)
{
    /// <summary>
    /// True when the block is an achievement and not a lone quest.
    /// </summary>
    public bool IsSuccess => SuccessName.Length > 0;

    /// <summary>
    /// True when this block picks up an achievement already started higher up,
    /// a lone quest having slipped in between two of its quests.
    /// </summary>
    public bool IsContinuation { get; init; }
}

/// <summary>
/// Sorts a zone's quests in the order they are played.
///
/// Quests that no achievement claims used to be dropped at the end of the
/// list, in alphabetical order. Yet many of them open an achievement or
/// continue it: "Une arrivée mouvementée" comes before "Médiation expéditive"
/// at Albuera, "En route pour Aerdala" comes before "Là où souffle le vent" at
/// Pandala. Seeing them at the bottom of the list, cut off from what they
/// serve, said nothing about progression. And alphabetical order read the
/// eighty alignment quests as "bontarien 1, 10, 11, 12, 2, 20".
///
/// The zone is therefore sorted by its prerequisites: out of the seven hundred
/// and eighty-two quests, five hundred and fourteen prerequisites out of seven
/// hundred and twenty-nine name a quest from the catalogue, and one hundred
/// and ninety-two of the two hundred and eighty-four lone quests are part of a
/// chain.
///
/// An achievement is an indivisible block: its quests follow one another, and
/// it is the achievement that gets sorted among the others. Lacking a
/// prerequisite, the order is the previous one, so that a zone where no
/// prerequisite is recognised does not move.
/// </summary>
public static class QuestZonePlan
{
    /// <summary>
    /// The blocks of a zone, in the order they are played.
    ///
    /// Prerequisites naming a quest missing from the list are ignored: they
    /// cannot sort anything by it. This is the case for those pointing to
    /// another zone.
    /// </summary>
    /// <param name="quests">The zone's quests.</param>
    /// <param name="successOrder">
    /// The order of achievements on the site.
    /// </param>
    public static IReadOnlyList<QuestZoneBlock> Of(
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<string> successOrder)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(successOrder);

        var blocks = Blocks(quests, out var blockOfUrl);

        if (blocks.Count == 0)
        {
            return [];
        }

        var waiting = new int[blocks.Count];
        var after = Edges(quests, blocks, blockOfUrl, waiting);
        var keys = Keys(blocks, successOrder, after);

        return Weave(Sort(blocks, keys, after, waiting, Lonely(blocks, after, waiting)));
    }


    /// <summary>
    /// Second pass: lets a lone quest slip in between two quests of an
    /// achievement, without ever interleaving two achievements.
    ///
    /// Treating an achievement as an indivisible block creates contradictions
    /// that no order can lift: in Amakna, "Étre plus royaliste que le roi"
    /// claims nine lone quests in the middle of its own sequence, so that
    /// those nine seemed either all before or all after. Measured over the
    /// twenty-five lists: eleven prerequisites ended up after the quest that
    /// claims them, and four disappear once the lone quest is let in.
    ///
    /// Two achievements, however, do not interleave. Letting them would lift
    /// the eleven, but the island of Frigost, where eight achievements claim
    /// each other, became a back and forth of fifteen headers between the same
    /// series. An unreadable list is not progress over an imperfect one.
    ///
    /// The first pass decides the order of achievements; this one does not
    /// touch it. With equal constraints, nothing moves.
    /// </summary>
    private static List<QuestZoneBlock> Weave(List<QuestZoneBlock> plan)
    {
        List<QuestSummary> flat = [.. plan.SelectMany(b => b.Quests)];

        if (flat.Count < 2)
        {
            return plan;
        }

        Dictionary<string, int> at = new(StringComparer.Ordinal);
        Dictionary<string, int> byTitle = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> bySuccess = new(StringComparer.Ordinal);

        for (var i = 0; i < flat.Count; i++)
        {
            at[flat[i].Url] = i;
            byTitle.TryAdd(QuestSearch.Normalize(flat[i].Title), i);

            if (flat[i].SuccessName.Length > 0)
            {
                var key = QuestSearch.Normalize(flat[i].SuccessName);

                if (!bySuccess.TryGetValue(key, out var members))
                {
                    members = [];
                    bySuccess[key] = members;
                }

                members.Add(i);
            }
        }

        var after = new List<HashSet<int>>(flat.Count);
        var waiting = new int[flat.Count];

        for (var i = 0; i < flat.Count; i++)
        {
            after.Add([]);
        }

        void Link(int from, int to)
        {
            if (from != to && after[from].Add(to))
            {
                waiting[to]++;
            }
        }

        // The prerequisites. Requiring an achievement means requiring all its
        // quests.
        for (var i = 0; i < flat.Count; i++)
        {
            foreach (var need in flat[i].Prerequisites)
            {
                var named = PrerequisiteLabel.Of(need);
                var key = QuestSearch.Normalize(named.Name);

                if (named.IsSuccess)
                {
                    foreach (var member in bySuccess.GetValueOrDefault(key, []))
                    {
                        Link(member, i);
                    }
                }
                else if (byTitle.TryGetValue(key, out var one))
                {
                    Link(one, i);
                }
            }
        }

        // And the order of achievements, as the first pass has set it: a whole
        // achievement before the whole next one. This is what keeps them from
        // interleaving, and transitivity is enough to cover the other pairs.
        List<List<int>> series =
        [
            .. plan
                .Where(b => b.IsSuccess)
                .Select(b => (List<int>)[.. b.Quests.Select(q => at[q.Url])]),
        ];

        for (var i = 1; i < series.Count; i++)
        {
            foreach (var before in series[i - 1])
            {
                foreach (var next in series[i])
                {
                    Link(before, next);
                }
            }
        }

        var woven = Thread(flat, after, waiting);

        return Runs(woven);
    }

    /// <summary>
    /// The topological sort of the second pass. Ties are broken by the
    /// previous position, so that an absent constraint moves nothing, and a
    /// cycle falls back to the order of the first pass.
    /// </summary>
    private static List<QuestSummary> Thread(
        List<QuestSummary> flat,
        List<HashSet<int>> after,
        int[] waiting)
    {
        SortedSet<int> left = [];
        SortedSet<int> ready = [];

        for (var i = 0; i < flat.Count; i++)
        {
            left.Add(i);

            if (waiting[i] == 0)
            {
                ready.Add(i);
            }
        }

        List<QuestSummary> order = new(flat.Count);

        while (left.Count > 0)
        {
            var at = ready.Count > 0 ? ready.Min : left.Min;

            _ = ready.Remove(at);
            _ = left.Remove(at);

            order.Add(flat[at]);

            foreach (var next in after[at])
            {
                if (left.Contains(next) && --waiting[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        return order;
    }

    /// <summary>
    /// Groups quests that follow one another under the same header. An
    /// achievement resumed further down is marked as a continuation, so the
    /// reader knows it is not starting a series over.
    /// </summary>
    private static List<QuestZoneBlock> Runs(List<QuestSummary> order)
    {
        List<QuestZoneBlock> plan = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        var start = 0;

        for (var i = 1; i <= order.Count; i++)
        {
            // Two lone quests that follow one another stay two blocks: this is
            // how the list shows them, one line each.
            if (i < order.Count
                && order[i].SuccessName.Length > 0
                && string.Equals(order[i].SuccessName, order[start].SuccessName, StringComparison.Ordinal))
            {
                continue;
            }

            var name = order[start].SuccessName;

            plan.Add(new QuestZoneBlock(name, order[start..i])
            {
                IsContinuation = name.Length > 0 && !seen.Add(name),
            });

            start = i;
        }

        return plan;
    }

    /// <summary>
    /// One block per achievement, one single-quest block for each lone quest,
    /// and a way to find a quest's block by its address.
    /// </summary>
    private static List<(string Success, List<QuestSummary> Quests)> Blocks(
        IReadOnlyList<QuestSummary> quests,
        out Dictionary<string, int> blockOfUrl)
    {
        List<(string Success, List<QuestSummary> Quests)> blocks = [];
        Dictionary<string, int> at = new(StringComparer.Ordinal);

        blockOfUrl = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            var key = quest.SuccessName.Length > 0
                ? "s:" + quest.SuccessName
                : "q:" + quest.Url;

            if (!at.TryGetValue(key, out var index))
            {
                index = blocks.Count;
                at[key] = index;
                blocks.Add((quest.SuccessName, []));
            }

            blocks[index].Quests.Add(quest);
            blockOfUrl[quest.Url] = index;
        }

        return blocks;
    }

    /// <summary>
    /// What breaks a tie between two blocks that no prerequisite separates:
    /// the previous order.
    ///
    /// The achievement's rank on the site first, its name second. A lone quest
    /// comes after all achievements of the same rank, as the "Hors succès"
    /// block used to do, gathering them at the end of the list. The block's
    /// rank closes the tie-break, so that the order is total.
    ///
    /// **A lone quest that follows from an achievement takes its rank**, and
    /// so sorts just behind it rather than after all the others. Without this,
    /// "La découverte d'un vaste monde", whose only prerequisite is the
    /// achievement "Devenir une légende", used to end up thirty ranks lower,
    /// behind achievements that have nothing to do with it: the sort placed it
    /// well after what it requires, but so far after that progression no
    /// longer read clearly.
    /// </summary>
    private static (int Rank, int Kind, int Depth, string Label, int Index)[] Keys(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        IReadOnlyList<string> successOrder,
        List<HashSet<int>> after)
    {
        Dictionary<string, int> rank = new(StringComparer.Ordinal);

        for (var i = 0; i < successOrder.Count; i++)
        {
            rank.TryAdd(successOrder[i], i);
        }

        var keys = new (int Rank, int Kind, int Depth, string Label, int Index)[blocks.Count];

        for (var i = 0; i < blocks.Count; i++)
        {
            var (success, members) = blocks[i];

            keys[i] = success.Length > 0
                ? (rank.GetValueOrDefault(success, int.MaxValue), 0, 0, success, i)
                : (int.MaxValue, 1, 0, members[0].Title, i);
        }

        Inherit(blocks, keys, after);

        return keys;
    }

    /// <summary>
    /// Passes an achievement's rank down onto the lone quests that follow from
    /// it, step by step.
    ///
    /// The walk starts from achievements in their order, so that a lone quest
    /// that two achievements might claim takes the rank of the first one.
    /// Depth records the distance travelled, so that a run of lone quests
    /// reads in the order they chain together and not in alphabetical order.
    ///
    /// Whatever no achievement reaches keeps its maximum rank, and so goes to
    /// the end of the list as before: the site says nothing of its place.
    /// </summary>
    private static void Inherit(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        (int Rank, int Kind, int Depth, string Label, int Index)[] keys,
        List<HashSet<int>> after)
    {
        List<int> departs = [.. Enumerable
            .Range(0, blocks.Count)
            .Where(i => blocks[i].Success.Length > 0)
            .OrderBy(i => keys[i].Rank)
            .ThenBy(i => keys[i].Label, StringComparer.Ordinal)];

        HashSet<int> vus = [.. departs];
        Queue<int> file = new(departs);

        while (file.Count > 0)
        {
            var at = file.Dequeue();

            foreach (var next in after[at])
            {
                if (blocks[next].Success.Length > 0 || !vus.Add(next))
                {
                    continue;
                }

                keys[next] = keys[next] with
                {
                    Rank = keys[at].Rank,
                    Depth = keys[at].Depth + 1,
                };

                file.Enqueue(next);
            }
        }
    }

    /// <summary>
    /// The edges between blocks: a recognised prerequisite places its block
    /// before that of the quest that claims it.
    ///
    /// The matching is done on the normalised name, as with search and with
    /// the quest chain: the site writes its prerequisites by hand. It goes
    /// through <see cref="PrerequisiteLabel"/>, because a prerequisite
    /// sometimes names a quest, sometimes the milestone it sets, sometimes an
    /// entire achievement.
    /// </summary>
    private static List<HashSet<int>> Edges(
        IReadOnlyList<QuestSummary> quests,
        List<(string Success, List<QuestSummary> Quests)> blocks,
        Dictionary<string, int> blockOfUrl,
        int[] waiting)
    {
        Dictionary<string, QuestSummary> byTitle = new(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            byTitle.TryAdd(QuestSearch.Normalize(quest.Title), quest);
        }

        // A prerequisite sometimes names an entire achievement rather than a
        // quest, "Succès Un nouveau départ réalisé". It then refers to the
        // block, which is exactly what gets sorted here.
        Dictionary<string, int> blockOfSuccess = new(StringComparer.Ordinal);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Success.Length > 0)
            {
                blockOfSuccess.TryAdd(QuestSearch.Normalize(blocks[i].Success), i);
            }
        }

        List<HashSet<int>> after = [];

        for (var i = 0; i < blocks.Count; i++)
        {
            after.Add([]);
        }

        foreach (var quest in quests)
        {
            var to = blockOfUrl[quest.Url];

            foreach (var need in quest.Prerequisites)
            {
                var named = PrerequisiteLabel.Of(need);
                var key = QuestSearch.Normalize(named.Name);

                int from;

                if (named.IsSuccess)
                {
                    if (!blockOfSuccess.TryGetValue(key, out from))
                    {
                        continue;
                    }
                }
                else if (byTitle.TryGetValue(key, out var found))
                {
                    from = blockOfUrl[found.Url];
                }
                else
                {
                    continue;
                }

                if (from == to || !after[from].Add(to))
                {
                    continue;
                }

                waiting[to]++;
            }
        }

        return after;
    }

    /// <summary>
    /// Lone quests that nothing links: neither a recognised prerequisite, nor
    /// a quest that claims them.
    ///
    /// The site says nothing of their place, and leaving them in the sort put
    /// them there at random: at the Château d'Amakna, "On recherche Ali
    /// Grothor" used to slip in between two achievements because it was the
    /// only thing the sort could pull out while a cycle blocked the second
    /// one. They therefore go to the end of the list, where they were before
    /// this sorting. One hundred and two lone quests out of three hundred and
    /// one are in this case.
    ///
    /// An achievement with no link, though, keeps its rank: that one, the site
    /// provides.
    /// </summary>
    private static HashSet<int> Lonely(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        List<HashSet<int>> after,
        int[] waiting)
    {
        HashSet<int> lonely = [];

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Success.Length == 0 && waiting[i] == 0 && after[i].Count == 0)
            {
                lonely.Add(i);
            }
        }

        return lonely;
    }

    /// <summary>
    /// The topological sort, with a fallback on cycles.
    ///
    /// Treating an achievement as an indivisible block creates a cycle as soon
    /// as two achievements claim each other through different quests. Rather
    /// than return a truncated list, the smallest remaining block in tie-break
    /// order is then taken and the process continues: the order stays total,
    /// and it falls back to the previous one wherever prerequisites contradict
    /// each other. Only six ranks are forced this way across the whole
    /// catalogue.
    /// </summary>
    private static List<QuestZoneBlock> Sort(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        (int Rank, int Kind, int Depth, string Label, int Index)[] keys,
        List<HashSet<int>> after,
        int[] waiting,
        HashSet<int> lonely)
    {
        var order = Comparer<int>.Create((first, second) => keys[first].CompareTo(keys[second]));
        SortedSet<int> left = new(order);
        SortedSet<int> ready = new(order);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (lonely.Contains(i))
            {
                continue;
            }

            left.Add(i);

            if (waiting[i] == 0)
            {
                ready.Add(i);
            }
        }

        List<QuestZoneBlock> plan = new(blocks.Count);

        while (left.Count > 0)
        {
            var at = ready.Count > 0 ? ready.Min : left.Min;

            _ = ready.Remove(at);
            _ = left.Remove(at);

            var (success, members) = blocks[at];

            plan.Add(new QuestZoneBlock(
                success,
                success.Length > 0 ? QuestPlayOrder.Sorted(members) : members));

            foreach (var next in after[at])
            {
                if (left.Contains(next) && --waiting[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        foreach (var at in lonely.Order(order))
        {
            plan.Add(new QuestZoneBlock(string.Empty, blocks[at].Quests));
        }

        return plan;
    }
}
