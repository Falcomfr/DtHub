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
        var scores = QuestProfit.Score(catalog, facts, Progress(catalog.Select(q => q.Id)), 10, 0, RewardRanking.Xp);
        Assert.Equal(20, scores.Count);
        Assert.Equal(2, scores.Values.Count(s => s.Tier == 3));
        Assert.Equal(3, scores.Values.Count(s => s.Tier == 2));
        Assert.Equal(5, scores.Values.Count(s => s.Tier == 1));
        Assert.All([20, 19], id => Assert.Equal(3, scores[id].Tier));
        Assert.Equal(2, scores[17].Tier);
        Assert.Equal(1, scores[14].Tier);
        Assert.Equal(1, scores[11].Tier);
        Assert.Equal(0, scores[10].Tier);
        Assert.Equal(scores[20].Gain.Xp / 1.0, scores[20].Score);
    }

    [Fact]
    public void Score_ties_keep_catalog_order_and_a_lone_top_quest_is_tier_three()
    {
        var catalog = new List<QuestSummary> { Quest(1), Quest(2), Quest(3) };
        var facts = catalog.ToDictionary(q => q.Id, _ => new TouchQuestFacts([Step(10, 1f)], []));
        var scores = QuestProfit.Score(catalog, facts, Progress([1, 2, 3]), 10, 0, RewardRanking.Xp);
        Assert.Equal(3, scores[1].Tier);
        Assert.Equal(1, scores[2].Tier);
        Assert.Equal(0, scores[3].Tier);
    }

    [Fact]
    public void Kamas_ranking_skips_quests_without_kamas()
    {
        var catalog = new List<QuestSummary> { Quest(1), Quest(2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 1f, 0f)], []),
            [2] = new([Step(10, 1f, 1f)], []),
        };
        var scores = QuestProfit.Score(catalog, facts, Progress([1, 2]), 10, 0, RewardRanking.Kamas);
        Assert.Equal([2], scores.Keys);
        Assert.Equal(2, QuestProfit.Score(catalog, facts, Progress([1, 2]), 10, 0, RewardRanking.Xp).Count);
    }

    private static IReadOnlyDictionary<int, QuestScore> Scores(params (int Id, double Score)[] items) =>
        items.ToDictionary(i => i.Id, i => new QuestScore(
            i.Id, new QuestGain(100, 10), new QuestEffort(2, ["dialogues"]), i.Score, 1));

    private static Dictionary<int, TouchQuestFacts> AllFacts(IEnumerable<QuestSummary> catalog) =>
        catalog.ToDictionary(q => q.Id, _ => Facts(Obj(1)));

    private static IReadOnlyList<ProfitEntry> Entries(
        QuestSummary[] catalog, IReadOnlyDictionary<int, QuestScore> scores, TrackerProgress progress,
        Dictionary<int, TouchQuestFacts>? facts = null) =>
        QuestProfit.Entries(catalog, scores, progress, facts ?? AllFacts(catalog), 10);

    [Fact]
    public void Series_starts_at_the_first_doable_quest_and_skips_leading_done_ones()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var entry = Assert.Single(Entries(catalog, Scores((2, 5), (3, 4)), Progress([2], completed: [1])));
        Assert.Equal("S", entry.Title);
        Assert.Equal([2, 3], entry.Quests.Select(q => q.Id));
        Assert.True(entry.Started);
        Assert.Equal(200, entry.Gain.Xp);
    }

    [Fact]
    public void Series_lists_later_quests_even_when_only_the_first_is_available()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var entry = Assert.Single(Entries(catalog, Scores((1, 5), (2, 4), (3, 3)), Progress([1])));
        Assert.Equal([1, 2, 3], entry.Quests.Select(q => q.Id));
        Assert.Equal(6, entry.Effort.Value);
    }

    [Fact]
    public void Series_stops_at_a_blocked_or_over_level_quest()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var blocked = Progress([1], new() { [2] = QuestStatus.Blocked });
        Assert.Equal([1], Assert.Single(Entries(catalog, Scores((1, 5), (3, 5)), blocked)).Quests.Select(q => q.Id));

        var facts = AllFacts(catalog);
        facts[2] = new([Step(20, 1f)], []);
        Assert.Equal([1], Assert.Single(Entries(catalog, Scores((1, 5), (3, 5)), Progress([1]), facts)).Quests.Select(q => q.Id));
    }

    [Fact]
    public void Unscored_members_stay_in_the_series_and_add_no_gain()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 1f, 1f)], []),
            [2] = new([Step(10, 1f, 0f)], []),
            [3] = new([Step(10, 1f, 1f)], []),
        };
        var progress = Progress([1, 2, 3]);
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0, RewardRanking.Kamas);
        Assert.Equal(2, scores.Count);
        var entry = Assert.Single(QuestProfit.Entries(catalog, scores, progress, facts, 10));
        Assert.Equal([1, 2, 3], entry.Quests.Select(q => q.Id));
        Assert.Equal(scores[1].Gain.Kamas + scores[3].Gain.Kamas, entry.Gain.Kamas);
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
        var scores = QuestProfit.Score(catalog, facts, progress, 10, 0, RewardRanking.Xp);
        var entry = Assert.Single(QuestProfit.Entries(catalog, scores, progress, facts, 10));
        Assert.Equal(["2 combats", "dialogues"], entry.Effort.Reasons);
    }

    [Fact]
    public void Crafting_and_travel_have_their_own_reasons()
    {
        Assert.Equal(["fabrication"], QuestProfit.EffortOf(Facts(Obj(20)), 50).Reasons);
        Assert.Equal(["déplacements"], QuestProfit.EffortOf(Facts(Obj(4)), 50).Reasons);
    }

    [Fact]
    public void Started_series_come_first_then_by_summed_score()
    {
        var catalog = new[]
        {
            Quest(1, "A", 1), Quest(2, "A", 2), Quest(3), Quest(4, "B", 1), Quest(5, "B", 2),
        };
        var progress = Progress([1, 2, 3, 4, 5], new() { [4] = QuestStatus.InProgress });
        var entries = Entries(catalog, Scores((1, 3), (2, 3), (3, 100), (4, 1), (5, 1)), progress);
        Assert.Equal(["B", "Q3", "A"], entries.Select(e => e.Title));
        Assert.Equal([true, false, false], entries.Select(e => e.Started));
    }
}
