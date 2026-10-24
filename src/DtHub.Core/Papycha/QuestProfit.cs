using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>The estimated minutes (<see cref="QuestTime"/>) and the costliest kinds of work, at most three.</summary>
public sealed record QuestEffort(int Minutes, IReadOnlyList<string> Reasons);

public sealed record QuestGain(long Xp, long Kamas);

/// <summary>
/// <c>Score</c> is the better of XP and kamas per minute, each against the best doable quest's,
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

/// <summary>
/// A dungeon is worth the listable quests it advances, one entry per quest, for the time of its run (<c>Minutes</c>);
/// <c>Tier</c> is the one its score would have among quests.
/// </summary>
public sealed record DungeonValue(int PapychaDungeonId, IReadOnlyList<ProfitEntry> Advances, QuestGain Gain, double Score, int Tier, int Minutes);

/// <summary>What a quest gives per minute it takes, from the game's own objectives. Pure.</summary>
public static class QuestProfit
{
    private enum Kind { Boss, Items, Fights, Souls, Crafting, Travel, Dialogues, Vague }

    // Reasons are listed costliest first, so this is also the display order of equal costs.
    private static readonly Kind[] Kinds = [Kind.Boss, Kind.Fights, Kind.Items, Kind.Souls, Kind.Crafting, Kind.Vague, Kind.Travel, Kind.Dialogues];

    public static QuestEffort EffortOf(TouchQuestFacts facts, QuestSummary quest) => EffortOf([(quest, facts)]);

    // A series' minutes are its quests', each from its own start.
    private static QuestEffort EffortOf(IReadOnlyList<(QuestSummary Quest, TouchQuestFacts Facts)> quests)
    {
        var cost = new Dictionary<Kind, int>();
        var count = new Dictionary<Kind, int>();
        foreach (var (quest, facts) in quests)
        {
            ArgumentNullException.ThrowIfNull(quest);
            ArgumentNullException.ThrowIfNull(facts);

            foreach (var o in facts.Objectives)
            {
                var kind = KindOf(o);
                cost[kind] = cost.GetValueOrDefault(kind) + QuestTime.SecondsOf(o, facts);
                count[kind] = count.GetValueOrDefault(kind) + 1;
            }

            if (QuestTime.TravelSeconds(facts, QuestTime.StartOf(quest)) is > 0 and var travel)
            {
                cost[Kind.Travel] = cost.GetValueOrDefault(Kind.Travel) + travel;
            }
        }

        // Aggregated per kind with counts, costliest first, at most three.
        var reasons = Kinds.Where(cost.ContainsKey).OrderByDescending(k => cost[k]).Take(3)
            .Select(k => Reason(k, count.GetValueOrDefault(k)));
        return new QuestEffort(quests.Sum(q => QuestTime.EstimateMinutes(q.Facts, QuestTime.StartOf(q.Quest))), [.. reasons]);
    }

    private static Kind KindOf(TouchObjective o) => o.TypeId switch
    {
        1 or 9 or 2 => Kind.Dialogues,
        4 => Kind.Travel,
        3 => Kind.Items,
        20 => Kind.Crafting,
        6 when o.Parameters.Length > 1 && o.Parameters[1] == 1 => Kind.Boss,
        6 or 14 => Kind.Fights,
        12 => Kind.Souls,
        // Types 0 and 10, and any type the game adds later.
        _ => Kind.Vague,
    };

    private static string Reason(Kind kind, int n)
    {
        var key = "ProfitReason" + kind + (kind is Kind.Boss or Kind.Items or Kind.Fights && n == 1 ? "One" : "");
        return Strings.Format(key, n);
    }

    public static QuestGain? GainOf(TouchQuestFacts facts, int level, int bonusPercent) =>
        RewardOf(facts, level, bonusPercent) is { } total ? new QuestGain(total.Experience, total.Kamas) : null;

