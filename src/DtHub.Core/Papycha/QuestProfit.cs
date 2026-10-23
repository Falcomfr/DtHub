using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

public sealed record QuestEffort(double Value, IReadOnlyList<string> Reasons);

public sealed record QuestGain(long Xp, long Kamas);

/// <summary>
/// <c>Score</c> is the better of XP and kamas for the effort, each against the best doable quest's,
/// so 1 is the best on one side; <c>ByKamas</c> tells which side gave it.
/// </summary>
public sealed record QuestScore(int QuestId, QuestGain Gain, QuestEffort Effort, double Score, int Tier, bool ByKamas);

/// <summary>
/// A quest or a series in the "profitable quests" ranking; <c>Quests.Count &gt; 1</c> is a series. <c>Score</c> is its
/// combined score against the quest maxima, comparable with <see cref="DungeonValue.Score"/>.
/// </summary>
public sealed record ProfitEntry(
    string Title, IReadOnlyList<QuestSummary> Quests, QuestGain Gain, QuestEffort Effort, int Tier, bool Started, bool ByKamas,
    double Score);

/// <summary>A dungeon is worth the listable quests it advances, one entry per quest; <c>Tier</c> is the one its score would have among quests.</summary>
public sealed record DungeonValue(int PapychaDungeonId, IReadOnlyList<ProfitEntry> Advances, QuestGain Gain, double Score, int Tier);

/// <summary>What a quest gives for the effort it asks, from the game's own objectives. Pure.</summary>
public static class QuestProfit
{
    private enum Kind { Boss, Items, Fights, Souls, Crafting, Travel, Dialogues, Vague }

    // Reasons are listed costliest first, so this is also the display order of equal costs.
    private static readonly Kind[] Kinds = [Kind.Boss, Kind.Fights, Kind.Items, Kind.Souls, Kind.Crafting, Kind.Vague, Kind.Travel, Kind.Dialogues];

