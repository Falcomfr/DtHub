namespace DtHub.Core.Papycha;

/// <summary>
/// A monster that drops an item; <see cref="NormalName"/> only for an archimonster whose normal version was found.
/// <see cref="Conditional"/>: drops it only under a condition, usually while the quest is active.
/// </summary>
public sealed record MonsterSource(
    int Id, string Name, int Level, bool IsArchimonster, string? NormalName, IReadOnlyList<string> SubAreas, bool Conditional = false);

/// <summary>A job and the level it needs. A record, not a tuple: the JSON cache drops tuple fields.</summary>
public sealed record JobLevel(string Job, int Level);

/// <summary>Where an item comes from; <see cref="NoKnownSource"/> when the game names no monster, harvest or recipe (event or shop).</summary>
public sealed record ItemSource(
    IReadOnlyList<MonsterSource> Droppers, IReadOnlyList<JobLevel> Harvest, IReadOnlyList<JobLevel> Craft, bool NoKnownSource);

/// <summary>
/// Item sources read from the game at runtime and cached on disk, one entry
/// per language. Nothing here ships with the application, see AGENTS.md.
/// </summary>
public sealed class ItemSourcesDocument
{
    public Dictionary<string, ItemSourcesLanguage> Languages { get; set; } = [];
}

public sealed record ItemSourcesLanguage
{
    /// <summary>Harvest skills by gathered item id, from the whole Skills class (about 90 KB).</summary>
    public Dictionary<int, JobLevel[]> Harvest { get; init; } = [];

    public DateTimeOffset HarvestReadUtc { get; init; }

    /// <summary>By game item id.</summary>
    public Dictionary<int, CachedItemSource> Items { get; init; } = [];
}

public sealed record CachedItemSource(DateTimeOffset ReadUtc, ItemSource Source);
