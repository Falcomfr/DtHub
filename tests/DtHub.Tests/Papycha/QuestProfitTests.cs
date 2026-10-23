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
        Assert.Equal(10, QuestProfit.EffortOf(facts, quest).Minutes);
    }

    [Fact]
    public void Effort_reasons_are_aggregated_and_costliest_first_at_most_three()
    {
        var effort = QuestProfit.EffortOf(
            Facts(Obj(1), Obj(1), Obj(3, [1, 2, 5]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 1]), Obj(12)), Quest(1));
        Assert.Equal(3, effort.Reasons.Count);
        // Ranked on their time: 3 x 450 s, 300 s, then the lone fight's 90 s.
        Assert.Equal(["3 combats", "âmes", "1 boss"], effort.Reasons);
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
        Assert.True(QuestProfit.IsDoable(Quest(8), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1), progress, facts, 9, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1, rec: QuestRecurrence.Daily), progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1) with { SectionIds = [QuestRecurrenceLabel.RepeatableSection] }, progress, facts, 10, chain));
        Assert.False(QuestProfit.IsDoable(Quest(1), null, facts, 10, chain));
    }

    [Fact]
    public void Out_of_available_a_quest_is_doable_once_its_previous_quests_are_completed()
    {
        // The site lists available quests for its active character only: here, quest 5 alone.
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
        var scores = QuestProfit.Score(catalog, facts, Progress([5], completed: [1]), 10, 0);
        Assert.Equal([2, 4, 5, 6], scores.Keys.Order());
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
        // Three quests of 90 + 15 s, 2 minutes each.
        Assert.Equal(6, entry.Effort.Minutes);
        Assert.Equal(XpOf(facts[1]) + XpOf(facts[2]) + XpOf(facts[3]), entry.Gain.Xp);
    }

    [Fact]
    public void A_blocked_or_over_level_quest_ends_a_run_and_the_next_doable_one_starts_another()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4, "S", 4) };
        var blocked = Progress([1, 3], new() { [2] = QuestStatus.Blocked });
        Assert.Equal(["Q1", "S"], Entries(catalog, blocked).Select(e => e.Title));
        Assert.Equal([3, 4], Entries(catalog, blocked)[1].Quests.Select(q => q.Id));

        var facts = catalog.ToDictionary(q => q.Id, _ => Facts(Obj(1)));
        facts[2] = new([Step(20, 1f)], []);
        Assert.Equal(["Q1", "S"], Entries(catalog, Progress([1, 3]), facts).Select(e => e.Title));
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

    private static TouchObjective Said(string text) => new(4, [1], 10, text);

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
    public void Entries_carry_their_score_in_order_and_a_single_quest_scores_as_its_dungeon()
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
            entries, scores, progress, facts, 10, 0, new Dictionary<int, TouchDungeonIds> { [7] = new([70]) }, new Dictionary<int, string> { [70] = "Donjon Fungus" });

        Assert.Equal(entries.Select(e => e.Score).OrderByDescending(x => x), entries.Select(e => e.Score));
        Assert.True(entries[0].Score > 0);
        Assert.Equal(values[7].Score, entries.Single(e => e.Title == "Q3").Score);
    }

    // Papycha dungeon n pairs with game dungeon 10n.
    private static IReadOnlyDictionary<int, DungeonValue> DungeonValues(
        QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts, Dictionary<int, string> names)
    {
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        var entries = QuestProfit.Entries(catalog, scores, progress, facts, 10, 0);
        var table = names.Keys.ToDictionary(id => id / 10, id => new TouchDungeonIds([id]));
        return QuestProfit.DungeonValues(entries, scores, progress, facts, 10, 0, table, names);
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
        // Same effort everywhere; quest i gives i times the XP of quest 1.
        var facts = catalog.ToDictionary(q => q.Id, q => new TouchQuestFacts(
            [Step(10, q.Id)], [Said(q.Id switch { 20 => "Donjon A", 1 => "Donjon B", 18 or 19 => "Donjon C", _ => "Ailleurs" })]));
        var names = new Dictionary<int, string> { [10] = "Donjon A", [20] = "Donjon B", [30] = "Donjon C" };

        var values = DungeonValues([.. catalog], Progress(catalog.Select(q => q.Id)), facts, names);

        Assert.Equal(1.0, values[1].Score);
        Assert.Equal(3, values[1].Tier);
        Assert.Equal(0, values[2].Tier);
        // 18 and 19 together score between quest 19 (tier 3) and quest 18 (tier 2).
        Assert.Equal(2, values[3].Tier);
        Assert.Equal(2, values[3].Advances.Count);
    }

    private static IReadOnlyList<ProfitEntry> Recommendable(QuestSummary[] catalog, TrackerProgress progress, Dictionary<int, TouchQuestFacts> facts)
    {
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);
        return QuestProfit.Recommendable(QuestProfit.Entries(catalog, scores, progress, facts, 10, 0), scores, facts, 10, 0);
    }

    [Fact]
    public void A_recommended_series_run_keeps_its_first_four_quests()
    {
        var catalog = Enumerable.Range(1, 6).Select(i => Quest(i, "S", i)).ToArray();
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        var entry = Assert.Single(Recommendable(catalog, Progress([1]), facts));

        Assert.Equal("S", entry.Title);
        Assert.Equal([1, 2, 3, 4], entry.Quests.Select(q => q.Id));
        Assert.Equal(Enumerable.Range(1, 4).Sum(i => XpOf(facts[i])), entry.Gain.Xp);
        Assert.Equal(8, entry.Effort.Minutes);
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
    public void Each_short_list_ranks_on_its_own_side_for_the_effort_and_keeps_eight()
    {
        var catalog = Enumerable.Range(1, 12).Select(i => Quest(i)).ToArray();
        // XP grows with the id, kamas shrink with it, and quest 12 gives none.
        var rows = catalog.Select(q => Row(q, q.Id * 10, q.Id == 12 ? 0 : (13 - q.Id) * 10, 2)).ToList();

        Assert.Equal([12, 11, 10, 9, 8, 7, 6, 5], Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], Short(rows, catalog, kamas: true).Select(e => e.Quests[0].Id));
        Assert.Equal([5, 6, 7, 8, 9, 10, 11], Short(rows.Skip(4), catalog, kamas: true).Select(e => e.Quests[0].Id));
    }

    [Fact]
    public void A_row_above_60_minutes_or_with_nothing_on_its_side_stays_out()
    {
        var catalog = new[] { Quest(1), Quest(2), Quest(3) };
        List<ProfitEntry> rows = [Row(catalog[0], 1000, 1000, 61), Row(catalog[1], 10, 0, 60), Row(catalog[2], 0, 10, 1)];

        Assert.Equal([2], Short(rows, catalog, kamas: false).Select(e => e.Quests[0].Id));
        Assert.Equal([3], Short(rows, catalog, kamas: true).Select(e => e.Quests[0].Id));
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
        // Q1: three times Q2's XP, but ten monsters (90 + 900 s, 17 min) against one dialogue (2 min).
        var catalog = new[] { Quest(1), Quest(2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 3f)], [Obj(14, [1, 10])]),
            [2] = new([Step(10, 1f)], [Obj(1)]),
        };
        var progress = Progress([1, 2]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0);

        Assert.Equal(17, scores[1].Effort.Minutes);
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
}