    /// <summary>The quest's whole reward at this level, null when a step lacks a formula field.</summary>
    public static QuestRewardTotal? RewardOf(TouchQuestFacts facts, int level, int bonusPercent)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var steps = facts.Steps.Select(r => r is null ? null : new TouchStep("", "", [], r)).ToList();
        return steps.Contains(null) ? null : QuestRewards.For(steps!, level, bonusPercent);
    }

    public static bool IsDoable(QuestSummary quest, TrackerProgress? progress, TouchQuestFacts facts, int level, QuestChainIndex chain) =>
        progress is not null && Continuable(quest, progress, facts, level) && Startable(quest, progress, chain);

    /// <summary>
    /// Whether the character can start the quest now: in progress, or by the site's <c>Available</c>, which it fills
    /// for its active character only. Listed: at least one previous quest done, or none named (some previous quests
    /// are alternatives, one per alignment, as in "La fatalité"); not listed while others are: no; nothing listed (the
    /// other characters): every previous quest done. Previous quests are the prerequisites naming another quest
    /// (<see cref="QuestChainIndex.Resolve"/>); the others (alignment, free text) are not checked.
    /// </summary>
    public static bool Startable(QuestSummary quest, TrackerProgress progress, QuestChainIndex chain)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(chain);

        if (QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress) return true;
        var previous = Previous(quest, chain);
        bool Done(QuestSummary p) => QuestTracking.StatusOf(progress, p.Id) == QuestStatus.Completed;
        return progress.Available.Count == 0
            ? previous.All(Done)
            : progress.Available.Contains(quest.Id) && (previous.Count == 0 || previous.Any(Done));
    }

    /// <summary>
    /// Whether the quest has a prerequisite, which keeps it out of "Recommandées": a need that is not a quest
    /// every character in <paramref name="progress"/> has completed (a time slot, an alignment, an item, or a previous
    /// quest still to do). A need naming the quest itself is ignored, as in <see cref="Previous"/>. A quest every one of
    /// them has in progress has none.
    /// </summary>
    /// <param name="met">Quests counted as done for everyone: a series run's own earlier quests.</param>
    public static bool HasPrerequisite(
        QuestSummary quest, QuestChainIndex chain, IReadOnlyCollection<TrackerProgress> progress, IReadOnlyCollection<int>? met = null)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(progress);

        // Started by every character: its conditions were met, whatever they were.
        if (progress.Count > 0 && progress.All(c => QuestTracking.StatusOf(c, quest.Id) == QuestStatus.InProgress))
        {
            return false;
        }

        return quest.Prerequisites.Any(need => chain.Resolve(need) is not { } p
            || (p.Id != quest.Id && met?.Contains(p.Id) != true
                && !progress.All(c => QuestTracking.StatusOf(c, p.Id) == QuestStatus.Completed)));
    }

    /// <summary>The quests the prerequisites name, the quest itself left out ("L'Ascension" names itself).</summary>
    public static IReadOnlyList<QuestSummary> Previous(QuestSummary quest, QuestChainIndex chain) =>
        [.. quest.Prerequisites.Select(chain.Resolve).OfType<QuestSummary>().Where(p => p.Id != quest.Id)];

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
            raw.Add((quest.Id, gain, EffortOf(f, quest)));
        }

        var best = Maxima(raw.Select(r => (r.Gain, r.Effort.Minutes)));

        // OrderBy is stable: ties keep catalog order.
        var ordered = raw.Select(r => (r.Id, r.Gain, r.Effort, Side: Combined(r.Gain, r.Effort.Minutes, best)))
            .OrderByDescending(s => s.Side.Score).ToList();

        // What the short lists leave out gets no mark ("L'éternelle moisson", 579 souls, wore a flame); still scored,
        // so it can top up a short list.
        bool Markable(int id, QuestEffort effort) => effort.Minutes <= ShortMinutes && !IsVeryLong(facts[id]);
        var n = ordered.Count(s => Markable(s.Id, s.Effort));
        var t3 = Math.Max(1, Share(n, 0.10));
        var t2 = t3 + Share(n, 0.15);
        var t1 = t2 + Share(n, 0.25);
        var result = new Dictionary<int, QuestScore>();
        var i = 0;
        foreach (var s in ordered)
        {
            var tier = !Markable(s.Id, s.Effort) ? 0 : i < t3 ? 3 : i < t2 ? 2 : i < t1 ? 1 : 0;
            i += Markable(s.Id, s.Effort) ? 1 : 0;
            result[s.Id] = new QuestScore(s.Id, s.Gain, s.Effort, s.Side.Score, tier, s.Side.ByKamas);
        }

        return result;
    }

    private static (double Xp, double Kamas) Maxima(IEnumerable<(QuestGain Gain, int Minutes)> quests) =>
        quests.Aggregate((0.0, 0.0), (m, q) => (Math.Max(m.Item1, (double)q.Gain.Xp / q.Minutes), Math.Max(m.Item2, (double)q.Gain.Kamas / q.Minutes)));

    // Each side per minute against its best doable quest; a side nobody gains on counts 0. XP wins a tie.
    private static (double Score, bool ByKamas) Combined(QuestGain gain, int minutes, (double Xp, double Kamas) best)
    {
        var xp = best.Xp > 0 ? (double)gain.Xp / minutes / best.Xp : 0;
        var kamas = best.Kamas > 0 ? (double)gain.Kamas / minutes / best.Kamas : 0;
        return (Math.Max(xp, kamas), kamas > xp);
    }

    private static int Share(int n, double part) => (int)Math.Round(n * part, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A series gives one run, from its next doable quest: it goes on with the following quests that pass every
    /// doable rule but being startable now, in play order, until one fails. A doable quest after that failure starts
    /// nothing: the series is not there yet. Every member counts in gain and effort, scored or not.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Entries(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent)
    {
        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Minutes)));
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
                var emitted = false;

                void Flush()
                {
                    if (members.Any(q => scores.ContainsKey(q.Id)))
                    {
                        entries.Add(Build(members.Count == 1 ? members[0].Title : group.Key, members, scores, facts, level, bonusPercent, best, started));
                        emitted = true;
                    }

                    members = [];
                }

                foreach (var q in QuestPlayOrder.Sorted(group))
                {
                    // One row per series: a second run made Recommandées list a split series twice.
                    if (emitted) break;
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

    // Summed gain / summed minutes against the quest maxima, so a single quest and a series compare on the same ratios.
    private static (ProfitEntry, double) Build(
        string title, List<QuestSummary> quests, IReadOnlyDictionary<int, QuestScore> scores,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent, (double Xp, double Kamas) best, bool started)
    {
        var f = quests.Select(q => facts[q.Id]).ToList();
        var gains = f.Select(x => GainOf(x, level, bonusPercent)).OfType<QuestGain>().ToList();
        var effort = EffortOf([.. quests.Select(q => (q, facts[q.Id]))]);
        var gain = new QuestGain(gains.Sum(x => x.Xp), gains.Sum(x => x.Kamas));
        var side = Combined(gain, effort.Minutes, best);
        var tier = quests.Where(q => scores.ContainsKey(q.Id)).Select(q => scores[q.Id].Tier).DefaultIfEmpty(0).Max();
        return (new ProfitEntry(title, quests, gain, effort, tier, started, side.ByKamas, side.Score), side.Score);
    }

    /// <summary>
    /// The "Succès" mode: per series, the run from its next doable quest (as <see cref="Entries"/> starts one) through
    /// its later quests not done or ignored, in play order, stopping before the first that is repeatable, blocked, above
    /// the level, of unknown gain, very long (keyring, souls), with a dungeon when <paramref name="dungeons"/> is off, or
    /// with a prerequisite (<see cref="HasPrerequisite"/>, the run's own quests counting as done); the first quest
    /// failing one gives no run. Runs of two quests or more,
    /// titled by the series, gains and minutes summed.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Runs(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, TouchQuestFacts> facts, TrackerProgress progress,
        int level, int bonusPercent, bool dungeons, IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(progress);

        var all = catalog.ToList();
        var chain = new QuestChainIndex(all);
        var names = NormalisedNames(dungeonNames);
        var runs = new List<ProfitEntry>();
        foreach (var group in all.Where(q => q.SuccessName.Length > 0).GroupBy(q => q.SuccessName))
        {
            var run = new List<QuestSummary>();
            foreach (var q in QuestPlayOrder.Sorted(group))
            {
                var known = facts.TryGetValue(q.Id, out var f);
                if (run.Count == 0)
                {
                    if (!known || !IsDoable(q, progress, f!, level, chain)) continue;
                }
                else if (QuestTracking.StatusOf(progress, q.Id) is QuestStatus.Completed or QuestStatus.Ignored) continue;
                else if (!known || !Continuable(q, progress, f!, level)) break;

                if (GainOf(f!, level, bonusPercent) is null || IsVeryLong(f!)
                    || (!dungeons && (QuestTime.HasDungeonRun(f!) || Named(f!, names).Any()))
                    || HasPrerequisite(q, chain, [progress], [.. run.Select(r => r.Id)]))
                {
                    break;
                }

                run.Add(q);
            }

            if (run.Count < 2) continue;
            var gains = run.Select(q => GainOf(facts[q.Id], level, bonusPercent)!).ToList();
            var started = group.Any(q => QuestTracking.StatusOf(progress, q.Id) is QuestStatus.Completed or QuestStatus.InProgress);
            runs.Add(new ProfitEntry(
                group.Key, run, new QuestGain(gains.Sum(g => g.Xp), gains.Sum(g => g.Kamas)),
                EffortOf([.. run.Select(q => (q, facts[q.Id]))]), 0, started, false, 0));
        }

        return runs;
    }

    /// <summary>Past this many minutes a row only tops up a short list holding fewer than <see cref="ShortFloor"/>.</summary>
    public const int ShortMinutes = 120;

    /// <summary>A short list shorter than this takes the best rows above <see cref="ShortMinutes"/>, after its own.</summary>
    public const int ShortFloor = 10;

    private static readonly string[] Keyring = ["trousseau", "keyring", "llavero"];

    // The whole dungeon list ("empty the keyring") or a soul hunt: worth it on paper, days of play in fact.
    private static bool IsVeryLong(TouchQuestFacts facts) =>
        facts.Objectives.Count(o => o.TypeId == 12) > 5
        || facts.Objectives.Any(o => o.TypeId is 0 or 10 && o.Text is { } text
            && Keyring.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The entries fit for the short lists, single quests only: a run gives its first quest, the series' next doable
    /// one, alone; a very long quest is dropped.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Recommendable(
        IReadOnlyList<ProfitEntry> entries, IReadOnlyDictionary<int, QuestScore> scores,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scores);

        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Minutes)));
        var kept = new List<ProfitEntry>();
        foreach (var entry in entries)
        {
            // A run of four quests could pass for an hour's work and take an evening.
            var first = entry.Quests[0];
            if (IsVeryLong(facts[first.Id])) continue;
            kept.Add(entry.Quests.Count == 1 ? entry : Build(first.Title, [first], scores, facts, level, bonusPercent, best, entry.Started).Item1);
        }

        return kept;
    }

    /// <summary>
    /// At most <paramref name="count"/> rows, best first on one side alone: its gain per minute, so a row never moves
    /// because another came or went. Rows gaining nothing on that side stay out; rows above <see cref="ShortMinutes"/>
    /// only top up a list of fewer than <see cref="ShortFloor"/>, after its own rows. Ties follow the catalog, then the
    /// quest id.
    /// </summary>
    public static IReadOnlyList<T> Shortlist<T>(
        IEnumerable<T> rows, Func<T, QuestGain> gainOf, Func<T, int> minutesOf, Func<T, QuestSummary> firstOf,
        IReadOnlyList<QuestSummary> catalog, bool kamas, int count = 20, int maxMinutes = ShortMinutes)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var rank = new Dictionary<int, int>();
        for (var i = 0; i < catalog.Count; i++) rank.TryAdd(catalog[i].Id, i);

        var ranked = rows
            .Select(r => (Row: r, Side: kamas ? gainOf(r).Kamas : gainOf(r).Xp, Minutes: minutesOf(r), First: firstOf(r)))
            .Where(r => r.Side > 0)
            .OrderByDescending(r => (double)r.Side / r.Minutes)
            .ThenBy(r => rank.GetValueOrDefault(r.First.Id, int.MaxValue))
            .ThenBy(r => r.First.Id)
            .ToList();
        var within = ranked.Where(r => r.Minutes <= maxMinutes).Take(count).ToList();
        var above = ranked.Where(r => r.Minutes > maxMinutes).Take(Math.Max(0, ShortFloor - within.Count));
        return [.. within.Concat(above).Select(r => r.Row)];
    }

    private static readonly HashSet<string> Articles = ["le", "la", "les", "l", "du", "des", "de", "the", "el", "los", "las", "del"];

    /// <summary>
    /// The game dungeons named in the quest's objective texts, compared without case, accents, punctuation or leading
    /// articles; a name inside a longer matched one ("Bouftous" in "Bouftous Royaux") does not count.
    /// </summary>
    public static IReadOnlyList<int> DungeonsOf(TouchQuestFacts facts, IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return DungeonsOf(facts, NormalisedNames(dungeonNames));
    }

    /// <summary>The dungeon names as they are compared, longest first: built once per call, not once per quest.</summary>
    private static List<(int Id, string Name)> NormalisedNames(IReadOnlyDictionary<int, string> dungeonNames)
    {
        ArgumentNullException.ThrowIfNull(dungeonNames);

        return [.. dungeonNames.Select(d => (d.Key, Name: Normalised(d.Value)))
            .Where(d => d.Name.Length > 0).OrderByDescending(d => d.Name.Length)];
    }

    private static IReadOnlyList<int> DungeonsOf(TouchQuestFacts facts, List<(int Id, string Name)> names) =>
        [.. Named(facts, names).Select(n => n.Id).Distinct().Order()];

    /// <summary>Each game dungeon an objective names, with that objective's step.</summary>
    private static IEnumerable<(int Id, int? Step)> Named(TouchQuestFacts facts, List<(int Id, string Name)> names) =>
        facts.Objectives.SelectMany(o => NamedBy(o, names).Select(id => (id, o.Step)));

    /// <summary>Each game dungeon one objective's text names.</summary>
    private static IEnumerable<int> NamedBy(TouchObjective o, List<(int Id, string Name)> names)
    {
        if (string.IsNullOrEmpty(o.Text)) yield break;
        var text = $" {Normalised(o.Text)} ";
        foreach (var (id, name) in names)
        {
            if (!text.Contains($" {name} ", StringComparison.Ordinal)) continue;
            yield return id;
            // The bar keeps the words around a match from forming a shorter name.
            text = text.Replace($" {name} ", " | ", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// True when the dungeon is all the quest asks (2026-10-24: "que les quêtes où il y a juste le donjon à faire"):
    /// dialogues, maps, texts naming it, and lone fights in its run or in a step naming it (its boss).
    /// </summary>
    private static bool OnlyThe(int[] dungeon, TouchQuestFacts facts, List<(int Id, string Name)> names)
    {
        bool Names(TouchObjective o) => NamedBy(o, names).Any(dungeon.Contains);

        return facts.Objectives.All(o => o.TypeId switch
        {
            1 or 9 or 2 or 4 => true,
            0 or 10 => Names(o),
            6 => o.Parameters.Length > 1 && o.Parameters[1] == 1
                && (QuestTime.IsRun(o, facts) || facts.Objectives.Any(e => e.Step is { } step && o.Step == step && Names(e))),
            _ => false,
        });
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
    /// By Papycha dungeon: the doable quests of <paramref name="entries"/> (what "Recommandées" lists) whose objectives
    /// name one of its game dungeons and ask nothing else (<see cref="OnlyThe"/>), each worth the reward of the steps naming it (an advance's title says which), not
    /// the whole quest's; summed and scored for the run's own time (<see cref="QuestTime.DungeonMinutes"/>)
    /// against the quest maxima. A dungeon with none is left out.
    /// </summary>
    /// <param name="rooms">Game dungeon id to its room count.</param>
    public static IReadOnlyDictionary<int, DungeonValue> DungeonValues(
        IReadOnlyList<ProfitEntry> entries, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent,
        IReadOnlyDictionary<int, TouchDungeonIds> dungeons, IReadOnlyDictionary<int, string> dungeonNames,
        IReadOnlyDictionary<int, int> rooms)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(dungeons);

        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Minutes)));
        var names = NormalisedNames(dungeonNames);
        // Doable now only: a run's later quests cannot be started yet, so a dungeon they name has nothing to do there.
        var named = entries.SelectMany(e => e.Quests).Where(q => scores.ContainsKey(q.Id)).DistinctBy(q => q.Id)
            .Select(q => (Quest: q, Named: Named(facts[q.Id], names).ToList()))
            .Where(q => q.Named.Count > 0).ToList();
        var result = new Dictionary<int, DungeonValue>();
        foreach (var (papychaId, ids) in dungeons)
        {
            var advances = new List<(ProfitEntry, double)>();
            foreach (var (quest, found) in named)
            {
                var steps = found.Where(n => ids.Dungeons.Contains(n.Id)).Select(n => n.Step).Distinct().Order().ToList();
                if (!OnlyThe(ids.Dungeons, facts[quest.Id], names)) continue;
                var own = facts[quest.Id].Steps;
                // A step not kept (a cache read before steps were) leaves the quest out: its whole reward would be a guess.
                if (steps.Count == 0 || steps.Any(s => s is not { } i || i < 0 || i >= own.Length)) continue;
                if (QuestRewards.For([.. steps.Select(s => new TouchStep("", "", [], own[s!.Value]))], level, bonusPercent) is not { } reward)
                {
                    continue;
                }

                var (entry, rank) = Build(quest.Title, [quest], scores, facts, level, bonusPercent, best,
                    QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress);
                var title = Strings.Format("DungeonQuestStep", quest.Title, string.Join(", ", steps.Select(s => s + 1)));
                advances.Add((entry with { Title = title, Gain = new QuestGain(reward.Experience, reward.Kamas) }, rank));
            }

            if (advances.Count == 0) continue;
            var gain = new QuestGain(advances.Sum(a => a.Item1.Gain.Xp), advances.Sum(a => a.Item1.Gain.Kamas));
            // The run, not the quests' whole time: they take the player there anyway.
            var minutes = QuestTime.DungeonMinutes(ids.Dungeons, rooms);
            var score = Combined(gain, minutes, best).Score;
            // The tier of the best quest it equals or beats: below every quest, none.
            var tier = scores.Values.Where(s => s.Score <= score).Select(s => s.Tier).DefaultIfEmpty(0).Max();
            result[papychaId] = new DungeonValue(papychaId, [.. advances.OrderByDescending(a => a.Item2).Select(a => a.Item1)], gain, score, tier, minutes);
        }

        return result;
    }
}