    public static QuestEffort EffortOf(TouchQuestFacts facts, int level)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return new QuestEffort(1 + facts.Objectives.Sum(o => Objective(o, level).Item2), Reasons(facts.Objectives, level));
    }

    // Aggregated per kind with counts, costliest first, at most three.
    private static List<string> Reasons(IEnumerable<TouchObjective> objectives, int level)
    {
        var cost = new Dictionary<Kind, double>();
        var count = new Dictionary<Kind, int>();
        foreach (var o in objectives)
        {
            var (kind, value) = Objective(o, level);
            cost[kind] = cost.GetValueOrDefault(kind) + value;
            count[kind] = count.GetValueOrDefault(kind) + 1;
        }

        return [.. Kinds.Where(count.ContainsKey).OrderByDescending(k => cost[k]).Take(3).Select(k => Reason(k, count[k]))];
    }

    private static (Kind, double) Objective(TouchObjective o, int level)
    {
        var p = o.Parameters;
        switch (o.TypeId)
        {
            case 1 or 9 or 2:
                return (Kind.Dialogues, 1);
            case 4:
                return (Kind.Travel, 1.5);
            case 3:
                return (Kind.Items, Math.Min(40, 2 + (p.Length > 2 ? p[2] : 0) / 10.0));
            case 20:
                return (Kind.Crafting, 3);
            case 6 or 14:
                var boss = o.TypeId == 6 && p.Length > 1 && p[1] == 1;
                return (boss ? Kind.Boss : Kind.Fights, (boss ? 8 : 3) + (o.StepOptimalLevel > level ? 4 : 0));
            case 12:
                return (Kind.Souls, 5);
            default:
                // Types 0 and 10, and any type the game adds later: unknown work is priced as vague.
                return (Kind.Vague, 6);
        }
    }

    private static string Reason(Kind kind, int n)
    {
        var key = "ProfitReason" + kind + (kind is Kind.Boss or Kind.Items or Kind.Fights && n == 1 ? "One" : "");
        return Strings.Format(key, n);
    }

    public static QuestGain? GainOf(TouchQuestFacts facts, int level, int bonusPercent)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var steps = facts.Steps.Select(r => r is null ? null : new TouchStep("", "", [], r)).ToList();
        if (steps.Contains(null)) return null;
        var total = QuestRewards.For(steps!, level, bonusPercent);
        return total is null ? null : new QuestGain(total.Experience, total.Kamas);
    }

    public static bool IsDoable(QuestSummary quest, TrackerProgress? progress, TouchQuestFacts facts, int level, QuestChainIndex chain) =>
        progress is not null && Continuable(quest, progress, facts, level)
        && (progress.Available.Contains(quest.Id) || QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress
            || PreviousDone(quest, progress, chain));

    // The site fills Available for its active character only: for the others, every previous quest done makes it startable.
    // A prerequisite naming no quest (alignment, free text) is not checked.
    private static bool PreviousDone(QuestSummary quest, TrackerProgress progress, QuestChainIndex chain) =>
        quest.Prerequisites.Select(chain.Resolve)
            .All(p => p is null || p.Id == quest.Id || QuestTracking.StatusOf(progress, p.Id) == QuestStatus.Completed);

    // Every doable rule except being startable now (Available, in progress, previous quests done).
    private static bool Continuable(QuestSummary quest, TrackerProgress progress, TouchQuestFacts facts, int level)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(facts);

        return facts.Steps.Length > 0 && facts.Steps[0] is { } first && first.OptimalLevel <= level
            && QuestTracking.StatusOf(progress, quest.Id) is not (QuestStatus.Completed or QuestStatus.Blocked or QuestStatus.Ignored)
            && !QuestRecurrenceLabel.IsRepeatable(quest);
    }

    public static IReadOnlyDictionary<int, QuestScore> Score(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts, TrackerProgress? progress,
        int level, int bonusPercent)
    {
        var all = catalog.ToList();
        var chain = new QuestChainIndex(all);
        var raw = new List<(int Id, QuestGain Gain, QuestEffort Effort)>();
        foreach (var quest in all)
        {
            if (!facts.TryGetValue(quest.Id, out var f) || !IsDoable(quest, progress, f, level, chain)) continue;
            if (GainOf(f, level, bonusPercent) is not { } gain || (gain.Xp == 0 && gain.Kamas == 0)) continue;
            raw.Add((quest.Id, gain, EffortOf(f, level)));
        }

        var best = Maxima(raw.Select(r => (r.Gain, r.Effort.Value)));

        // OrderBy is stable: ties keep catalog order.
        var ordered = raw.Select(r => (r.Id, r.Gain, r.Effort, Side: Combined(r.Gain, r.Effort.Value, best)))
            .OrderByDescending(s => s.Side.Score).ToList();
        var n = ordered.Count;
        var t3 = Math.Max(1, Share(n, 0.10));
        var t2 = t3 + Share(n, 0.15);
        var t1 = t2 + Share(n, 0.25);
        var result = new Dictionary<int, QuestScore>();
        for (var i = 0; i < n; i++)
        {
            var s = ordered[i];
            var tier = i < t3 ? 3 : i < t2 ? 2 : i < t1 ? 1 : 0;
            result[s.Id] = new QuestScore(s.Id, s.Gain, s.Effort, s.Side.Score, tier, s.Side.ByKamas);
        }

        return result;
    }

    private static (double Xp, double Kamas) Maxima(IEnumerable<(QuestGain Gain, double Effort)> quests) =>
        quests.Aggregate((0.0, 0.0), (m, q) => (Math.Max(m.Item1, q.Gain.Xp / q.Effort), Math.Max(m.Item2, q.Gain.Kamas / q.Effort)));

    // Each side against its best doable quest; a side nobody gains on counts 0. XP wins a tie.
    private static (double Score, bool ByKamas) Combined(QuestGain gain, double effort, (double Xp, double Kamas) best)
    {
        var xp = best.Xp > 0 ? gain.Xp / effort / best.Xp : 0;
        var kamas = best.Kamas > 0 ? gain.Kamas / effort / best.Kamas : 0;
        return (Math.Max(xp, kamas), kamas > xp);
    }

    private static int Share(int n, double part) => (int)Math.Round(n * part, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A series run starts at a doable quest and goes on with the following quests that pass every
    /// doable rule but being startable now, in play order, until one fails; the next doable quest starts another
    /// run. Every member counts in gain and effort, scored or not.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Entries(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent)
    {
        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Value)));
        var entries = new List<(ProfitEntry Entry, double Score)>();
        var all = catalog.ToList();
        var chain = new QuestChainIndex(all);
        var series = all.Where(q => q.SuccessName.Length > 0).GroupBy(q => q.SuccessName).Where(g => g.Count() > 1).ToList();
        var inSeries = series.SelectMany(g => g).Select(q => q.Id).ToHashSet();

        if (progress is not null)
        {
            foreach (var group in series)
            {
                var started = group.Any(q => QuestTracking.StatusOf(progress, q.Id) is QuestStatus.Completed or QuestStatus.InProgress);
                var members = new List<QuestSummary>();

                void Flush()
                {
                    if (members.Any(q => scores.ContainsKey(q.Id)))
                    {
                        entries.Add(Build(members.Count == 1 ? members[0].Title : group.Key, members, scores, facts, level, bonusPercent, best, started));
                    }

                    members = [];
                }

                foreach (var q in QuestPlayOrder.Sorted(group))
                {
                    var known = facts.TryGetValue(q.Id, out var f);
                    if (members.Count == 0)
                    {
                        if (known && IsDoable(q, progress, f!, level, chain)) members.Add(q);
                    }
                    else if (QuestTracking.StatusOf(progress, q.Id) == QuestStatus.Completed) continue;
                    else if (known && Continuable(q, progress, f!, level)) members.Add(q);
                    // Failing Continuable, it fails IsDoable too: it cannot start the next run.
                    else Flush();
                }

                Flush();
            }
        }

        foreach (var q in all.Where(q => scores.ContainsKey(q.Id) && !inSeries.Contains(q.Id)))
        {
            entries.Add(Build(q.Title, [q], scores, facts, level, bonusPercent, best, QuestTracking.StatusOf(progress, q.Id) == QuestStatus.InProgress));
        }

        // Most worth doing first; a started entry keeps its rank, the list marks it.
        return [.. entries.OrderByDescending(e => e.Score).Select(e => e.Entry)];
    }

    // Summed gain / summed effort against the quest maxima, so a single quest and a series compare on the same ratios.
    private static (ProfitEntry, double) Build(
        string title, List<QuestSummary> quests, IReadOnlyDictionary<int, QuestScore> scores,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent, (double Xp, double Kamas) best, bool started)
    {
        var f = quests.Select(q => facts[q.Id]).ToList();
        var gains = f.Select(x => GainOf(x, level, bonusPercent)).OfType<QuestGain>().ToList();
        var effort = new QuestEffort(f.Sum(x => EffortOf(x, level).Value), Reasons(f.SelectMany(x => x.Objectives), level));
        var gain = new QuestGain(gains.Sum(x => x.Xp), gains.Sum(x => x.Kamas));
        var side = Combined(gain, effort.Value, best);
        var tier = quests.Where(q => scores.ContainsKey(q.Id)).Select(q => scores[q.Id].Tier).DefaultIfEmpty(0).Max();
        return (new ProfitEntry(title, quests, gain, effort, tier, started, side.ByKamas, side.Score), side.Score);
    }

    /// <summary>The short lists cut a run here: past four quests a series is an evening, not a next step.</summary>
    public const int ShortRun = 4;

    /// <summary>Past this effort a row stays out of the short lists, however much it pays.</summary>
    public const double ShortEffort = 25;

    private static readonly string[] Keyring = ["trousseau", "keyring", "llavero"];

    // The whole dungeon list ("empty the keyring") or a soul hunt: worth it on paper, days of play in fact.
    private static bool IsVeryLong(TouchQuestFacts facts) =>
        facts.Objectives.Count(o => o.TypeId == 12) > 5
        || facts.Objectives.Any(o => o.TypeId is 0 or 10 && o.Text is { } text
            && Keyring.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The entries fit for the short lists: each run cut before its first very long quest and to its first
    /// <see cref="ShortRun"/> quests, gain and effort summed again; a run left empty is dropped.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Recommendable(
        IReadOnlyList<ProfitEntry> entries, IReadOnlyDictionary<int, QuestScore> scores,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scores);

        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Value)));
        var kept = new List<ProfitEntry>();
        foreach (var entry in entries)
        {
            var quests = entry.Quests.TakeWhile(q => !IsVeryLong(facts[q.Id])).Take(ShortRun).ToList();
            if (quests.Count == entry.Quests.Count) kept.Add(entry);
            else if (quests.Count > 0)
            {
                var title = quests.Count == 1 ? quests[0].Title : entry.Title;
                kept.Add(Build(title, quests, scores, facts, level, bonusPercent, best, entry.Started).Item1);
            }
        }

        return kept;
    }

    /// <summary>
    /// At most <paramref name="count"/> rows, best first on one side alone: its gain for the effort, so a row never moves
    /// because another came or went. Rows above <see cref="ShortEffort"/> or gaining nothing on that side stay out; ties
    /// follow the catalog, then the quest id.
    /// </summary>
    public static IReadOnlyList<T> Shortlist<T>(
        IEnumerable<T> rows, Func<T, QuestGain> gainOf, Func<T, double> effortOf, Func<T, QuestSummary> firstOf,
        IReadOnlyList<QuestSummary> catalog, bool kamas, int count = 8)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var rank = new Dictionary<int, int>();
        for (var i = 0; i < catalog.Count; i++) rank.TryAdd(catalog[i].Id, i);

        return [.. rows
            .Select(r => (Row: r, Side: kamas ? gainOf(r).Kamas : gainOf(r).Xp, Effort: effortOf(r), First: firstOf(r)))
            .Where(r => r.Side > 0 && r.Effort <= ShortEffort)
            .OrderByDescending(r => r.Side / r.Effort)
            .ThenBy(r => rank.GetValueOrDefault(r.First.Id, int.MaxValue))
            .ThenBy(r => r.First.Id)
            .Take(count)
            .Select(r => r.Row)];
    }

    private static readonly HashSet<string> Articles = ["le", "la", "les", "l", "du", "des", "de", "the", "el", "los", "las", "del"];

    /// <summary>
    /// The game dungeons named in the quest's objective texts, compared without case, accents, punctuation or leading
    /// articles; a name inside a longer matched one ("Bouftous" in "Bouftous Royaux") does not count.
    /// </summary>
    public static IReadOnlyList<int> DungeonsOf(TouchQuestFacts facts, IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(dungeonNames);

        var names = dungeonNames.Select(d => (d.Key, Name: Normalised(d.Value)))
            .Where(d => d.Name.Length > 0).OrderByDescending(d => d.Name.Length).ToList();
        var found = new SortedSet<int>();
        foreach (var o in facts.Objectives.Where(o => !string.IsNullOrEmpty(o.Text)))
        {
            var text = $" {Normalised(o.Text!)} ";
            foreach (var (id, name) in names)
            {
                if (!text.Contains($" {name} ", StringComparison.Ordinal)) continue;
                found.Add(id);
                // The bar keeps the words around a match from forming a shorter name.
                text = text.Replace($" {name} ", " | ", StringComparison.Ordinal);
            }
        }

        return [.. found];
    }

    private static string Normalised(string text)
    {
        var letters = text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray();
        return string.Join(' ', new string(letters).Split(' ', StringSplitOptions.RemoveEmptyEntries).SkipWhile(Articles.Contains));
    }

    /// <summary>
    /// By Papycha dungeon: the quests of <paramref name="entries"/> (what "Recommandées" lists) whose objectives name one
    /// of its game dungeons, summed and scored like a series against the quest maxima. A dungeon with none is left out.
    /// </summary>
    public static IReadOnlyDictionary<int, DungeonValue> DungeonValues(
        IReadOnlyList<ProfitEntry> entries, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent,
        IReadOnlyDictionary<int, TouchDungeonIds> dungeons, IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(dungeons);

        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Value)));
        var named = entries.SelectMany(e => e.Quests).DistinctBy(q => q.Id)
            .Select(q => (Quest: q, Dungeons: DungeonsOf(facts[q.Id], dungeonNames)))
            .Where(q => q.Dungeons.Count > 0).ToList();
        var result = new Dictionary<int, DungeonValue>();
        foreach (var (papychaId, ids) in dungeons)
        {
            var advances = named.Where(q => q.Dungeons.Any(ids.Dungeons.Contains))
                .Select(q => Build(q.Quest.Title, [q.Quest], scores, facts, level, bonusPercent, best,
                    QuestTracking.StatusOf(progress, q.Quest.Id) == QuestStatus.InProgress))
                .ToList();
            if (advances.Count == 0) continue;
            var gain = new QuestGain(advances.Sum(a => a.Item1.Gain.Xp), advances.Sum(a => a.Item1.Gain.Kamas));
            var score = Combined(gain, advances.Sum(a => a.Item1.Effort.Value), best).Score;
            // The tier of the best quest it equals or beats: below every quest, none.
            var tier = scores.Values.Where(s => s.Score <= score).Select(s => s.Tier).DefaultIfEmpty(0).Max();
            result[papychaId] = new DungeonValue(papychaId, [.. advances.OrderByDescending(a => a.Item2).Select(a => a.Item1)], gain, score, tier);
        }

        return result;
    }
}
