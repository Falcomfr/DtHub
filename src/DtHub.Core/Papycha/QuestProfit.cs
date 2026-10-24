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
/// A quest or a series run in the "profitable quests" ranking; <c>Quests.Count &gt; 1</c> is a run. <c>Score</c> is its
/// combined score against the quest maxima, comparable with <see cref="DungeonValue.Score"/>. <c>Path</c>, on a run
/// along one of a series' alternative ways, is that way's first quest: it names the way.
/// </summary>
public sealed record ProfitEntry(
    string Title, IReadOnlyList<QuestSummary> Quests, QuestGain Gain, QuestEffort Effort, int Tier, bool Started, bool ByKamas,
    double Score, QuestSummary? Path = null);

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
        45 or 46 => Kind.Boss,
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
    /// Whether the character can start the quest now: in progress, or not ruled out (<see cref="IsRuledOut"/>) and by
    /// the site's <c>Available</c>, which it fills for its active character only. Listed: at least one previous quest
    /// done, or none named (some previous quests are alternatives, one per alignment, as in "La fatalité"); not listed
    /// while others are: no; nothing listed (the other characters): every previous quest done. Previous quests are the
    /// prerequisites naming another quest (<see cref="QuestChainIndex.Resolve"/>), met as <see cref="IsMet"/> says; the
    /// others (alignment, free text) are not checked.
    /// </summary>
    public static bool Startable(QuestSummary quest, TrackerProgress progress, QuestChainIndex chain)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(chain);

        if (QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress) return true;
        if (IsRuledOut(quest, progress)) return false;
        var needs = QuestNeeds(quest, chain);
        bool Done(QuestSummary p) => IsDone(p, quest, progress);
        // Listed by the site: one previous quest done is enough, and one end of an achievement (rule E is for the others).
        return progress.Available.Count == 0
            ? needs.All(need => IsMet(need, quest, chain, Done))
            : progress.Available.Contains(quest.Id)
                && (needs.Count == 0 || needs.Any(need => chain.Required(need).Any(p => p.Id != quest.Id && Done(p))));
    }

    /// <summary>
    /// Rule A (2026-10-24, "une suite de quêtes ou l'autre mais pas les 2"): one of the quests the game takes in its
    /// place (<see cref="QuestSummary.Alternatives"/>) is done or started, so this one is off the character's way.
    /// </summary>
    public static bool IsRuledOut(QuestSummary quest, TrackerProgress? progress)
    {
        ArgumentNullException.ThrowIfNull(quest);

        return quest.Alternatives.Any(id => QuestTracking.StatusOf(progress, id) is QuestStatus.Completed or QuestStatus.InProgress);
    }

    /// <summary>
    /// Rule B: <paramref name="previous"/> is done, or one of its alternatives is when <paramref name="quest"/>, which
    /// needs it, is on no way of its own: "Malédiction" follows any alignment's prologue, but the alchemy way's second
    /// quest still needs the first, not the farming way's.
    /// </summary>
    internal static bool IsDone(QuestSummary previous, QuestSummary quest, TrackerProgress? progress) =>
        QuestTracking.StatusOf(progress, previous.Id) == QuestStatus.Completed
        || (quest.Alternatives.Count == 0 && previous.Alternatives.Any(id => QuestTracking.StatusOf(progress, id) == QuestStatus.Completed));

    /// <summary>
    /// Whether a need naming a quest is met, <paramref name="done"/> judging each quest it requires: the one it names,
    /// or every end of an achievement's series (rule E, <see cref="QuestChainIndex.Required"/>), the quest itself left out.
    /// </summary>
    internal static bool IsMet(string need, QuestSummary quest, QuestChainIndex chain, Func<QuestSummary, bool> done) =>
        chain.Required(need).Where(p => p.Id != quest.Id).All(done);

    /// <summary>The prerequisites naming another quest, the quest itself left out ("L'Ascension" names itself).</summary>
    internal static List<string> QuestNeeds(QuestSummary quest, QuestChainIndex chain) =>
        [.. quest.Prerequisites.Where(need => chain.Resolve(need) is { } p && p.Id != quest.Id)];

    /// <summary>
    /// Whether the quest has a prerequisite, which keeps it out of "Recommandées": a need that is not a quest
    /// every character in <paramref name="progress"/> has completed (a time slot, an alignment, an item, or a previous
    /// quest still to do; done as <see cref="IsMet"/> says, or as <see cref="Startable"/> for a quest the site lists for
    /// its one character). A need naming the quest itself is ignored, as in
    /// <see cref="Previous"/>. A quest every one of them has in progress has none.
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

        // A run's quest stands for done, and by rule B one of its alternatives.
        bool Done(QuestSummary p) =>
            (met is not null && (met.Contains(p.Id) || (quest.Alternatives.Count == 0 && p.Alternatives.Any(met.Contains))))
            || progress.All(c => IsDone(p, quest, c));
        if (quest.Prerequisites.Any(need => chain.Resolve(need) is null))
        {
            return true;
        }

        // Listed by the site for its only character: Startable's soft rule, one previous quest or one end of an
        // achievement done is enough, so a quest "Faisables" shows is not kept out of "Recommandées" and its runs.
        var needs = QuestNeeds(quest, chain);
        return progress.Count == 1 && progress.First().Available.Contains(quest.Id)
            ? needs.Count > 0 && !needs.Any(need => chain.Required(need).Any(p => p.Id != quest.Id && Done(p)))
            : needs.Any(need => !IsMet(need, quest, chain, Done));
    }

    /// <summary>The quests the prerequisites name, the quest itself left out ("L'Ascension" names itself).</summary>
    public static IReadOnlyList<QuestSummary> Previous(QuestSummary quest, QuestChainIndex chain) =>
        [.. quest.Prerequisites.Select(chain.Resolve).OfType<QuestSummary>().Where(p => p.Id != quest.Id)];

    // Every doable rule except being startable now (Available, in progress, previous quests done), rule A included.
    private static bool Continuable(QuestSummary quest, TrackerProgress progress, TouchQuestFacts facts, int level)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(facts);

        return facts.Steps.Length > 0 && facts.Steps[0] is { } first && first.OptimalLevel <= level
            && QuestTracking.StatusOf(progress, quest.Id) is not (QuestStatus.Completed or QuestStatus.Blocked or QuestStatus.Ignored)
            && !QuestRecurrenceLabel.IsRepeatable(quest) && !IsRuledOut(quest, progress);
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

        // What the short lists leave out gets no mark ("L'éternelle moisson", 579 souls, wore a flame), nor does an
        // uncertain quest, whose mark the lists hide; still scored, so it can top up a short list.
        bool Markable(int id, QuestEffort effort) =>
            effort.Minutes <= ShortMinutes && !IsVeryLong(facts[id]) && !IsUncertain(id, progress, facts[id]);
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
    /// Each scored quest as its own entry, most worth doing first (ties in catalog order). A series gives the next
    /// quest of each of its chains and ways, each startable on its own (2026-10-24: "une suite de quêtes ou l'autre").
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Entries(
        IEnumerable<QuestSummary> catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress? progress,
        IReadOnlyDictionary<int, TouchQuestFacts> facts, int level, int bonusPercent)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(scores);

        var best = Maxima(scores.Values.Select(s => (s.Gain, s.Effort.Minutes)));
        return [.. catalog.Where(q => scores.ContainsKey(q.Id))
            .Select(q => Build(q.Title, [q], scores, facts, level, bonusPercent, best, QuestTracking.StatusOf(progress, q.Id) == QuestStatus.InProgress))
            .OrderByDescending(e => e.Item2).Select(e => e.Item1)];
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
    /// The "Succès" mode: per series, the run from its next doable quest through its later quests not done or ignored,
    /// in play order, leaving out each one that is repeatable, blocked, above the level, of unknown gain, very long
    /// (keyring, souls), with a dungeon when <paramref name="dungeons"/> is off, ruled out (<see cref="IsRuledOut"/>),
    /// uncertain (<see cref="IsUncertain"/>), or
    /// with a prerequisite (<see cref="HasPrerequisite"/>, the run's own quests counting as done). Rule C: a quest left
    /// out is skipped, not an end, so what follows it falls with it and the other chains go on. Rule D: a quest
    /// left out because one of its alternatives is in the run opens a run of its own, kept when it holds that quest:
    /// one per way until the player takes one, the fork reached before or inside the run. Runs of two quests or more,
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
            var order = QuestPlayOrder.Sorted(group);
            var openers = new List<QuestSummary>();
            var tried = new List<List<QuestSummary>>();

            List<QuestSummary> RunOf(IReadOnlyList<int> off)
            {
                var run = new List<QuestSummary>();
                foreach (var q in order)
                {
                    if (off.Contains(q.Id) || !facts.TryGetValue(q.Id, out var f)) continue;
                    // Startable now or not ("Développement durable." still to do): the opener's own run decides.
                    if (q.Alternatives.Any(a => run.Exists(r => r.Id == a)))
                    {
                        if (!openers.Contains(q)) openers.Add(q);
                        continue;
                    }

                    // A quest in progress over several steps, wherever it comes: its gain and time would be guesses.
                    var fits = (run.Count == 0 ? IsDoable(q, progress, f, level, chain) : Continuable(q, progress, f, level))
                        && !IsUncertain(q.Id, progress, f);
                    if (fits && GainOf(f, level, bonusPercent) is not null && !IsVeryLong(f)
                        && (dungeons || (!QuestTime.HasDungeonRun(f) && !Named(f, names).Any()))
                        && !HasPrerequisite(q, chain, [progress], [.. run.Select(r => r.Id)]))
                    {
                        run.Add(q);
                    }
                }

                return run;
            }

            // The first run leaves nothing out; each opener's run leaves out its alternatives, the other ways.
            for (var i = -1; i < openers.Count; i++)
            {
                var opener = i < 0 ? null : openers[i];
                if (opener is not null && tried.Exists(r => r.Contains(opener))) continue;
                var run = RunOf(opener?.Alternatives ?? []);
                tried.Add(run);
                if (run.Count < 2 || (opener is not null && !run.Contains(opener))) continue;

                var gains = run.Select(q => GainOf(facts[q.Id], level, bonusPercent)!).ToList();
                var started = group.Any(q => QuestTracking.StatusOf(progress, q.Id) is QuestStatus.Completed or QuestStatus.InProgress);
                // The way's first quest: on the run, or done or started before it.
                var path = run.Exists(q => q.Alternatives.Count > 0)
                    ? order.First(q => q.Alternatives.Count > 0
                        && (run.Contains(q) || QuestTracking.StatusOf(progress, q.Id) is QuestStatus.Completed or QuestStatus.InProgress))
                    : null;
                runs.Add(new ProfitEntry(
                    group.Key, run, new QuestGain(gains.Sum(g => g.Xp), gains.Sum(g => g.Kamas)),
                    EffortOf([.. run.Select(q => (q, facts[q.Id]))]), 0, started, false, 0, path));
            }
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
        facts.Objectives.Sum(QuestTime.SoulsOf) > 5
        || facts.Objectives.Any(o => o.TypeId is 0 or 10 && o.Text is { } text
            && Keyring.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// True when the quest is in progress and has more than one step or objective: Papycha gives no step, so its gain
    /// and time are only maximums ("possiblement j'en ai déjà fait dedans mais tu le sais pas", 2026-10-24).
    /// </summary>
    public static bool IsUncertain(int questId, TrackerProgress? progress, TouchQuestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return QuestTracking.StatusOf(progress, questId) == QuestStatus.InProgress
            && (facts.Steps.Length > 1 || facts.Objectives.Length > 1);
    }

    /// <summary>
    /// The entries fit for the short lists: a very long quest or an uncertain one (<see cref="IsUncertain"/>) is dropped.
    /// </summary>
    public static IReadOnlyList<ProfitEntry> Recommendable(
        IReadOnlyList<ProfitEntry> entries, TrackerProgress? progress, IReadOnlyDictionary<int, TouchQuestFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(facts);

        return [.. entries.Where(e => !IsVeryLong(facts[e.Quests[0].Id]) && !IsUncertain(e.Quests[0].Id, progress, facts[e.Quests[0].Id]))];
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
    /// dialogues, maps, texts naming it, a boss in its dungeon (types 45, 46), and lone fights in its run or in a step
    /// naming it (its boss).
    /// </summary>
    private static bool OnlyThe(int[] dungeon, TouchQuestFacts facts, List<(int Id, string Name)> names)
    {
        bool Names(TouchObjective o) => NamedBy(o, names).Any(dungeon.Contains);

        return facts.Objectives.All(o => o.TypeId switch
        {
            1 or 9 or 2 or 4 or 45 or 46 => true,
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
    /// name one of its game dungeons and ask nothing else (<see cref="OnlyThe"/>), each worth its whole reward, since
    /// the dungeon completes it; summed and scored for the run's own time (<see cref="QuestTime.DungeonMinutes"/>)
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
        // Entries are startable quests (a series by the next quest of each chain and way), each doable now.
        var named = entries.SelectMany(e => e.Quests).Where(q => scores.ContainsKey(q.Id) && !IsUncertain(q.Id, progress, facts[q.Id]))
            .DistinctBy(q => q.Id)
            .Select(q => (Quest: q, Named: Named(facts[q.Id], names).ToList()))
            .Where(q => q.Named.Count > 0).ToList();
        var result = new Dictionary<int, DungeonValue>();
        foreach (var (papychaId, ids) in dungeons)
        {
            var advances = new List<(ProfitEntry, double)>();
            foreach (var (quest, found) in named)
            {
                // It ends with the dungeon, so the whole quest counts, as its own row would.
                if (!found.Any(n => ids.Dungeons.Contains(n.Id)) || !OnlyThe(ids.Dungeons, facts[quest.Id], names)
                    || GainOf(facts[quest.Id], level, bonusPercent) is null)
                {
                    continue;
                }

                advances.Add(Build(quest.Title, [quest], scores, facts, level, bonusPercent, best,
                    QuestTracking.StatusOf(progress, quest.Id) == QuestStatus.InProgress));
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
