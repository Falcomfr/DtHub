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
    public void Team_sums_a_quest_only_for_the_characters_it_is_certain_for()
    {
        var catalog = new[] { Quest(1), Quest(2) };
        // Two steps: in progress, DT Hub cannot tell what is left.
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = new([Step(10, 1f), Step(10, 1f)], []), [2] = Xp(1f) };
        var going = new TrackerProgress(
            new Dictionary<int, QuestStatus> { [1] = QuestStatus.InProgress }, new HashSet<int>(), new HashSet<int> { 2 }, []);
        var a = Character("A", 10, Progress([1, 2]));
        var b = Character("B", 10, Progress([1, 2]));
        var c = Character("C", 10, going);

        var team = QuestPlanning.Team(catalog, facts, [a, b, c], 0);

        // C has quest 1 in progress: the row is A's and B's, C's maximum left out of the sum.
        var first = team.Single(t => t.Entry.Quests[0].Id == 1);
        Assert.Equal(["A", "B"], first.Characters.Select(x => x.Name));
        Assert.Equal(2 * QuestProfit.GainOf(facts[1], 10, 0)!.Xp, first.Gain.Xp);
        Assert.Equal(["A", "B", "C"], team.Single(t => t.Entry.Quests[0].Id == 2).Characters.Select(x => x.Name));

        // Only A and C: C is out, A alone is no team row.
        Assert.DoesNotContain(QuestPlanning.Team(catalog, facts, [a, c], 0), t => t.Entry.Quests[0].Id == 1);
    }

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
    public void Team_matches_a_series_quest_by_its_id()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4) };
        var facts = catalog.ToDictionary(q => q.Id, _ => Xp(1f));
        var team = QuestPlanning.Team(
            catalog, facts,
            [Character("A", 10, Progress([1, 4])), Character("B", 10, Progress([2, 4], completed: [1])), Character("C", 10, Progress([2], completed: [1]))],
            0);

        // A's next quest of S is 1, B's and C's is 2: only quest 2 is shared there.
        Assert.Equal([2, 4], team.Select(t => t.Entry.Quests.Single().Id).Order());
        var two = Assert.Single(team, t => t.Entry.Quests[0].Id == 2);
        Assert.Equal("Q2", two.Entry.Title);
        Assert.Equal(["B", "C"], two.Characters.Select(c => c.Name));
        Assert.Equal(2 * QuestProfit.GainOf(facts[2], 10, 0)!.Xp, two.Gain.Xp);
    }

    [Fact]
    public void Team_counts_a_series_by_each_character_s_next_doable_quest_only()
    {
        QuestSummary[] catalog =
        [
            Quest(1, "S", 1), Quest(2, "S", 2) with { Prerequisites = ["Q1"] }, Quest(3, "S", 3) with { Prerequisites = ["Q2"] },
            Quest(4, "S", 4) with { Prerequisites = ["Q3"] },
        ];
        var facts = new Dictionary<int, TouchQuestFacts> { [1] = Xp(1f), [2] = Xp(1f), [3] = Xp(5f), [4] = Xp(5f) };
        // A's quest 2 is blocked: the site lists 3, but it follows 2, so A's next quest of S is 1 alone; B's is 3, C's is 1.
        var blocked = new TrackerProgress(
            new Dictionary<int, QuestStatus> { [2] = QuestStatus.Blocked }, new HashSet<int>(), new HashSet<int> { 1, 3 }, []);
        var team = QuestPlanning.Team(
            catalog, facts,
            [Character("A", 10, blocked), Character("B", 10, Progress([3], completed: [1, 2])), Character("C", 10, Progress([1]))],
            0);

        var one = Assert.Single(team);
        Assert.Equal([1], one.Entry.Quests.Select(q => q.Id));
        Assert.Equal(["A", "C"], one.Characters.Select(c => c.Name));
        Assert.Equal(2 * QuestProfit.GainOf(facts[1], 10, 0)!.Xp, one.Gain.Xp);
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

    [Fact]
    public void Team_runs_keep_a_series_two_characters_can_run_sum_their_gains_and_take_the_longest_time()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2), Quest(3, "S", 3), Quest(4, "T", 1), Quest(5, "T", 2) };
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));
        // A runs S 1-3 and T 4-5; B runs S 2-3 at level 30; C has only T's 5 left, a run of one.
        var team = QuestPlanning.TeamRuns(
            catalog, facts,
            [Character("A", 10, Progress([1, 4])), Character("B", 30, Progress([2], completed: [1])), Character("C", 10, Progress([5], completed: [4]))],
            0, dungeons: true, new Dictionary<int, string>());

        var s = Assert.Single(team);
        Assert.Equal("S", s.Entry.Title);
        Assert.Equal([1, 2, 3], s.Entry.Quests.Select(q => q.Id));
        Assert.Equal(["A", "B"], s.Characters.Select(c => c.Name));
        long Xp10(int id) => QuestProfit.GainOf(facts[id], 10, 0)!.Xp;
        long Xp30(int id) => QuestProfit.GainOf(facts[id], 30, 0)!.Xp;
        Assert.Equal(Xp10(1) + Xp10(2) + Xp10(3) + Xp30(2) + Xp30(3), s.Gain.Xp);
        // The longest run: three quests of 3 minutes.
        Assert.Equal(9, s.Entry.Effort.Minutes);
    }

    [Fact]
    public void Team_runs_need_two_characters()
    {
        var catalog = new[] { Quest(1, "S", 1), Quest(2, "S", 2) };
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(q.Id));

        Assert.Empty(QuestPlanning.TeamRuns(catalog, facts, [Character("A", 10, Progress([1]))], 0, true, new Dictionary<int, string>()));
    }

    [Fact]
    public void Team_runs_of_a_series_with_two_ways_are_grouped_by_way()
    {
        // 1 then two ways, 11-12-13 or 21-22-23. A has not chosen, B is on the first way, C on the second.
        QuestSummary[] catalog =
        [
            Quest(1, "S", 1),
            Quest(11, "S", 2) with { Prerequisites = ["Q1"], Alternatives = [21] }, Quest(21, "S", 3) with { Prerequisites = ["Q1"], Alternatives = [11] },
            Quest(12, "S", 4) with { Prerequisites = ["Q11"], Alternatives = [22] }, Quest(22, "S", 5) with { Prerequisites = ["Q21"], Alternatives = [12] },
            Quest(13, "S", 6) with { Prerequisites = ["Q12"], Alternatives = [23] }, Quest(23, "S", 7) with { Prerequisites = ["Q22"], Alternatives = [13] },
        ];
        var facts = catalog.ToDictionary(q => q.Id, q => Xp(1f));

        var team = QuestPlanning.TeamRuns(
            catalog, facts,
            [Character("A", 10, Progress([], completed: [1])), Character("B", 10, Progress([], completed: [1, 11])), Character("C", 10, Progress([], completed: [1, 21]))],
            0, dungeons: true, new Dictionary<int, string>());

        Assert.Equal(2, team.Count);
        Assert.Equal(["A", "B"], team.Single(t => t.Entry.Path!.Id == 11).Characters.Select(c => c.Name));
        Assert.Equal(["A", "C"], team.Single(t => t.Entry.Path!.Id == 21).Characters.Select(c => c.Name));
        Assert.Equal([11, 12, 13], team.Single(t => t.Entry.Path!.Id == 11).Entry.Quests.Select(q => q.Id));
    }
}
