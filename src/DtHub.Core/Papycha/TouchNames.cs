namespace DtHub.Core.Papycha;

/// <summary>
/// The game's own names, read at runtime and cached on disk, one entry per
/// language. Nothing here ships with the application, see AGENTS.md.
/// </summary>
public sealed class TouchNamesDocument
{
    public Dictionary<string, TouchLanguageNames> Languages { get; set; } = [];
}

public sealed record TouchLanguageNames
{
    public DateTimeOffset ReadUtc { get; init; }

    /// <summary>By game quest id.</summary>
    public Dictionary<int, TouchQuest> Quests { get; init; } = [];

    /// <summary>By game dungeon id.</summary>
    public Dictionary<int, TouchDungeon> Dungeons { get; init; } = [];

    public Dictionary<int, string> Monsters { get; init; } = [];

    /// <summary>Dungeon keys.</summary>
    public Dictionary<int, string> Items { get; init; } = [];

    public Dictionary<int, string> Achievements { get; init; } = [];

    public Dictionary<int, string> ObjectiveTypes { get; init; } = [];

    /// <summary>By Papycha quest id.</summary>
    public Dictionary<int, TouchStep[]> Steps { get; init; } = [];

    /// <summary>By Papycha quest id; read in bulk with its own freshness, so a names refresh keeps it.</summary>
    public Dictionary<int, TouchQuestFacts> Facts { get; init; } = [];

    public DateTimeOffset FactsReadUtc { get; init; }

    /// <summary>
    /// By game achievement id, the rewards of the achievements made of paired quests only (<see cref="TouchIdTable.Whole"/>),
    /// read with the facts; null in a cache read before them.
    /// </summary>
    public Dictionary<int, TouchAchievementReward[]>? AchievementRewards { get; init; }
}

/// <summary>Steps in game order; a null step lacks a formula field, and all are null when the server left one out.</summary>
public sealed record TouchQuestFacts(TouchStepReward?[] Steps, TouchObjective[] Objectives);

/// <summary>
/// <c>Text</c> is the data/text of types 0, 4 and 10, empty otherwise; null in a cache read before texts were.
/// <c>X</c>, <c>Y</c>: the objective's map, null when it has none. <c>Step</c>: its step's index in the quest, null in a
/// cache read before coordinates were.
/// </summary>
public sealed record TouchObjective(
    int TypeId, int[] Parameters, int StepOptimalLevel, string? Text = null, int? X = null, int? Y = null, int? Step = null);

public sealed record TouchQuest(string Name, int LevelMin, int LevelMax, int[] StepIds);

/// <summary><c>Rooms</c>: the game's <c>mapIds</c> count, 0 in a cache read before rooms were.</summary>
public sealed record TouchDungeon(string Name, int KeyId, int Rooms = 0);

public sealed record TouchStep(
    string Name, string Description, TouchObjectiveLine[] Objectives, TouchStepReward? Reward = null);

public sealed record TouchStepReward(
    int OptimalLevel, float Duration, float XpRatio, float KamasRatio, bool KamasScaleWithPlayerLevel,
    TouchRewardRow[] Rows);

/// <summary>-1 = no bound.</summary>
public sealed record TouchRewardRow(
    int LevelMin, int LevelMax, TouchItemReward[] Items,
    int Emotes, int Spells, int Titles, int Ornaments, int Companions, int Jobs);

public sealed record TouchItemReward(int ItemId, int Quantity);

/// <summary>
/// One reward row of a game achievement: the quest formula at the achievement's level over a duration of 1, its items
/// and the rest in <c>Formula.Rows</c>; <c>Account</c> when the game gives it to the account, not the character.
/// </summary>
public sealed record TouchAchievementReward(TouchStepReward Formula, bool Account);

/// <summary>Position is "[-3,-44]", or empty when the objective has none.</summary>
public sealed record TouchObjectiveLine(string Text, string Position);
