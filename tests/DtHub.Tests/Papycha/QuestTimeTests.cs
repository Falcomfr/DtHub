using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestTimeTests
{
    private static TouchObjective Obj(int type, int[]? p = null, int step = 0, int? x = null, int? y = null) =>
        new(type, p ?? [], 10, "", x, y, step);

    private static TouchQuestFacts Facts(params TouchObjective[] objectives) => new([], objectives);

    private static int Seconds(TouchObjective o, params TouchObjective[] others) =>
        QuestTime.SecondsOf(o, Facts([o, .. others]));

    [Theory]
    [InlineData(1, 15)]
    [InlineData(9, 15)]
    [InlineData(2, 15)]
    [InlineData(4, 45)]
    [InlineData(20, 60)]
    [InlineData(12, 300)]
    [InlineData(0, 300)]
    [InlineData(10, 300)]
    [InlineData(99, 300)]
    public void Each_objective_type_has_its_seconds(int type, int expected) =>
        Assert.Equal(expected, Seconds(Obj(type)));

    [Theory]
    [InlineData(new[] { 1, 2, 5 }, 110)]
    [InlineData(new[] { 1, 2, 59 }, 1190)]
    [InlineData(new[] { 1, 2, 60 }, 1200)]
    [InlineData(new[] { 1, 2, 1000 }, 1200)]
    [InlineData(new[] { 1, 2 }, 10)]
    public void Bringing_items_grows_with_the_quantity_up_to_twenty_minutes(int[] parameters, int expected) =>
        Assert.Equal(expected, Seconds(Obj(3, parameters)));

    [Fact]
    public void A_quest_bringing_twenty_three_and_ten_harvested_items_takes_about_thirteen_minutes()
    {
        // "Agriculture et Alchimie" (11038): 90 + 410 + 70 + 210 = 780 s, where 10 + 5 per item gave 5 min.
        var facts = Facts(Obj(3, [1, 2, 20]), Obj(3, [1, 2, 3]), Obj(3, [1, 2, 10]));

        Assert.Equal(13, QuestTime.EstimateMinutes(facts, null));
    }

    [Fact]
    public void Objectives_without_a_known_step_never_make_a_dungeon_run()
    {
        // A cache read before steps were kept: null must not match null.
        var boss = new TouchObjective(6, [1, 1], 10);
        var exit = new TouchObjective(4, [], 10);

        Assert.Equal(90, Seconds(boss, exit));
        Assert.Equal(45, Seconds(exit, boss));
    }

    [Fact]
    public void Fights_cost_per_monster_up_to_ten()
    {
        Assert.Equal(450, Seconds(Obj(14, [1, 5])));
        Assert.Equal(450, Seconds(Obj(6, [1, 5])));
        Assert.Equal(900, Seconds(Obj(6, [1, 40])));
        Assert.Equal(90, Seconds(Obj(6, [1, 1])));
        Assert.Equal(90, Seconds(Obj(14, [1])));
    }

    [Fact]
    public void A_single_fight_beside_a_map_in_the_same_step_is_a_dungeon_run()
    {
        var boss = Obj(6, [1, 1], step: 2);
        var exit = Obj(4, [1], step: 2);

        Assert.Equal(900, Seconds(boss, exit));
        Assert.Equal(0, Seconds(exit, boss));
        // In another step, the map is a map and the fight a fight.
        Assert.Equal(90, Seconds(boss, Obj(4, [1], step: 3)));
        Assert.Equal(45, Seconds(Obj(4, [1], step: 3), boss));
        // A type 14 fight is no dungeon run.
        Assert.Equal(90, Seconds(Obj(14, [1, 1], step: 2), exit));
    }

    [Fact]
    public void Travel_counts_maps_between_known_coordinates_from_the_start()
    {
        var facts = Facts(Obj(1, x: 3, y: 4), Obj(1), Obj(1, x: 3, y: -1));

        // 7 maps then 5 maps, 8 s each; the objective without coordinates is skipped.
        Assert.Equal(96, QuestTime.TravelSeconds(facts, (0, 0)));
        // Unknown start: the first hop is not counted.
        Assert.Equal(40, QuestTime.TravelSeconds(facts, null));
        // A long hop is a zaap.
        Assert.Equal(300 + 40, QuestTime.TravelSeconds(facts, (-50, 4)));
    }

    [Fact]
    public void Minutes_are_the_base_the_objectives_and_the_travel_rounded_up()
    {
        // 90 + 15 + 450 + 8 = 563 s.
        Assert.Equal(10, QuestTime.EstimateMinutes(Facts(Obj(1, x: 1, y: 0), Obj(14, [1, 5])), (0, 0)));
        // 90 s alone.
        Assert.Equal(2, QuestTime.EstimateMinutes(Facts(), null));
    }

    [Theory]
    [InlineData("[15,28]", 15, 28)]
    [InlineData("[-54,34] ou [-49,47]", -54, 34)]
    [InlineData("[ -3 , -44 ]", -3, -44)]
    public void The_start_is_the_first_coordinates_of_the_quest(string position, int x, int y) =>
        Assert.Equal((x, y), QuestTime.StartOf(new QuestSummary { StartPosition = position }));

    [Theory]
    [InlineData("")]
    [InlineData("Astrub")]
    public void An_unknown_start_is_null(string position) =>
        Assert.Null(QuestTime.StartOf(new QuestSummary { StartPosition = position }));

    [Theory]
    [InlineData(25, "≈ 25 min")]
    [InlineData(59, "≈ 59 min")]
    [InlineData(60, "≈ 1 h 00")]
    [InlineData(125, "≈ 2 h 05")]
    public void The_text_switches_to_hours_from_sixty_minutes(int minutes, string expected) =>
        Assert.Equal(expected, QuestTime.Text(minutes));
}
