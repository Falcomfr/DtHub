namespace DtHub.Core.Papycha;

/// <summary>
/// A recommended entry doable by several characters; <c>Gain</c> and <c>Score</c> are summed over their own runs,
/// <c>Entry</c> is the first character's run.
/// </summary>
public sealed record TeamEntry(ProfitEntry Entry, IReadOnlyList<TrackerCharacter> Characters, QuestGain Gain, double Score, int Tier);

/// <summary>Recommended entries whose first quest is in <c>Area</c>; <c>Score</c> is their summed score.</summary>
public sealed record AreaGroup(string Area, IReadOnlyList<ProfitEntry> Entries, double Score);

/// <summary>The first <c>Count</c> entries are worth the whole level <c>From</c> to <c>To</c>.</summary>
public sealed record LevelLine(int Count, int From, int To);

/// <summary>Team, area and level views over the profitable quests ranking. Pure.</summary>
public static class QuestPlanning
{
    /// <summary>
    /// Each character's own ranking at its own level, a series matched by its name (each character's best run), a lone
    /// quest by its id, kept when two characters or more can do them; one Bonus Pack setting for the account.
    /// </summary>
    public static IReadOnlyList<TeamEntry> Team(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts,
        IReadOnlyList<(TrackerCharacter Character, TrackerProgress Progress)> characters, int bonusPercent)
    {
        var all = catalog.ToList();
        var shared = new Dictionary<string, (ProfitEntry Entry, List<TrackerCharacter> Characters, long Xp, long Kamas, double Score)>();
        foreach (var (character, progress) in characters)
        {
            var scores = QuestProfit.Score(all, facts, progress, character.Level, bonusPercent);
            var seen = new HashSet<string>();
            foreach (var entry in QuestProfit.Entries(all, scores, progress, facts, character.Level, bonusPercent))
            {
                // A run depends on the character's level and on what it did: a series matches by name, its best run only.
                var name = entry.Quests[0].SuccessName;
                var key = name.Length > 0 ? "s:" + name : "q:" + entry.Quests[0].Id;
                if (!seen.Add(key)) continue;
                var s = shared.TryGetValue(key, out var found) ? found : (entry, [], 0, 0, 0);
                s.Characters.Add(character);
                shared[key] = (s.Entry, s.Characters, s.Xp + entry.Gain.Xp, s.Kamas + entry.Gain.Kamas, s.Score + ScoreOf(entry, scores));
            }
        }

        // OrderBy is stable: ties keep the first character's order.
        var ordered = shared.Values.Where(s => s.Characters.Count >= 2).OrderByDescending(s => s.Score).ToList();
        var n = ordered.Count;
        var t3 = Math.Max(1, Share(n, 0.10));
        var t2 = t3 + Share(n, 0.15);
        var t1 = t2 + Share(n, 0.25);
        return [.. ordered.Select((s, i) => new TeamEntry(
            s.Entry, s.Characters, new QuestGain(s.Xp, s.Kamas), s.Score, i < t3 ? 3 : i < t2 ? 2 : i < t1 ? 1 : 0))];
    }

    /// <summary>
    /// The score <see cref="QuestProfit.Entries"/> ranks on: summed gain for summed effort, each side against the best
    /// scored quest's, the better side. Same formula as QuestProfit's private one, kept in step by a test.
    /// </summary>
    public static double ScoreOf(ProfitEntry entry, IReadOnlyDictionary<int, QuestScore> scores)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(scores);

        var bestXp = scores.Values.Select(s => s.Gain.Xp / s.Effort.Value).DefaultIfEmpty().Max();
        var bestKamas = scores.Values.Select(s => s.Gain.Kamas / s.Effort.Value).DefaultIfEmpty().Max();
        var xp = bestXp > 0 ? entry.Gain.Xp / entry.Effort.Value / bestXp : 0;
        var kamas = bestKamas > 0 ? entry.Gain.Kamas / entry.Effort.Value / bestKamas : 0;
        return Math.Max(xp, kamas);
    }

    private static int Share(int n, double part) => (int)Math.Round(n * part, MidpointRounding.AwayFromZero);

    /// <summary>Entries grouped by their first quest's area, best summed score first; entries keep their order.</summary>
    public static IReadOnlyList<AreaGroup> ByArea(
        IReadOnlyList<ProfitEntry> entries, Func<ProfitEntry, double> scoreOf, Func<QuestSummary, string> areaOf)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries.GroupBy(e => areaOf(e.Quests[0]))
            .Select(g => new AreaGroup(g.Key, [.. g], g.Sum(scoreOf)))
            .OrderByDescending(g => g.Score)];
    }

    /// <summary>
    /// The smallest number of first entries whose summed XP covers the XP from <paramref name="level"/> to the next;
    /// null at 200, when the table lacks either level, or when the entries never reach it.
    /// </summary>
    /// <param name="totalXpByLevel">Level to total XP needed to reach it.</param>
    public static LevelLine? WholeLevel(IReadOnlyList<ProfitEntry> ordered, int level, IReadOnlyDictionary<int, long> totalXpByLevel)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(totalXpByLevel);

        if (level >= 200 || !totalXpByLevel.TryGetValue(level, out var from) || !totalXpByLevel.TryGetValue(level + 1, out var to)
            || to <= from)
        {
            return null;
        }

        long sum = 0;
        for (var i = 0; i < ordered.Count; i++)
        {
            sum += ordered[i].Gain.Xp;
            if (sum >= to - from) return new LevelLine(i + 1, level, level + 1);
        }

        return null;
    }
}
