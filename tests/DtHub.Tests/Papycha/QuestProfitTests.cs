using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestProfitTests
{
    private static TouchObjective Obj(int type, int[]? p = null, int level = 10) => new(type, p ?? [], level);

    private static TouchQuestFacts Facts(params TouchObjective[] objectives) => new([Step(10, 1f)], objectives);

    private static TouchStepReward Step(int level, float xp, float kamas = 0f) =>
        new(level, 1f, xp, kamas, false, []);

    private static TrackerProgress Progress(
        IEnumerable<int> available, Dictionary<int, QuestStatus>? statuses = null, IEnumerable<int>? completed = null) =>
        new(statuses ?? [], (completed ?? []).ToHashSet(), available.ToHashSet(), []);

    private static QuestSummary Quest(int id, string success = "", int order = 0, QuestRecurrence rec = QuestRecurrence.Once) =>
        new() { Id = id, Title = "Q" + id, SuccessName = success, PlayOrder = order, Recurrence = rec };

    [Fact]
    public void Effort_is_the_quest_time_from_its_start()
    {
        var facts = new TouchQuestFacts([Step(10, 1f)], [new(14, [1, 5], 10, "", 4, 0, 0)]);
        var quest = Quest(1) with { StartPosition = "[0,0]" };

        Assert.Equal(QuestTime.EstimateMinutes(facts, (0, 0)), QuestProfit.EffortOf(facts, quest).Minutes);
        Assert.Equal(9, QuestProfit.EffortOf(facts, quest).Minutes);
    }

    [Fact]
    public void Effort_reasons_are_aggregated_and_costliest_first_at_most_three()
    {
        var effort = QuestProfit.EffortOf(
            Facts(Obj(1), Obj(1), Obj(3, [1, 2, 3]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 1]), Obj(12)), Quest(1));
        Assert.Equal(3, effort.Reasons.Count);
        // Ranked on their time: 3 x 300 s, 300 s, then the 70 s of items over the lone fight's 60 s.
        Assert.Equal(["3 combats", "âmes", "1 objet"], effort.Reasons);
    }

    [Fact]
    public void Effort_reasons_name_dialogues_and_vague()
    {
        Assert.Equal(["objectif vague", "dialogues"], QuestProfit.EffortOf(Facts(Obj(1), Obj(0)), Quest(1)).Reasons);
        Assert.Equal(["1 objet"], QuestProfit.EffortOf(Facts(Obj(3, [1, 2, 1])), Quest(1)).Reasons);
    }

    [Fact]
    public void A_long_way_between_objectives_names_travel()
    {
        var far = Facts(new TouchObjective(1, [1], 10, "", 30, 0, 0), new TouchObjective(1, [1], 10, "", 0, 0, 0));

        Assert.Equal(["déplacements", "dialogues"], QuestProfit.EffortOf(far, Quest(1)).Reasons);
    }

    [Fact]
    public void Gain_is_null_when_a_step_is_missing_and_summed_otherwise()
    {
        Assert.Null(QuestProfit.GainOf(new([Step(10, 1f), null], []), 10, 0));
        var one = QuestProfit.GainOf(new([Step(10, 1f, 1f)], []), 10, 0)!;
        var two = QuestProfit.GainOf(new([Step(10, 1f, 1f), Step(10, 1f, 1f)], []), 10, 0)!;
        Assert.True(one.Xp > 0 && one.Kamas > 0);
        Assert.Equal(one.Xp * 2, two.Xp);
        Assert.Equal(one.Kamas * 2, two.Kamas);
    }

    [Fact]
    public void Doable_rules_one_by_one()
    {
        var facts = Facts();
        var chain = new QuestChainIndex([]);
        var progress = Progress([1, 2, 3, 4, 5, 6], new()
        {
            [2] = QuestStatus.Blocked,
            [3] = QuestStatus.Ignored,
            [4] = QuestStatus.InProgress,
            [7] = QuestStatus.InProgress,
        }, [5]);
        Assert.True(QuestProfit.IsDoable(Quest(1), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(2), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(3), progress, facts, 10, chain));
        Assert.True(QuestProfit.IsDoable(Quest(4), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(5), progress, facts, 10, chain));
        Assert.True(QuestProfit.IsDoable(Quest(7), progress, facts, 10, chain));
        // The site's list stands for its active character: quest 8 is out of it.
        Assert.False(QuestProfit.IsDoable(Quest(8), progress, facts, 10, chain));
        Assert.True(QuestProfit.IsDoable(Quest(8), Progress([]), facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1), progress, facts, 9, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1, rec: QuestRecurrence.Daily), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1) with { SectionIds = [QuestRecurrenceLabel.RepeatableSection] }, progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1), null, facts, 10, chain));
    }

    [Fact]
    public void Out_of_available_a_quest_is_doable_once_its_previous_quests_are_completed()
    {
        var catalog = new List<QuestSummary>
        {
            Quest(1),
            Quest(2) with { Prerequisites = ["Q1"] },
            Quest(3) with { Prerequisites = ["Q1", "Q6"] },
            Quest(4) with { Prerequisites = ["Alignement bontarien 40 minimum."] },
            Quest(5) with { Prerequisites = ["Q6"] },
            Quest(6),
        };
        var facts = catalog.ToDictionary(q => q.Id, _ => Facts());
        // An empty list: not the site's active character, startable is read from the previous quests.
        var scores = QuestProfit.Score(catalog, facts, Progress([], completed: [1]), 10, 0);
        Assert.Equal([2, 4, 6], scores.Keys.Order());
    }

    [Fact]
    public void For_the_site_s_active_character_a_quest_needs_its_list_and_its_previous_quests()
    {
        var catalog = new List<QuestSummary> { Quest(1), Quest(2) with { Prerequisites = ["Q1"] }, Quest(5) with { Prerequisites = ["Q6"] }, Quest(6) };
        var facts = catalog.ToDictionary(q => q.Id, _ => Facts());

        // 2 is not listed, 5 is but 6 is not done: its lock would say unmet on a recommended row.
        Assert.Empty(QuestProfit.Score(catalog, facts, Progress([5], completed: [1]), 10, 0));
        Assert.Equal([5], QuestProfit.Score(catalog, facts, Progress([5], completed: [1, 6]), 10, 0).Keys);
    }

    [Fact]
    public void Startable_follows_the_site_s_list_and_the_previous_quests()
    {
        // 2 is "Tour de l'île" (one previous); 3 is "La fatalité" (two previous, alternatives by alignment); 5 names
        // itself ("L'Ascension"); 6 names no quest.
        var catalog = new List<QuestSummary>
        {
            Quest(1), Quest(2) with { Prerequisites = ["Q1"] }, Quest(3) with { Prerequisites = ["Q1", "Q4"] }, Quest(4),
            Quest(5) with { Prerequisites = ["Q5"] }, Quest(6) with { Prerequisites = ["Alignement bontarien 40 minimum."] },
        };
        var chain = new QuestChainIndex(catalog);
        bool Startable(int id, TrackerProgress progress) => QuestProfit.Startable(catalog.First(q => q.Id == id), progress, chain);

        // Listed: one previous done is enough, none done is not.
        Assert.False(Startable(2, Progress([2])));
        Assert.True(Startable(2, Progress([2], completed: [1])));
        Assert.True(Startable(3, Progress([3], completed: [1])));
        Assert.False(Startable(3, Progress([3])));
        // Not listed by the site: never, unless in progress.
        Assert.False(Startable(3, Progress([2], completed: [1, 4])));
        Assert.True(Startable(3, Progress([2], new() { [3] = QuestStatus.InProgress })));
        // Nothing listed (another character): every previous done.
        Assert.False(Startable(3, Progress([], completed: [1])));
        Assert.True(Startable(3, Progress([], completed: [1, 4])));
        // Itself and free text name no previous quest.
        Assert.True(Startable(5, Progress([5])));
        Assert.True(Startable(5, Progress([])));
        Assert.True(Startable(6, Progress([6])));
    }

    [Fact]
    public void A_prerequisite_is_a_need_that_is_not_a_quest_done_by_every_character()
    {
        // 2 follows 1; 3 asks a time slot ("Blessure de chasse"); 4 names itself; 5 has no need.
        var catalog = new List<QuestSummary>
        {
            Quest(1), Quest(2) with { Prerequisites = ["Q1"] }, Quest(3) with { Prerequisites = ["Être dans les créneaux horaires"] },
            Quest(4) with { Prerequisites = ["Q4"] }, Quest(5),
        };
        var chain = new QuestChainIndex(catalog);
        bool Has(int id, params TrackerProgress[] team) => QuestProfit.HasPrerequisite(catalog.First(q => q.Id == id), chain, team);
        var done = Progress([], completed: [1]);
        var notDone = Progress([]);

        Assert.True(Has(3, done));
        Assert.False(Has(2, done));
        Assert.True(Has(2, notDone));
        Assert.False(Has(4, notDone));
        Assert.False(Has(5, notDone));
        // Team: every character of the row must have done the previous quest.
        Assert.False(Has(2, done, done));
        Assert.True(Has(2, done, notDone));
        // A quest already in progress has passed its conditions, for every character of the row only.
        var started = new TrackerProgress(new Dictionary<int, QuestStatus> { [3] = QuestStatus.InProgress }, new HashSet<int>(), new HashSet<int>(), []);
        Assert.False(Has(3, started));
        Assert.False(Has(3, started, started));
        Assert.True(Has(3, started, done));
    }

    private static (List<QuestSummary> Catalog, Dictionary<int, TouchQuestFacts> Facts) Twenty()
    {
        var catalog = Enumerable.Range(1, 20).Select(i => Quest(i)).ToList();
        // Quest i gives i times the XP of quest 1 for the same effort.
        var facts = catalog.ToDictionary(q => q.Id, q => new TouchQuestFacts([Step(10, q.Id)], []));
        return (catalog, facts);
    }

    [Fact]
    public void Score_and_tiers_on_twenty_quests()
    {
        var (catalog, facts) = Twenty();
        var scores = QuestProfit.Score(catalog, facts, Progress(catalog.Select(q => q.Id)), 10, 0);
        Assert.Equal(20, scores.Count);
        Assert.Equal(2, scores.Values.Count(s => s.Tier == 3));
        Assert.Equal(3, scores.Values.Count(s => s.Tier == 2));
        Assert.Equal(5, scores.Values.Count(s => s.Tier == 1));
        Assert.All([20, 19], id => Assert.Equal(3, scores[id].Tier));
        Assert.Equal(2, scores[17].Tier);
        Assert.Equal(1, scores[14].Tier);
        Assert.Equal(1, scores[11].Tier);
        Assert.Equal(0, scores[10].Tier);
        Assert.Equal(1.0, scores[20].Score);
        Assert.Equal(19.0 / 20, scores[19].Score, 2);
        Assert.False(scores[20].ByKamas);
    }

    [Fact]
    public void A_quest_the_short_lists_leave_out_gets_no_mark_but_stays_scored()
    {
        // The best gains, but six souls, over two hours of items, or the keyring; quest 4 is the only one left.
        var catalog = new List<QuestSummary> { Quest(1), Quest(2), Quest(3), Quest(4) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 9f)], [.. Enumerable.Repeat(Obj(12), 6)]),
            [2] = new([Step(10, 900f)], [.. Enumerable.Repeat(Obj(3, [1, 2, 60]), 6)]),
            [3] = new([Step(10, 9f)], [new TouchObjective(0, [1], 10, "Vider le trousseau")]),
            [4] = new([Step(10, 1f)], []),
        };

        var scores = QuestProfit.Score(catalog, facts, Progress([1, 2, 3, 4]), 10, 0);

        Assert.True(scores[2].Effort.Minutes > QuestProfit.ShortMinutes);
        Assert.Equal([0, 0, 0, 3], scores.Keys.Order().Select(id => scores[id].Tier));
    }

    [Fact]
    public void Score_ties_keep_catalog_order_and_a_lone_top_quest_is_tier_three()
    {
        var catalog = new List<QuestSummary> { Quest(1), Quest(2), Quest(3) };
        var facts = catalog.ToDictionary(q => q.Id, _ => new TouchQuestFacts([Step(10, 1f)], []));
        var scores = QuestProfit.Score(catalog, facts, Progress([1, 2, 3]), 10, 0);
        Assert.Equal(3, scores[1].Tier);
        Assert.Equal(1, scores[2].Tier);
        Assert.Equal(0, scores[3].Tier);
    }

    [Fact]
    public void A_quest_best_in_kamas_but_poor_in_xp_is_tier_three()
    {
        var catalog = Enumerable.Range(1, 20).Select(i => Quest(i)).ToList();
        // Quests 1 to 19 give i XP and no kamas; quest 20 gives the least XP but the only kamas.
        var facts = catalog.ToDictionary(q => q.Id, q => new TouchQuestFacts([Step(10, q.Id == 20 ? 1f : q.Id, q.Id == 20 ? 1f : 0f)], []));
        var scores = QuestProfit.Score(catalog, facts, Progress(catalog.Select(q => q.Id)), 10, 0);
        Assert.Equal(3, scores[20].Tier);
        Assert.True(scores[20].ByKamas);
        Assert.Equal(3, scores[19].Tier);
        Assert.False(scores[19].ByKamas);
        Assert.Equal(2, scores[18].Tier);
    }

    [Fact]
    public void A_quest_with_neither_xp_nor_kamas_is_not_scored()
    {
        var catalog = new List<QuestSummary> { Quest(1), Quest(2), Quest(3) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 0f, 0f)], []),
            [2] = new([Step(10, 1f, 0f)], []),
            [3] = new([Step(10, 0f, 1f)], []),
        };
        var scores = QuestProfit.Score(catalog, facts, Progress([1, 2, 3]), 10, 0);
        Assert.Equal([2, 3], scores.Keys.Order());
        Assert.Equal(1.0, scores[2].Score);
        Assert.Equal(1.0, scores[3].Score);
    }

    private static TouchQuestFacts Xp(float xp) => new([Step(10, xp)], [Obj(1)]);

    private static long XpOf(TouchQuestFacts facts) => QuestProfit.GainOf(facts, 10, 0)!.Xp;

    private static IReadOnlyList<ProfitEntry> Entries(
        QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts>? facts = null)
    {
        facts ??= catalog.ToDictionary(q => q.Id, _ => Facts(Obj(1)));
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        return QuestProfit.Entries(catalog, scores, progress, facts, 10, 0);
    }

    [Fact]
    public void Series_starts_at_the_first_doable_quest_and_skips_leading_done_ones()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f), [2] = Xp(2f), [3] = Xp(3f) };
        var entry = Assert.Single(Entries(catalog, Progress([2], completed: [1]), facts));
        Assert.Equal("S", entry.Title);
        Assert.Equal([2, 3], entry.Quests.Select(q => q.Id));
        Assert.True(entry.Started);
        Assert.Equal(XpOf(facts[2]) + XpOf(facts[3]), entry.Gain.Xp);
    }

    [Fact]
    public void Series_lists_later_quests_even_when_only_the_first_is_available()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f), [2] = Xp(2f), [3] = Xp(3f) };
        var entry = Assert.Single(Entries(catalog, Progress([1]), facts));
        Assert.Equal([1, 2, 3], entry.Quests.Select(q => q.Id));
        // Three quests of 180 + 30 s, 4 minutes each.
        Assert.Equal(12, entry.Effort.Minutes);
        Assert.Equal(XpOf(facts[1]) + XpOf(facts[2]) + XpOf(facts[3]), entry.Gain.Xp);
    }

    [Fact]
    public void A_blocked_or_over_level_quest_ends_the_series_entry_and_nothing_after_it_starts_another()
    {
        // One entry per series, from its next doable quest: 3 is doable too, but the series is not there yet.
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4, "S", 4) };
        var blocked = Progress([1, 3], new() { [2] = QuestStatus.Blocked });
        Assert.Equal([1], Assert.Single(Entries(catalog, blocked)).Quests.Select(q => q.Id));

        var facts = catalog.ToDictionary(q => q.Id, _ => Facts(Obj(1)));
        facts[2] = new([Step(20, 1f)], []);
        Assert.Equal(["Q1"], Entries(catalog, Progress([1, 3]), facts).Select(e => e.Title));
    }

    [Fact]
    public void A_series_split_by_a_blocked_quest_gives_one_recommended_row()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4, "S", 4) };
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        var row = Assert.Single(Recommendable(catalog, Progress([1, 3], new() { [2] = QuestStatus.Blocked }), facts));

        Assert.Equal("Q1", row.Title);
    }

    [Fact]
    public void Series_follow_the_house_play_order()
    {
        // A zero play order is unknown and goes last.
        var catalog = new[] { Quest(1, "S", 0), Quest(2, "S", 1) };
        Assert.Equal([2, 1], Assert.Single(Entries(catalog, Progress([1, 2]))).Quests.Select(q => q.Id));
    }

    [Fact]
    public void Unscored_members_stay_in_the_series_and_add_no_gain()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 1f, 1f)], []),
            [2] = new([Step(10, 1f, 1f), null], []),
            [3] = new([Step(10, 1f, 1f)], []),
        };
        var progress = Progress([1, 2, 3]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        Assert.Equal(2, scores.Count);
        var entry = Assert.Single(QuestProfit.Entries(catalog, scores, progress, facts, 10, 0));
        Assert.Equal([1, 2, 3], entry.Quests.Select(q => q.Id));
        Assert.Equal(scores[1].Gain.Kamas + scores[3].Gain.Kamas, entry.Gain.Kamas);
        Assert.Equal(scores[1].Gain.Xp + scores[3].Gain.Xp, entry.Gain.Xp);
    }

    [Fact]
    public void Series_reasons_are_aggregated_per_kind_across_members()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = Facts(Obj(6, [1, 5]), Obj(1)),
            [2] = Facts(Obj(6, [1, 5]), Obj(1)),
        };
        var progress = Progress([1, 2]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        var entry = Assert.Single(QuestProfit.Entries(catalog, scores, progress, facts, 10, 0));
        Assert.Equal(["2 combats", "dialogues"], entry.Effort.Reasons);
    }

    [Fact]
    public void Crafting_and_travel_have_their_own_reasons()
    {
        Assert.Equal(["fabrication"], QuestProfit.EffortOf(Facts(Obj(20)), Quest(1)).Reasons);
        Assert.Equal(["déplacements"], QuestProfit.EffortOf(Facts(Obj(4)), Quest(1)).Reasons);
    }

    [Fact]
    public void Entries_rank_on_summed_gain_for_summed_effort_started_or_not()
    {
        var catalog = new[]
        {
            Quest(1, "A", 1), Quest(2, "A", 2), Quest(3), Quest(4, "B", 1), Quest(5, "B", 2),
        };
        // A gains 6 for 4 of effort, below Q3's 4 for 2, though its two scores add up above Q3's.
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(3f), [2] = Xp(3f), [3] = Xp(4f), [4] = Xp(1f), [5] = Xp(1f) };
        var entries = Entries(catalog, Progress([1, 2, 3, 4, 5], new() { [4] = QuestStatus.InProgress }), facts);
        Assert.Equal(["Q3", "A", "B"], entries.Select(e => e.Title));
        Assert.Equal([false, false, true], entries.Select(e => e.Started));
    }

    [Fact]
    public void Series_rank_on_their_best_side_against_the_quest_maxima()
    {
        var catalog = new[] { Quest(1, "K", 1), Quest(2, "K", 2), Quest(3), Quest(4) };
        // K: little XP, kamas 4 for 4 of effort against the best quest's 3 for 2; Q3: a quarter of Q4's XP.
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 0.1f, 1f)], [Obj(1)]),
            [2] = new([Step(10, 0.1f, 3f)], [Obj(1)]),
            [3] = Xp(1f),
            [4] = Xp(4f),
        };
        var entries = Entries(catalog, Progress([1, 2, 3, 4]), facts);
        Assert.Equal(["Q4", "K", "Q3"], entries.Select(e => e.Title));
        Assert.Equal([false, true, false], entries.Select(e => e.ByKamas));
    }

    private static TouchObjective Said(string text, int step = 0) => new(4, [1], 10, text, Step: step);

    [Fact]
    public void Dungeons_of_a_quest_match_names_without_case_accents_punctuation_or_leading_articles()
    {
        var names = new Dictionary<int, string>
        {
            [1] = "Donjon Fungus",
            [2] = "La Serre du Royalmouth",
            [3] = "Donjon des Écailles",
            [4] = "Grotte",
        };
        Assert.Equal([1], QuestProfit.DungeonsOf(Facts(Said("Découvrir la carte Sortie du Donjon Fungus")), names));
        Assert.Equal([2], QuestProfit.DungeonsOf(Facts(Said("Braver les dangers de la Serre du Royalmouth.")), names));
        Assert.Equal([3], QuestProfit.DungeonsOf(Facts(Said("Vaincre le DONJON des ecailles !")), names));
        Assert.Equal([1, 3], QuestProfit.DungeonsOf(Facts(Said("Donjon Fungus"), Obj(1), Said("donjon des écailles")), names));
        Assert.Empty(QuestProfit.DungeonsOf(Facts(Said("Les Grottes Hesques"), Obj(1)), names));
    }

    [Fact]
    public void A_dungeon_name_inside_a_longer_one_loses_to_it()
    {
        var names = new Dictionary<int, string> { [1] = "Donjon des Bouftous", [2] = "Donjon des Bouftous Royaux" };
        Assert.Equal([2], QuestProfit.DungeonsOf(Facts(Said("Sortie du Donjon des Bouftous Royaux")), names));
        Assert.Equal([1], QuestProfit.DungeonsOf(Facts(Said("Sortie du Donjon des Bouftous")), names));
    }

    [Fact]
    public void English_and_spanish_leading_articles_are_dropped_too()
    {
        var names = new Dictionary<int, string> { [1] = "The Flooded Chapel", [2] = "Los Pantanos del Sur" };
        Assert.Equal([1], QuestProfit.DungeonsOf(Facts(Said("Exit of Flooded Chapel")), names));
        Assert.Equal([2], QuestProfit.DungeonsOf(Facts(Said("Salida pantanos del sur")), names));
    }

    [Fact]
    public void Entries_carry_their_score_in_order_and_a_dungeon_scores_its_quest_s_gain_for_its_run()
    {
        var catalog = new[] { Quest(1, "A", 1), Quest(2, "A", 2), Quest(3), Quest(4) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = Xp(3f),
            [2] = Xp(3f),
            [3] = new([Step(10, 4f, 1f)], [Said("Donjon Fungus")]),
            [4] = Xp(1f),
        };
        var progress = Progress([1, 2, 3, 4]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        var entries = QuestProfit.Entries(catalog, scores, progress, facts, 10, 0);
        var values = QuestProfit.DungeonValues(
            entries, scores, progress, facts, 10, 0, new Dictionary<int, TouchDungeonIds> { [7] = new([70]) },
            new Dictionary<int, string> { [70] = "Donjon Fungus" }, new Dictionary<int, int> { [70] = 1 });

        Assert.Equal(entries.Select(e => e.Score).OrderByDescending(x => x), entries.Select(e => e.Score));
        Assert.True(entries[0].Score > 0);
        // Q3 takes 4 min, the one-room run 5: same gain, four fifths of the score.
        Assert.Equal(5, values[7].Minutes);
        Assert.Equal(entries.Single(e => e.Title == "Q3").Score * 4 / 5, values[7].Score, 6);
    }

    // Papycha dungeon n pairs with game dungeon 10n.
    private static IReadOnlyDictionary<int, DungeonValue> DungeonValues(
        QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts, Dictionary<int, string> names,
        Dictionary<int, int>? rooms = null)
    {
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        var entries = QuestProfit.Entries(catalog, scores, progress, facts, 10, 0);
        var table = names.Keys.ToDictionary(id => id / 10, id => new TouchDungeonIds([id]));
        return QuestProfit.DungeonValues(entries, scores, progress, facts, 10, 0, table, names, rooms ?? []);
    }

    [Fact]
    public void A_dungeon_is_worth_the_listable_quests_it_advances()
    {
        var catalog = new[] { Quest(1), Quest(2), Quest(3), Quest(4), Quest(5, "S", 1), Quest(6, "S", 2) with { Prerequisites = ["Q5"] } };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 2f, 1f)], [Said("Sortie du Donjon Fungus"), Obj(1)]),
            [2] = new([Step(10, 1f)], [Said("Serre du Royalmouth")]),
            [3] = Xp(1f),
            // Done: not listable, so it adds nothing.
            [4] = new([Step(10, 5f)], [Said("Sortie du Donjon Fungus")]),
            [5] = Xp(1f),
            // Listed as the series' next quest, but not startable yet: nothing to do there now.
            [6] = new([Step(10, 3f)], [Said("Donjon Fungus")]),
        };
        var names = new Dictionary<int, string> { [70] = "Donjon Fungus", [80] = "Serre du Royalmouth", [90] = "Donjon des Craqueleurs" };

        var values = DungeonValues(catalog, Progress([1, 2, 3, 5], new() { [1] = QuestStatus.InProgress }, completed: [4]), facts, names);

        Assert.Equal([7, 8], values.Keys.Order());
        var fungus = values[7];
        Assert.Equal(7, fungus.PapychaDungeonId);
        Assert.Equal([1], fungus.Advances.SelectMany(e => e.Quests).Select(q => q.Id));
        Assert.True(fungus.Advances.Single().Started);
        Assert.Equal(QuestProfit.GainOf(facts[1], 10, 0)!.Xp, fungus.Gain.Xp);
        Assert.Equal(QuestProfit.GainOf(facts[1], 10, 0)!.Kamas, fungus.Gain.Kamas);
        Assert.Equal(2, Assert.Single(values[8].Advances).Quests.Single().Id);
    }

    [Fact]
    public void A_dungeon_is_worth_the_reward_of_the_steps_naming_it_not_the_whole_quest()
    {
        // "La voie du guerrier": seven steps, the Bworker in the sixth (index 5).
        var catalog = new[] { Quest(1) };
        var steps = Enumerable.Range(1, 7).Select(i => Step(10, i, i)).ToArray();
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new(steps, [Obj(1) with { Step = 0 }, new TouchObjective(6, [1, 1], 10, Step: 5), Said("Sortie du Donjon du Bworker", 5)]),
        };

        var values = DungeonValues(catalog, Progress([1]), facts, new Dictionary<int, string> { [70] = "Donjon du Bworker" });

        var sixth = QuestRewards.For([new TouchStep("", "", [], steps[5])], 10, 0)!;
        Assert.Equal(new QuestGain(sixth.Experience, sixth.Kamas), values[7].Gain);
        Assert.True(values[7].Gain.Xp < QuestProfit.GainOf(facts[1], 10, 0)!.Xp);
        Assert.Equal("Q1 (étape 6)", Assert.Single(values[7].Advances).Title);
    }

    [Fact]
    public void A_quest_naming_a_dungeon_in_an_unknown_step_adds_nothing()
    {
        // A cache read before steps were kept: the whole quest would be a guess.
        var catalog = new[] { Quest(1) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = new([Step(10, 1f)], [new TouchObjective(4, [1], 10, "Donjon Fungus")]) };

        Assert.Empty(DungeonValues(catalog, Progress([1]), facts, new Dictionary<int, string> { [70] = "Donjon Fungus" }));
    }

    [Fact]
    public void A_dungeon_advancing_only_a_later_quest_of_a_run_is_worth_nothing()
    {
        var catalog = new[]
        {
            Quest(1, "S", 1), Quest(2, "S", 2) with { Prerequisites = ["Q1"] }, Quest(3, "S", 3) with { Prerequisites = ["Q2"] },
        };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = Xp(1f),
            [2] = Xp(1f),
            [3] = new([Step(10, 5f)], [Said("Sortie du Donjon Fungus")]),
        };

        var values = DungeonValues(catalog, Progress([1]), facts, new Dictionary<int, string> { [70] = "Donjon Fungus" });

        Assert.Empty(values);
    }

    [Fact]
    public void A_dungeon_gets_the_tier_its_score_would_have_among_quests()
    {
        var catalog = Enumerable.Range(1, 20).Select(i => Quest(i)).ToList();
        // Five minutes everywhere (a map, 3 dialogues), as a one-room run; quest i gives i times the XP of quest 1.
        var facts = catalog.ToDictionary(q => q.Id, q => new TouchQuestFacts(
            [Step(10, q.Id)], [Said(q.Id switch { 20 => "Donjon A", 1 => "Donjon B", 18 or 19 => "Donjon C", _ => "Ailleurs" }), Obj(1), Obj(1), Obj(1)]));
        var names = new Dictionary<int, string> { [10] = "Donjon A", [20] = "Donjon B", [30] = "Donjon C" };
        var rooms = new Dictionary<int, int> { [10] = 1, [20] = 1, [30] = 3 };

        var values = DungeonValues([.. catalog], Progress(catalog.Select(q => q.Id)), facts, names, rooms);

        Assert.Equal(5, values[1].Minutes);
        Assert.Equal(1.0, values[1].Score);
        Assert.Equal(3, values[1].Tier);
        Assert.Equal(0, values[2].Tier);
        // 18 and 19 together for an 11-minute run: 37 / 11 against 20 / 5, 0.84, between quests 17 and 16 (tier 2).
        Assert.Equal(11, values[3].Minutes);
        Assert.Equal(2, values[3].Tier);
        Assert.Equal(2, values[3].Advances.Count);
    }

    private static TouchObjective Text(string text, int step = 0) => new(0, [], 10, text, Step: step);

    private static TouchObjective Boss(int step = 0, int monsters = 1) => new(6, [1, monsters], 10, Step: step);

    [Fact]
    public void A_dungeon_counts_a_quest_whose_only_work_is_the_dungeon_dialogues_and_maps_aside()
    {
        // Talk (1, 9, 2), walk to a map (4), the dungeon by name (0) and its boss in that step.
        var catalog = new[] { Quest(1), Quest(2) };
        var only = new TouchQuestFacts([Step(10, 2f)],
            [Obj(1), Obj(9), Obj(2), Said("Chantier du Barrage"), Text("Braver le Donjon Fungus"), Boss()]);
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = only, [2] = only };

        var values = DungeonValues(catalog, Progress([1, 2]), facts, new Dictionary<int, string> { [70] = "Donjon Fungus" });

        Assert.Equal([1, 2], values[7].Advances.SelectMany(a => a.Quests).Select(q => q.Id).Order());
    }

    [Fact]
    public void A_dungeon_leaves_out_a_quest_asking_items_or_other_work_besides_it()
    {
        var catalog = Enumerable.Range(1, 7).Select(i => Quest(i)).ToArray();
        TouchQuestFacts With(params TouchObjective[] more) =>
            new([Step(10, 1f), Step(10, 1f)], [Said("Sortie du Donjon Fungus"), Boss(), .. more]);
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = With(),
            [2] = With(Obj(3, [1, 2, 4])),
            // Fights outside the run: three monsters, or a lone one in a step with no dungeon.
            [3] = With(Boss(monsters: 3)),
            [4] = With(Boss(step: 1)),
            [5] = With(Obj(12, [1, 1])),
            [6] = With(Text("Trouver le marchand", 1)),
            [7] = With(Obj(20, [1, 1])),
        };

        var values = DungeonValues(catalog, Progress(catalog.Select(q => q.Id)), facts, new Dictionary<int, string> { [70] = "Donjon Fungus" });

        Assert.Equal(1, Assert.Single(values[7].Advances).Quests.Single().Id);
    }

    [Fact]
    public void A_dungeon_row_counts_only_its_dungeon_only_quests_toward_the_two_it_needs()
    {
        // The row needs two such quests: one plus one with items leaves a single advance, which the list drops.
        var catalog = new[] { Quest(1), Quest(2), Quest(3) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 2f, 1f)], [Obj(1), Said("Sortie du Donjon Fungus"), Boss()]),
            [2] = new([Step(10, 3f, 2f)], [Text("Braver le Donjon Fungus"), Boss()]),
            [3] = new([Step(10, 5f)], [Said("Sortie du Donjon Fungus"), Boss(), Obj(3, [1, 2, 4])]),
        };
        var names = new Dictionary<int, string> { [70] = "Donjon Fungus" };

        var both = DungeonValues(catalog, Progress([1, 2, 3]), facts, names)[7];
        var one = DungeonValues(catalog, Progress([1, 3]), facts, names)[7];

        Assert.Equal(2, both.Advances.Count);
        Assert.Equal(QuestProfit.GainOf(facts[1], 10, 0)!.Xp + QuestProfit.GainOf(facts[2], 10, 0)!.Xp, both.Gain.Xp);
        Assert.Equal(QuestProfit.GainOf(facts[1], 10, 0)!.Kamas + QuestProfit.GainOf(facts[2], 10, 0)!.Kamas, both.Gain.Kamas);
        Assert.Single(one.Advances);
    }

    private static IReadOnlyList<ProfitEntry> Recommendable(QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts)
    {
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        return QuestProfit.Recommendable(QuestProfit.Entries(catalog, scores, progress, facts, 10, 0), scores, facts, 10, 0);
    }

    [Fact]
    public void A_series_is_recommended_by_its_next_doable_quest_alone()
    {
        var catalog = Enumerable.Range(1, 6).Select(i => Quest(i, "S", i)).ToArray();
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        var entry = Assert.Single(Recommendable(catalog, Progress([3], completed: [1, 2]), facts));

        Assert.Equal("Q3", entry.Title);
        Assert.Equal([3], entry.Quests.Select(q => q.Id));
        Assert.Equal(XpOf(facts[3]), entry.Gain.Xp);
        Assert.Equal(4, entry.Effort.Minutes);
    }

    [Theory]
    [InlineData(0, "Vider le Trousseau de clefs")]
    [InlineData(10, "Empty the keyring")]
    [InlineData(0, "Vaciar el LLAVERO")]
    public void A_quest_emptying_the_keyring_is_never_recommended_and_ends_a_run(int type, string text)
    {
        var catalog = new[] { Quest(1), Quest(2, "S", 1), Quest(3, "S", 2), Quest(4, "S", 3) };
        var facts = catalog.ToDictionary(q => q.Id, _ => Xp(1f));
        facts[1] = facts[3] = new([Step(10, 1f)], [new TouchObjective(type, [1], 10, text)]);

        var entry = Assert.Single(Recommendable(catalog, Progress([1, 2]), facts));

        Assert.Equal("Q2", entry.Title);
        Assert.Equal([2], entry.Quests.Select(q => q.Id));
        Assert.Equal(XpOf(facts[2]), entry.Gain.Xp);
    }

    [Fact]
    public void A_quest_asking_more_than_five_souls_is_never_recommended()
    {
        var catalog = new[] { Quest(1), Quest(2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 1f)], [.. Enumerable.Repeat(Obj(12), 6)]),
            [2] = new([Step(10, 1f)], [.. Enumerable.Repeat(Obj(12), 5)]),
        };

        Assert.Equal(["Q2"], Recommendable(catalog, Progress([1, 2]), facts).Select(e => e.Title));
    }

    private static IReadOnlyList<ProfitEntry> Short(IEnumerable<ProfitEntry> rows, IReadOnlyList<QuestSummary> catalog, bool kamas) =>
        QuestProfit.Shortlist(rows, e => e.Gain, e => e.Effort.Minutes, e => e.Quests[0], catalog, kamas);

    private static ProfitEntry Row(QuestSummary quest, long xp, long kamas, int minutes) =>
        new(quest.Title, [quest], new QuestGain(xp, kamas), new QuestEffort(minutes, []), 0, false, false, 0);

    [Fact]
    public void Each_short_list_ranks_on_its_own_side_for_the_effort_and_keeps_twenty()
    {
        var catalog = Enumerable.Range(1, 26).Select(i => Quest(i)).ToArray();
        // XP grows with the id, kamas shrink with it, and quest 26 gives none.
        var rows = catalog.Select(q => Row(q, q.Id * 10, q.Id == 26 ? 0 : (27 - q.Id) * 10, 2)).ToList();

        Assert.Equal(Enumerable.Range(7, 20).Reverse(), Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Equal(Enumerable.Range(1, 20), Short(rows, catalog, kamas: true).Select(e => e.Quests[0].Id));
        Assert.Equal(Enumerable.Range(10, 16), Short(rows.Skip(9), catalog, kamas: true).Select(e => e.Quests[0].Id));
    }

    [Fact]
    public void A_row_above_120_minutes_stays_out_of_a_list_of_ten_and_one_with_nothing_on_its_side_always()
    {
        var catalog = Enumerable.Range(1, 12).Select(i => Quest(i)).ToArray();
        // Ten rows within the limit, one far richer above it, one with no kamas.
        List<ProfitEntry> rows = [.. catalog.Take(10).Select(q => Row(q, 10, 10, 120)), Row(catalog[10], 1000, 1000, 121), Row(catalog[11], 10, 0, 1)];

        Assert.Equal([12, .. Enumerable.Range(1, 10)], Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Equal(Enumerable.Range(1, 10), Short(rows, catalog, kamas: true).Select(e => e.Quests[0].Id));
    }

    [Fact]
    public void A_list_under_ten_is_topped_up_with_the_best_rows_above_120_minutes_after_its_own()
    {
        var catalog = Enumerable.Range(1, 14).Select(i => Quest(i)).ToArray();
        // Three rows within the limit, eleven above it, better per minute as the id grows.
        List<ProfitEntry> rows = [.. catalog.Take(3).Select(q => Row(q, 10, 0, 100)), .. catalog.Skip(3).Select(q => Row(q, q.Id * 100, 0, 200))];

        Assert.Equal([1, 2, 3, 14, 13, 12, 11, 10, 9, 8], Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Empty(Short(rows, catalog, kamas: true));
    }

    [Fact]
    public void Short_list_ties_follow_the_catalog_then_the_quest_id_whatever_the_input_order()
    {
        var catalog = new[] { Quest(5), Quest(3), Quest(9) };
        // 2 and 1 are not in the catalog: after it, by id.
        var rows = new[] { Quest(2), Quest(9), Quest(1), Quest(3), Quest(5) }.Select(q => Row(q, 10, 10, 2)).ToList();

        Assert.Equal([5, 3, 9, 1, 2], Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Equal(Short(rows, catalog, kamas: true), Short(rows.AsEnumerable().Reverse(), catalog, kamas: true));
    }

    [Fact]
    public void Gains_are_per_minute_so_a_quick_quest_beats_a_long_richer_one()
    {
        // Q1: three times Q2's XP, but ten monsters (180 + 600 s, 13 min) against one dialogue (4 min).
        var catalog = new[] { Quest(1), Quest(2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 3f)], [Obj(14, [1, 10])]),
            [2] = new([Step(10, 1f)], [Obj(1)]),
        };
        var progress = Progress([1, 2]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);

        Assert.Equal(13, scores[1].Effort.Minutes);
        Assert.Equal(1.0, scores[2].Score);
        Assert.Equal([2, 1], Short(Recommendable(catalog, progress, facts), catalog, kamas: false).Select(e => e.Quests[0].Id));
    }

    [Fact]
    public void Doing_the_best_kamas_quest_does_not_reorder_the_others()
    {
        // The combined score swapped Q2 and Q3 here: Q3's kamas were measured against Q4's, then against its own.
        var catalog = new[] { Quest(1), Quest(2), Quest(3), Quest(4) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 4f)], [Obj(1)]),
            [2] = new([Step(10, 3f)], [Obj(1)]),
            [3] = new([Step(10, 1f, 3f)], [Obj(1)]),
            [4] = new([Step(10, 0.5f, 6f)], [Obj(1)]),
        };

        IEnumerable<int> Lists(TrackerProgress progress)
        {
            var entries = Recommendable(catalog, progress, facts);
            return Short(entries, catalog, kamas: false).Concat(Short(entries, catalog, kamas: true)).Select(e => e.Quests[0].Id);
        }

        Assert.Equal([1, 2, 3, 4, 4, 3], Lists(Progress([1, 2, 3, 4])));
        Assert.Equal([1, 2, 3, 3], Lists(Progress([1, 2, 3], completed: [4])));
        Assert.Equal(Lists(Progress([1, 2, 3, 4])), Lists(Progress([1, 2, 3, 4])));
    }
    [Fact]
    public void A_recommended_quest_never_shows_an_unmet_lock()
    {
        // 2 follows done 1; 3 and 6 follow undone 4, 6 with free text too; 5 follows 4 but is in progress; 7 needs the
        // achievement S, closed by the undone 9; 10 follows 1 or 4 ("La fatalité": one alignment or the other).
        QuestSummary Q(int id, string success = "", int order = 0, params string[] needs) =>
            Quest(id, success, order) with { Prerequisites = needs };
        var catalog = new[]
        {
            Q(1), Q(2, needs: "Q1"), Q(3, needs: "Q4"), Q(4), Q(5, needs: "Q4"), Q(6, needs: ["Q4", "Un objet"]),
            Q(7, needs: "Succès S réalisé"), Q(8, "S", 1), Q(9, "S", 2), Q(10, needs: ["Q1", "Q4"]),
        };
        var facts = catalog.ToDictionary(q => q.Id, _ => Xp(1f));
        var chain = new QuestChainIndex(catalog);
        var inProgress = new Dictionary<int, QuestStatus> { [5] = QuestStatus.InProgress };
        bool? Lock(QuestSummary q, TrackerProgress progress) => QuestTracking.Locked(
            new QuestNode(QuestNodeKind.Quest, q.Title, Quest: q, Needs: [.. q.Prerequisites.Select(n => new QuestNeed(n, chain.Find(n)))]),
            progress, chain).NeedsMet;

        // The active character, whose list the site fills (here wrongly listing 3, 6 and 7), then another one.
        var active = Progress([2, 3, 4, 6, 7, 8, 10], inProgress, [1]);
        var other = Progress([], inProgress, [1]);
        foreach (var (progress, expected) in new[] { (active, new[] { 2, 4, 5, 8, 10 }), (other, [2, 4, 5, 8]) })
        {
            var shown = Recommendable(catalog, progress, facts).Select(e => e.Quests[0]).ToList();

            Assert.Equal(expected, shown.Select(q => q.Id).Order());
            Assert.All(shown, q => Assert.NotEqual(false, Lock(q, progress)));
        }

        // The team list: each entry's lock is met for every character it names.
        var characters = new[] { (new TrackerCharacter("a", "A", 10), active), (new TrackerCharacter("b", "B", 10), other) };
        var team = QuestPlanning.Team(catalog, facts, characters, 0);
        Assert.NotEmpty(team);
        Assert.All(team, t => Assert.All(t.Characters, c => Assert.NotEqual(false, Lock(t.Entry.Quests[0], characters.Single(x => x.Item1 == c).Item2))));
    }

    private static QuestSummary[] Series(int n, string name = "S") => [.. Enumerable.Range(1, n).Select(i => Quest(i, name, i))];

    private static IReadOnlyList<ProfitEntry> Runs(
        QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts,
        bool dungeons = true, Dictionary<int, string>? dungeonNames = null) =>
        QuestProfit.Runs(catalog, facts, progress, 10, 0, dungeons, dungeonNames ?? []);

    private static int[] RunIds(QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts,
        bool dungeons = true, Dictionary<int, string>? dungeonNames = null) =>
        [.. Runs(catalog, progress, facts, dungeons, dungeonNames).SelectMany(r => r.Quests).Select(q => q.Id)];

    [Fact]
    public void A_run_is_the_series_from_its_next_doable_quest_past_done_and_ignored_ones_gains_and_minutes_summed()
    {
        var catalog = Series(5);
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
        var progress = Progress([2], new() { [1] = QuestStatus.Completed, [3] = QuestStatus.Completed, [4] = QuestStatus.Ignored });

        var run = Assert.Single(Runs(catalog, progress, facts));

        Assert.Equal("S", run.Title);
        Assert.Equal([2, 5], run.Quests.Select(q => q.Id));
        Assert.Equal(XpOf(facts[2]) + XpOf(facts[5]), run.Gain.Xp);
        // Two quests of 180 + 30 s, 4 minutes each.
        Assert.Equal(8, run.Effort.Minutes);
        Assert.True(run.Started);
    }

    [Fact]
    public void A_run_starts_at_the_quest_in_progress()
    {
        var catalog = Series(4);
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        // The site lists another quest: 1 cannot start, 2 is under way.
        Assert.Equal([2, 3, 4], RunIds(catalog, Progress([9], new() { [2] = QuestStatus.InProgress }), facts));
    }

    [Fact]
    public void A_run_stops_before_a_quest_it_cannot_count_on()
    {
        int[] With(Action<QuestSummary[], Dictionary<int, TouchQuestFacts>> change, Dictionary<int, QuestStatus>? statuses = null)
        {
            var catalog = Series(4);
            var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
            change(catalog, facts);
            return RunIds(catalog, Progress([1], statuses), facts);
        }

        Assert.Equal([1, 2, 3, 4], With((_, _) => { }));
        // Repeatable.
        Assert.Equal([1, 2], With((c, _) => c[2] = c[2] with { Recurrence = QuestRecurrence.Daily }));
        // No facts, or a step without its formula: the gain is unknown.
        Assert.Equal([1, 2], With((_, f) => f.Remove(3)));
        Assert.Equal([1, 2], With((_, f) => f[3] = new([null], [Obj(1)])));
        // Above the character's level.
        Assert.Equal([1, 2], With((_, f) => f[3] = new([Step(20, 1f)], [Obj(1)])));
        // The short lists' exclusions: the keyring, more than five souls.
        Assert.Equal([1, 2], With((_, f) => f[3] = new([Step(10, 1f)], [new TouchObjective(0, [1], 10, "Vider le trousseau")])));
        Assert.Equal([1, 2], With((_, f) => f[3] = new([Step(10, 1f)], [.. Enumerable.Repeat(Obj(12), 6)])));
        // Blocked.
        Assert.Equal([1, 2], With((_, _) => { }, new() { [3] = QuestStatus.Blocked }));
    }

    [Fact]
    public void A_run_of_one_quest_is_left_to_the_quests_mode()
    {
        var catalog = Series(3);
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
        facts[2] = new([Step(20, 1f)], [Obj(1)]);

        Assert.Empty(Runs(catalog, Progress([1]), facts));
    }

    [Fact]
    public void A_prerequisite_cuts_a_run_unless_the_need_is_a_quest_of_the_run()
    {
        var catalog = Series(4);
        // 3 follows 2, inside the run; 4 asks a time slot.
        catalog[2] = catalog[2] with { Prerequisites = ["Q2"] };
        catalog[3] = catalog[3] with { Prerequisites = ["Être dans les créneaux horaires"] };
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        Assert.Equal([1, 2, 3], RunIds(catalog, Progress([1]), facts));

        // A quest outside the run, not done, is a prerequisite.
        QuestSummary[] outside = [.. Series(3), Quest(9)];
        outside[2] = outside[2] with { Prerequisites = ["Q9"] };
        Assert.Equal([1, 2], RunIds(outside, Progress([1]), facts));

        // The first quest's own need hides the run, as it hides its row in the quests mode.
        var first = Series(3);
        first[0] = first[0] with { Prerequisites = ["Être dans les créneaux horaires"] };
        Assert.Empty(RunIds(first, Progress([1]), facts));
    }

    [Fact]
    public void Off_dungeons_cut_a_run_before_a_quest_with_a_dungeon()
    {
        var catalog = Series(4);
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
        // 4 runs a dungeon (a lone fight beside its exit), 3 names one.
        facts[4] = new([Step(10, 1f)], [new TouchObjective(6, [1, 1], 10, Step: 0), new TouchObjective(4, [], 10, "Sortie du donjon", Step: 0)]);
        Dictionary<int, string> names = new() { [7] = "Donjon des Bouftous" };

        Assert.Equal([1, 2, 3, 4], RunIds(catalog, Progress([1]), facts, dungeonNames: names));
        Assert.Equal([1, 2, 3], RunIds(catalog, Progress([1]), facts, dungeons: false));
        facts[3] = new([Step(10, 1f)], [new TouchObjective(0, [1], 10, "Braver le Donjon des Bouftous")]);
        Assert.Equal([1, 2], RunIds(catalog, Progress([1]), facts, dungeons: false, dungeonNames: names));
    }

    [Fact]
    public void Runs_rank_on_the_tab_s_side_per_minute_with_no_two_hour_limit()
    {
        var catalog = Enumerable.Range(1, 12).Select(i => Quest(i)).ToArray();
        // Ten rows within two hours, two far longer ones better per minute in XP only.
        List<ProfitEntry> rows = [.. catalog.Take(10).Select(q => Row(q, 10, 20, 100)), Row(catalog[10], 1000, 0, 300), Row(catalog[11], 2000, 0, 300)];

        IEnumerable<int> Ranked(bool kamas) =>
            QuestProfit.Shortlist(rows, e => e.Gain, e => e.Effort.Minutes, e => e.Quests[0], catalog, kamas, maxMinutes: int.MaxValue)
                .Select(e => e.Quests[0].Id);

        Assert.Equal([12, 11, .. Enumerable.Range(1, 10)], Ranked(kamas: false));
        Assert.Equal(Enumerable.Range(1, 10), Ranked(kamas: true));
    }

    [Fact]
    public void Equal_runs_follow_the_catalog_whatever_the_order_they_come_in()
    {
        QuestSummary[] catalog = [Quest(3, "B", 1), Quest(4, "B", 2), Quest(1, "A", 1), Quest(2, "A", 2)];
        var facts = catalog.ToDictionary(q => q.Id, _ => Xp(1f));
        var runs = Runs(catalog, Progress([1, 3]), facts);

        IEnumerable<string> Ranked(IEnumerable<ProfitEntry> rows) =>
            QuestProfit.Shortlist(rows, e => e.Gain, e => e.Effort.Minutes, e => e.Quests[0], catalog, false, maxMinutes: int.MaxValue)
                .Select(e => e.Title);

        Assert.Equal(["B", "A"], Ranked(runs));
        Assert.Equal(["B", "A"], Ranked(runs.Reverse()));
    }
}
