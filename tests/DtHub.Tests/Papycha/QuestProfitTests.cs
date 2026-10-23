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

    [Theory]
    [InlineData(1, 2.0)]
    [InlineData(9, 2.0)]
    [InlineData(2, 2.0)]
    [InlineData(4, 2.5)]
    [InlineData(20, 4.0)]
    [InlineData(12, 6.0)]
    [InlineData(0, 7.0)]
    [InlineData(10, 7.0)]
    public void Effort_of_simple_objective_types(int type, double expected) =>
        Assert.Equal(expected, QuestProfit.EffortOf(Facts(Obj(type)), 50).Value);

    [Theory]
    [InlineData(5, 3.5)]
    [InlineData(100, 8.0)]
    public void Effort_of_bringing_items_grows_with_quantity_up_to_seven(int quantity, double expected) =>
        Assert.Equal(expected, QuestProfit.EffortOf(Facts(Obj(3, [1, 2, quantity])), 50).Value);

    [Fact]
    public void Effort_of_fights_boss_and_over_level()
    {
        Assert.Equal(4.0, QuestProfit.EffortOf(Facts(Obj(14, [1, 5])), 50).Value);
        Assert.Equal(4.0, QuestProfit.EffortOf(Facts(Obj(6, [1, 5])), 50).Value);
        Assert.Equal(9.0, QuestProfit.EffortOf(Facts(Obj(6, [1, 1])), 50).Value);
        Assert.Equal(4.0, QuestProfit.EffortOf(Facts(Obj(14, [1, 1])), 50).Value);
        Assert.Equal(8.0, QuestProfit.EffortOf(Facts(Obj(14, [1, 5], 60)), 50).Value);
        Assert.Equal(13.0, QuestProfit.EffortOf(Facts(Obj(6, [1, 1], 60)), 50).Value);
    }

    [Fact]
    public void Effort_reasons_are_aggregated_and_costliest_first_at_most_three()
    {
        var effort = QuestProfit.EffortOf(
            Facts(Obj(1), Obj(1), Obj(3, [1, 2, 5]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 5]), Obj(6, [1, 1]), Obj(12)), 50);
        Assert.Equal(3, effort.Reasons.Count);
        Assert.Equal(["3 combats", "1 boss", "âmes"], effort.Reasons);
    }

    [Fact]
    public void Effort_reasons_name_dialogues_and_vague()
    {
        Assert.Equal(["objectif vague", "dialogues"], QuestProfit.EffortOf(Facts(Obj(1), Obj(0)), 50).Reasons);
        Assert.Equal(["1 objet"], QuestProfit.EffortOf(Facts(Obj(3, [1, 2, 1])), 50).Reasons);
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
        var progress = Progress([1, 2, 3, 4, 5, 6], new()
        {
            [2] = QuestStatus.Blocked,
            [3] = QuestStatus.Ignored,
            [4] = QuestStatus.InProgress,
            [7] = QuestStatus.InProgress,
        }, [5]);
        Assert.True(QuestProfit.IsDoable(Quest(1), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(2), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(3), progress, facts, 10));
        Assert.True(QuestProfit.IsDoable(Quest(4), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(5), progress, facts, 10));
        Assert.True(QuestProfit.IsDoable(Quest(7), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(8), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(1), progress, facts, 9));
        Assert.False(QuestProfit.IsDoable(Quest(1, rec: QuestRecurrence.Daily), progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(1) with { SectionIds = [QuestRecurrenceLabel.RepeatableSection] }, progress, facts, 10));
        Assert.False(QuestProfit.IsDoable(Quest(1), null, facts, 10));
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
        Assert.Equal(6, entry.Effort.Value);
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
        Assert.Equal(["fabrication"], QuestProfit.EffortOf(Facts(Obj(20)), 50).Reasons);
        Assert.Equal(["déplacements"], QuestProfit.EffortOf(Facts(Obj(4)), 50).Reasons);
    }

    [Fact]
    public void Started_series_come_first_then_by_summed_gain_for_summed_effort()
    {
        var catalog = new[]
        {
            Quest(1, "A", 1), Quest(2, "A", 2), Quest(3), Quest(4, "B", 1), Quest(5, "B", 2),
        };
        // A gains 6 for 4 of effort, below Q3's 4 for 2, though its two scores add up above Q3's.
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(3f), [2] = Xp(3f), [3] = Xp(4f), [4] = Xp(1f), [5] = Xp(1f) };
        var entries = Entries(catalog, Progress([1, 2, 3, 4, 5], new() { [4] = QuestStatus.InProgress }), facts);
        Assert.Equal(["B", "Q3", "A"], entries.Select(e => e.Title));
        Assert.Equal([true, false, false], entries.Select(e => e.Started));
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
}
