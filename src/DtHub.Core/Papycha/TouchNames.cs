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
}

/// <summary>Steps in game order; a null step lacks a formula field.</summary>
public sealed record TouchQuestFacts(TouchStepReward?[] Steps, TouchObjective[] Objectives);

/// <summary>
/// <c>Text</c> is the data/text of types 0, 4 and 10, empty otherwise; null in a cache read before texts were.
/// <c>X</c>, <c>Y</c>: the objective's map, null when it has none. <c>Step</c>: its step's index in the quest, null in a
/// cache read before coordinates were.
/// </summary>
public sealed record TouchObjective(
    int TypeId, int[] Parameters, int StepOptimalLevel, string? Text = null, int? X = null, int? Y = null, int? Step = null);

public sealed record TouchQuest(string Name, int LevelMin, int LevelMax, int[] StepIds);

public sealed record TouchDungeon(string Name, int KeyId);

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

/// <summary>Position is "[-3,-44]", or empty when the objective has none.</summary>
public sealed record TouchObjectiveLine(string Text, string Position);
