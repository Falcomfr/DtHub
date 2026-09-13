namespace DtHub.Core.Papycha;

/// <summary>
/// A path from the site: the route to reach a place.
///
/// Paths do not form a separate family in the window. Some lead to
/// a dungeon, others to an island, a zaap or an underground area,
/// and then serve a quest: they are therefore sorted into one or
/// the other of the two branches, according to what
/// <see cref="PathTarget"/> decides.
/// </summary>
public sealed record PathSummary
{
    public int Id { get; init; }

    /// <summary>
    /// Path name, without the prefix the site adds to its titles.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    /// <summary>Normalized title, for search.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>The branch the path is sorted into.</summary>
    public PathSide Side { get; init; }
}

/// <summary>Which side a path is sorted into.</summary>
public enum PathSide
{
    /// <summary>
    /// It leads to an island, a zaap, a place: it serves quests.
    /// </summary>
    Quests,

    /// <summary>It leads to a dungeon.</summary>
    Dungeons,
}
