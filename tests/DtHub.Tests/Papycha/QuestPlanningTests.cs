using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestPlanningTests
{
    private static TouchStepReward Step(int level, float xp, float kamas = 0f) => new(level, 1f, xp, kamas, false, []);

    private static TouchQuestFacts Xp(float xp) => new([Step(10, xp)], []);

    private static QuestSummary Quest(int id, string success = "", int order = 0) =>
        new() { Id = id, Title = "Q" + id, SuccessName = success, PlayOrder = order };

    private static TrackerProgress Progress(IEnumerable<int> available, IEnumerable<int>? completed = null) =>
        new((completed ?? []).ToDictionary(id => id, _ => QuestStatus.Completed), (completed ?? []).ToHashSet(), available.ToHashSet(), []);

    private static (TrackerCharacter, TrackerProgress) Character(string name, int level, TrackerProgress progress) =>
        (new TrackerCharacter(name, name, level), progress);

    private static ProfitEntry Entry(int id, long xp = 0, long kamas = 0, int effort = 1) =>
        new("Q" + id, [Quest(id)], new QuestGain(xp, kamas), new QuestEffort(effort, []), 0, false, false, 0);

    [Fact]
    public void Team_with_one_character_is_empty()
    {
        var catalog = new[] { Quest(1) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f) };
        Assert.Empty(QuestPlanning.Team(catalog, facts, [Character("A", 10, Progress([1]))], 0));
        Assert.Empty(QuestPlanning.Team(catalog, facts, [], 0));
    }

    [Fact]
    public void Team_keeps_entries_doable_by_two_characters_and_sums_their_gains_at_each_level()
    {
        var catalog = new[] { Quest(1), Quest(2), Quest(3) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f), [2] = Xp(5f), [3] = Xp(3f) };
        var team = QuestPlanning.Team(
            catalog, facts,
            [Character("A", 10, Progress([1, 2, 3])), Character("B", 30, Progress([1, 3], completed: [2])), Character("C", 30, Progress([], completed: [1, 2, 3]))],
            20);

        Assert.Equal([3, 1], team.Select(t => t.Entry.Quests.Single().Id));
        Assert.All(team, t => Assert.Equal(["A", "B"], t.Characters.Select(c => c.Name)));
        var gain = QuestProfit.GainOf(facts[3], 10, 20)!.Xp + QuestProfit.GainOf(facts[3], 30, 20)!.Xp;
        Assert.Equal(gain, team[0].Gain.Xp);
        // A scores 3 against its best quest 2 (5), B scores 1 on its own best.
        Assert.Equal(3.0 / 5 + 1, team[0].Score, 3);
        Assert.Equal(3, team[0].Tier);
    }

    [Fact]
    public void Team_matches_a_series_by_its_name_whatever_the_run_and_sums_each_run()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4) };
        var facts = catalog.ToDictionary(q => q.Id, _ => Xp(1f));
        var team = QuestPlanning.Team(
            catalog, facts, [Character("A", 10, Progress([1, 4])), Character("B", 10, Progress([2, 4], completed: [1]))], 0);

        var series = Assert.Single(team, t => t.Entry.Title == "S");
        Assert.Equal([1, 2, 3], series.Entry.Quests.Select(q => q.Id));
        Assert.Equal(["A", "B"], series.Characters.Select(c => c.Name));
        Assert.Equal(5 * QuestProfit.GainOf(facts[1], 10, 0)!.Xp, series.Gain.Xp);
        Assert.Single(team, t => t.Entry.Title == "Q4");
    }

    [Fact]
    public void Team_counts_only_the_best_run_of_a_series_for_each_character()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4, "S", 4) };
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f), [2] = Xp(1f), [3] = Xp(5f), [4] = Xp(5f) };
        // A's quest 2 is blocked: runs [1] and [3, 4], the second ranked first.
        var blocked = new TrackerProgress(
            new Dictionary<int, QuestStatus> { [2] = QuestStatus.Blocked }, new HashSet<int>(), new HashSet<int> { 1, 3 }, []);
        var team = QuestPlanning.Team(catalog, facts, [Character("A", 10, blocked), Character("B", 10, Progress([1]))], 0);

        var series = Assert.Single(team);
        Assert.Equal(["A", "B"], series.Characters.Select(c => c.Name));
        Assert.Equal([3, 4], series.Entry.Quests.Select(q => q.Id));
        long Gain(int id) => QuestProfit.GainOf(facts[id], 10, 0)!.Xp;
        Assert.Equal(Gain(3) + Gain(4) + Gain(1) + Gain(2) + Gain(3) + Gain(4), series.Gain.Xp);
    }

    [Fact]
    public void Team_tiers_follow_the_quest_shares()
    {
        var catalog = Enumerable.Range(1, 20).Select(i => Quest(i)).ToList();
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
        var all = Progress(catalog.Select(q => q.Id));
        var team = QuestPlanning.Team(catalog, facts, [Character("A", 10, all), Character("B", 10, all)], 0);
        Assert.Equal(20, team.Count);
        Assert.Equal(20, team[0].Entry.Quests.Single().Id);
        Assert.Equal([2, 3, 5, 10], [team.Count(t => t.Tier == 3), team.Count(t => t.Tier == 2), team.Count(t => t.Tier == 1), team.Count(t => t.Tier == 0)]);
    }

    private static readonly Dictionary<int, long> Table = new() { [10] = 1000, [11] = 1150, [199] = 5000, [200] = 9000 };

    [Fact]
    public void Whole_level_is_the_smallest_count_whose_xp_covers_the_level()
    {
        var line = QuestPlanning.WholeLevel([Entry(1, 100), Entry(2, 30), Entry(3, 40), Entry(4, 500)], 10, Table);
        Assert.Equal(new LevelLine(3, 10, 11), line);
        Assert.Equal(new LevelLine(1, 10, 11), QuestPlanning.WholeLevel([Entry(1, 150)], 10, Table));
    }

    [Fact]
    public void Whole_level_is_null_when_unreachable_at_200_or_without_the_table_rows()
    {
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 100), Entry(2, 49)], 10, Table));
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 1_000_000)], 200, Table));
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 1_000_000)], 11, Table));
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 1_000_000)], 50, Table));
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 1_000_000)], 10, new Dictionary<int, long>()));
        Assert.Null(QuestPlanning.WholeLevel([Entry(1, 1_000_000)], 10, new Dictionary<int, long> { [10] = 5, [11] = 5 }));
        Assert.Null(QuestPlanning.WholeLevel([], 10, Table));
    }

    [Fact]
    public void Team_leaves_out_what_the_short_lists_leave_out()
    {
        var catalog = new[] { Quest(1), Quest(2) };
        var facts = new Dictionary<int, TouchQuestFacts>
        {
            [1] = new([Step(10, 1f)], [new TouchObjective(0, [1], 10, "Vider le trousseau")]),
            [2] = Xp(1f),
        };
        var team = QuestPlanning.Team(catalog, facts, [Character("A", 10, Progress([1, 2])), Character("B", 10, Progress([1, 2]))], 0);

        Assert.Equal([2], team.Select(t => t.Entry.Quests.Single().Id));
    }
}
