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
}

public sealed record TouchQuest(string Name, int LevelMin, int LevelMax, int[] StepIds);

public sealed record TouchDungeon(string Name, int KeyId);

public sealed record TouchStep(string Name, string Description, TouchObjectiveLine[] Objectives);

/// <summary>Position is "[-3,-44]", or empty when the objective has none.</summary>
public sealed record TouchObjectiveLine(string Text, string Position);
