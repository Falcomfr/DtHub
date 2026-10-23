using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestTimeTests
{
    private static TouchObjective Obj(int type, int[]? p = null, int step = 0, int? x = null, int? y = null, string text = "") =>
        new(type, p ?? [], 10, text, x, y, step);

    private static TouchQuestFacts Facts(params TouchObjective[] objectives) => new([], objectives);

    private static int Seconds(TouchObjective o, params TouchObjective[] others) =>
        QuestTime.SecondsOf(o, Facts([o, .. others]));

    [Theory]
    [InlineData(1, 30)]
    [InlineData(9, 30)]
    [InlineData(2, 30)]
    [InlineData(4, 30)]
    [InlineData(20, 60)]
    [InlineData(12, 300)]
    [InlineData(0, 60)]
    [InlineData(10, 60)]
    [InlineData(99, 60)]
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
    public void A_quest_bringing_twenty_three_and_ten_harvested_items_takes_about_fifteen_minutes()
    {
        // "Agriculture et Alchimie" (11038): 180 + 410 + 70 + 210 = 870 s.
        var facts = Facts(Obj(3, [1, 2, 20]), Obj(3, [1, 2, 3]), Obj(3, [1, 2, 10]));

        Assert.Equal(15, QuestTime.EstimateMinutes(facts, null));
    }

    [Theory]
    [InlineData("Braver les dangers de la Serre du Royalmouth", 900)]
    [InlineData("capturer l'âme du Chafer", 900)]
    [InlineData("Aider les habitants d'Albuera", 1800)]
    [InlineData("Terminer les quêtes du village", 1800)]
    [InlineData("Trouver le garde", 60)]
    [InlineData("", 60)]
    public void Free_text_is_a_minute_but_a_dungeon_a_boss_or_a_group_of_quests(string text, int expected)
    {
        Assert.Equal(expected, Seconds(Obj(0, text: text)));
        Assert.Equal(expected, Seconds(Obj(10, text: text)));
    }

    [Fact]
    public void The_free_text_words_never_price_a_map()
    {
        Assert.Equal(30, Seconds(Obj(4, text: "Terminer le chantier")));
        Assert.Equal(30, Seconds(Obj(4, text: "Braver la tempête")));
    }

    [Fact]
    public void Objectives_without_a_known_step_never_make_a_dungeon_run()
    {
        // A cache read before steps were kept: null must not match null.
        var boss = new TouchObjective(6, [1, 1], 10);
        var exit = new TouchObjective(4, [], 10, "Sortie du donjon");

        Assert.Equal(60, Seconds(boss, exit));
        Assert.Equal(30, Seconds(exit, boss));
    }

    [Fact]
    public void Fights_cost_per_monster_up_to_ten()
    {
        Assert.Equal(300, Seconds(Obj(14, [1, 5])));
        Assert.Equal(300, Seconds(Obj(6, [1, 5])));
        Assert.Equal(600, Seconds(Obj(6, [1, 40])));
        Assert.Equal(60, Seconds(Obj(6, [1, 1])));
        Assert.Equal(60, Seconds(Obj(14, [1])));
    }

    [Theory]
    [InlineData("Sortie du Donjon des Bouftous")]
    [InlineData("exit of the Flooded Chapel")]
    [InlineData("Salida de la mazmorra")]
    public void A_single_fight_beside_a_dungeon_exit_in_the_same_step_is_a_dungeon_run(string text)
    {
        var boss = Obj(6, [1, 1], step: 2);
        var exit = Obj(4, [1], step: 2, text: text);

        Assert.Equal(900, Seconds(boss, exit));
        Assert.Equal(0, Seconds(exit, boss));
        // In another step, the map is a map and the fight a fight.
        Assert.Equal(60, Seconds(boss, Obj(4, [1], step: 3, text: text)));
        Assert.Equal(30, Seconds(Obj(4, [1], step: 3, text: text), boss));
        // A type 14 fight is no dungeon run.
        Assert.Equal(60, Seconds(Obj(14, [1, 1], step: 2), exit));
    }

    [Fact]
    public void A_single_fight_beside_any_other_map_is_a_fight()
    {
        // "Les indésirables d'Albuera": "Chantier du Barrage" and a one-monster fight in the same step.
        var fight = Obj(6, [1, 1], step: 1);
        var map = Obj(4, [1], step: 1, text: "Chantier du Barrage");

        Assert.Equal(60, Seconds(fight, map));
        Assert.Equal(30, Seconds(map, fight));
    }

    [Fact]
    public void Travel_counts_maps_between_known_coordinates_from_the_start()
    {
        var facts = Facts(Obj(1, x: 3, y: 4), Obj(1), Obj(1, x: 3, y: -1));

        // 7 maps then 5 maps, 4 s each; the objective without coordinates is skipped.
        Assert.Equal(48, QuestTime.TravelSeconds(facts, (0, 0)));
        // Unknown start: the first hop is not counted.
        Assert.Equal(20, QuestTime.TravelSeconds(facts, null));
        // A long hop is a zaap.
        Assert.Equal(180 + 20, QuestTime.TravelSeconds(facts, (-50, 4)));
    }

    [Fact]
    public void Minutes_are_the_base_the_objectives_and_the_travel_rounded_up()
    {
        // 180 + 30 + 300 + 4 = 514 s.
        Assert.Equal(9, QuestTime.EstimateMinutes(Facts(Obj(1, x: 1, y: 0), Obj(14, [1, 5])), (0, 0)));
        // 180 s alone.
        Assert.Equal(3, QuestTime.EstimateMinutes(Facts(), null));
    }

    [Fact]
    public void A_dungeon_run_is_three_minutes_a_room_and_two_more_or_fifteen_unknown()
    {
        var rooms = new Dictionary<int, int> { [50] = 5, [51] = 2 };

        // A Papycha dungeon of several game dungeons: the biggest.
        Assert.Equal(17, QuestTime.DungeonMinutes([50, 51], rooms));
        // A names cache read before rooms were.
        Assert.Equal(15, QuestTime.DungeonMinutes([60], rooms));
        Assert.Equal(15, QuestTime.DungeonMinutes([], rooms));
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
    [Fact]
    public void A_dungeon_run_is_a_lone_fight_beside_an_exit_map_of_its_step()
    {
        Assert.True(QuestTime.HasDungeonRun(Facts(Obj(6, [1, 1], step: 2), Obj(4, [1], step: 2, text: "Sortie du Donjon des Bouftous"))));
        Assert.False(QuestTime.HasDungeonRun(Facts(Obj(6, [1, 1], step: 2), Obj(4, [1], step: 2, text: "Chantier du Barrage"))));
        Assert.False(QuestTime.HasDungeonRun(Facts(Obj(6, [1, 1], step: 2), Obj(4, [1], step: 3, text: "Sortie du Donjon des Bouftous"))));
        Assert.False(QuestTime.HasDungeonRun(Facts(Obj(1))));
    }
}
