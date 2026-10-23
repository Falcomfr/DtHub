using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

public enum RewardRanking
{
    Xp,
    Kamas,
}

public sealed record QuestEffort(double Value, IReadOnlyList<string> Reasons);

public sealed record QuestGain(long Xp, long Kamas);

public sealed record QuestScore(int QuestId, QuestGain Gain, QuestEffort Effort, double Score, int Tier);

/// <summary>A quest or a series in the "profitable quests" ranking; <c>Quests.Count &gt; 1</c> is a series.</summary>
public sealed record ProfitEntry(
    string Title, IReadOnlyList<QuestSummary> Quests, QuestGain Gain, QuestEffort Effort, int Tier, bool Started);

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
                return (Kind.Items, Math.Min(7, 2 + (p.Length > 2 ? p[2] : 0) / 10.0));
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

    public static bool IsDoable(QuestSummary quest, TrackerProgress? progress, TouchQuestFacts facts, int level) =>
        progress is not null && Continuable(quest, progress, facts, level)
        && (progress.Available.Contains(quest.Id) || QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress);

    // Every doable rule except membership in Available, which lists only what is startable now.
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
        int level, int bonusPercent, RewardRanking ranking)
    {
        var scored = new List<(int Id, QuestGain Gain, QuestEffort Effort, double Score)>();
        foreach (var quest in catalog)
        {
            if (!facts.TryGetValue(quest.Id, out var f) || !IsDoable(quest, progress, f, level)) continue;
            if (GainOf(f, level, bonusPercent) is not { } gain) continue;
            var value = ranking == RewardRanking.Xp ? gain.Xp : gain.Kamas;
            if (ranking == RewardRanking.Kamas && value == 0) continue;
            var effort = EffortOf(f, level);
            scored.Add((quest.Id, gain, effort, value / effort.Value));
        }

        // OrderBy is stable: ties keep catalog order.
        var ordered = scored.OrderByDescending(s => s.Score).ToList();
        var n = ordered.Count;
        var t3 = Math.Max(1, Share(n, 0.10));
        var t2 = t3 + Share(n, 0.15);
        var t1 = t2 + Share(n, 0.25);
        var result = new Dictionary<int, QuestScore>();
        for (var i = 0; i < n; i++)
        {
            var s = ordered[i];
            var tier = i < t3 ? 3 : i < t2 ? 2 : i < t1 ? 1 : 0;
            result[s.Id] = new QuestScore(s.Id, s.Gain, s.Effort, s.Score, tier);
        }

        return result;
    }

    private static int Share(int n, double part) => (int)Math.Round(n * part, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A series run starts at a doable quest and goes on with the following quests that pass every
    /// doable rule but Available, in play order, until one fails; the next doable quest starts another
    /// run. Every member counts in gain and effort, scored or not.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Entries(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent, RewardRanking ranking)
    {
        var entries = new List<(ProfitEntry Entry, double Score)>();
        var all = catalog.ToList();
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
                        entries.Add(Build(members.Count == 1 ? members[0].Title : group.Key, members, scores, facts, level, bonusPercent, ranking, started));
                    }

                    members = [];
                }

                foreach (var q in QuestPlayOrder.Sorted(group))
                {
                    var known = facts.TryGetValue(q.Id, out var f);
                    if (members.Count == 0)
                    {
                        if (known && IsDoable(q, progress, f!, level)) members.Add(q);
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
            entries.Add(Build(q.Title, [q], scores, facts, level, bonusPercent, ranking, QuestTracking.StatusOf(progress, q.Id) == QuestStatus.InProgress));
        }

        return [.. entries.OrderByDescending(e => e.Entry.Started).ThenByDescending(e => e.Score).Select(e => e.Entry)];
    }

    // Score = summed gain / summed effort, so a single quest and a series compare on the same ratio.
    private static (ProfitEntry, double) Build(
        string title, List<QuestSummary> quests, IReadOnlyDictionary<int, QuestScore> scores,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent, RewardRanking ranking, bool started)
    {
        var f = quests.Select(q => facts[q.Id]).ToList();
        var gains = f.Select(x => GainOf(x, level, bonusPercent)).OfType<QuestGain>().ToList();
        var effort = new QuestEffort(f.Sum(x => EffortOf(x, level).Value), Reasons(f.SelectMany(x => x.Objectives), level));
        var gain = new QuestGain(gains.Sum(x => x.Xp), gains.Sum(x => x.Kamas));
        var value = ranking == RewardRanking.Xp ? gain.Xp : gain.Kamas;
        var tier = quests.Where(q => scores.ContainsKey(q.Id)).Max(q => scores[q.Id].Tier);
        return (new ProfitEntry(title, quests, gain, effort, tier, started), value / effort.Value);
    }
}
