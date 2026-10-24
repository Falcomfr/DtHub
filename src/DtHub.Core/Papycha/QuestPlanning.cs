namespace DtHub.Core.Papycha;

/// <summary>
/// A recommended entry doable by several characters; <c>Gain</c> and <c>Score</c> are summed over their own runs,
/// <c>Entry</c> is the first character's run.
/// </summary>
public sealed record TeamEntry(ProfitEntry Entry, IReadOnlyList<TrackerCharacter> Characters, QuestGain Gain, double Score, int Tier);

/// <summary>The first <c>Count</c> entries are worth the whole level <c>From</c> to <c>To</c>.</summary>
public sealed record LevelLine(int Count, int From, int To);

/// <summary>Team and level views over the profitable quests ranking. Pure.</summary>
public static class QuestPlanning
{
    /// <summary>
    /// Each character's recommendable quests at its own level, matched by their id, kept when two characters or more can
    /// do them; one Bonus Pack setting for the account.
    /// </summary>
    public static IReadOnlyList<TeamEntry> Team(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts,
        IReadOnlyList<(TrackerCharacter Character, TrackerProgress Progress)> characters, int bonusPercent)
    {
        var all = catalog.ToList();
        var shared = new Dictionary<int, (ProfitEntry Entry, List<TrackerCharacter> Characters, long Xp, long Kamas, double Score)>();
        foreach (var (character, progress) in characters)
        {
            var scores = QuestProfit.Score(all, facts, progress, character.Level, bonusPercent);
            var entries = QuestProfit.Entries(all, scores, progress, facts, character.Level, bonusPercent);
            foreach (var entry in QuestProfit.Recommendable(entries, scores, facts, character.Level, bonusPercent))
            {
                // Recommendable entries are single quests, each once per character.
                var key = entry.Quests[0].Id;
                var s = shared.TryGetValue(key, out var found) ? found : (entry, [], 0, 0, 0);
                s.Characters.Add(character);
                shared[key] = (s.Entry, s.Characters, s.Xp + entry.Gain.Xp, s.Kamas + entry.Gain.Kamas, s.Score + entry.Score);
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
    /// The "Succès" mode for the team: each character's own run of a series (<see cref="QuestProfit.Runs"/>) at its
    /// level, kept when two characters or more have one; gains summed, <c>Entry</c> the longest run (the first
    /// character's on a tie), so its minutes are the team's.
    /// </summary>
    public static IReadOnlyList<TeamEntry> TeamRuns(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts,
        IReadOnlyList<(TrackerCharacter Character, TrackerProgress Progress)> characters, int bonusPercent,
        bool withPrerequisites, bool dungeons, IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(characters);

        var all = catalog.ToList();
        var shared = new Dictionary<string, (ProfitEntry Entry, List<TrackerCharacter> Characters, long Xp, long Kamas)>();
        foreach (var (character, progress) in characters)
        {
            foreach (var run in QuestProfit.Runs(all, facts, progress, character.Level, bonusPercent, withPrerequisites, dungeons, dungeonNames))
            {
                var s = shared.TryGetValue(run.Title, out var found) ? found : (run, [], 0, 0);
                s.Characters.Add(character);
                var longest = run.Effort.Minutes > s.Entry.Effort.Minutes ? run : s.Entry;
                shared[run.Title] = (longest, s.Characters, s.Xp + run.Gain.Xp, s.Kamas + run.Gain.Kamas);
            }
        }

        return [.. shared.Values.Where(s => s.Characters.Count >= 2)
            .Select(s => new TeamEntry(s.Entry, s.Characters, new QuestGain(s.Xp, s.Kamas), 0, 0))];
    }

    private static int Share(int n, double part) => (int)Math.Round(n * part, MidpointRounding.AwayFromZero);

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
