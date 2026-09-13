namespace DtHub.Core.Papycha;

/// <summary>
/// A combat location from the site - a dungeon, a raid or a lair -
/// as needed to choose it.
///
/// The site publishes eighty three of them, in a format far more
/// regular than its quests: the level, the position and the
/// character come from its metadata, the key and the soul stone from
/// a block whose classes are code, not labels.
///
/// Nothing that comes from Ankama's servers - the boss and key
/// thumbnails - is carried over here: the list stays plain text.
/// </summary>
public sealed record DungeonSummary
{
    public int Id { get; init; }

    /// <summary>
    /// Dungeon, raid or lair, according to the site's category.
    /// </summary>
    public DungeonKind Kind { get; init; }

    /// <summary>
    /// Dungeon name, without the site's "[Donjon]" ("[Dungeon]")
    /// prefix.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    /// <summary>Normalized title, for search.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>
    /// Recommended level. Zero when the site does not provide it,
    /// which happens for three dungeons: those are sorted separately
    /// rather than at level zero.
    /// </summary>
    public int Level { get; init; }

    /// <summary>Entrance coordinates, "[9,-57]".</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>Character to talk to in order to enter.</summary>
    public string Person { get; init; } = string.Empty;

    /// <summary>
    /// Key required at the entrance. Empty for nine dungeons, which
    /// do not ask for one: the absence is information, not a gap.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Size of the soul stone, exactly as the site writes it:
    /// "petite" (small), "moyenne" (medium), "grande" (large) or
    /// "gigantesque" (huge).
    /// </summary>
    public string SoulStone { get; init; } = string.Empty;

    /// <summary>True if a key is required.</summary>
    public bool NeedsKey => Key.Length > 0;
}
