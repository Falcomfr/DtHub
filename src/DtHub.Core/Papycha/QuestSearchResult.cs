namespace DtHub.Core.Papycha;

/// <summary>What a search has found.</summary>
public enum QuestSearchKind
{
    /// <summary>A quest zone.</summary>
    Zone,

    /// <summary>An achievement, with the quests that make it up.</summary>
    Success,

    /// <summary>A quest.</summary>
    Quest,

    /// <summary>A dungeon, a raid or a lair.</summary>
    Dungeon,

    /// <summary>A path.</summary>
    Path,
}

/// <summary>
/// Search results, sorted by kind.
///
/// Four lists rather than one: a zone, an achievement, a quest and
/// a dungeon are not picked the same way, and mixing them would
/// force reading every line to guess what it is.
/// </summary>
/// <param name="Zones">Categories whose name matches.</param>
/// <param name="Successes">Achievements whose name matches.</param>
/// <param name="Quests">Quests whose title matches.</param>
/// <param name="Dungeons">
/// Dungeons, raids and lairs whose name matches.
/// </param>
/// <param name="Paths">Paths whose name matches.</param>
public sealed record QuestSearchResults(
    IReadOnlyList<QuestSection> Zones,
    IReadOnlyList<string> Successes,
    IReadOnlyList<QuestSummary> Quests,
    IReadOnlyList<DungeonSummary> Dungeons,
    IReadOnlyList<PathSummary> Paths)
{
    public static readonly QuestSearchResults Empty = new([], [], [], [], []);

    /// <summary>True when nothing was found, regardless of kind.</summary>
    public bool IsEmpty =>
        Zones.Count == 0
        && Successes.Count == 0
        && Quests.Count == 0
        && Dungeons.Count == 0
        && Paths.Count == 0;

    /// <summary>Combat locations of a given kind, in level order.</summary>
    public IEnumerable<DungeonSummary> Of(DungeonKind kind) => Dungeons.Where(d => d.Kind == kind);
}
