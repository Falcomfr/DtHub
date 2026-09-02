namespace DtHub.Core.Papycha;

/// <summary>Ce qu'une recherche a trouvé.</summary>
public enum QuestSearchKind
{
    /// <summary>Une zone de quêtes.</summary>
    Zone,

    /// <summary>Un succès, avec les quêtes qui le composent.</summary>
    Success,

    /// <summary>Une quête.</summary>
    Quest,

    /// <summary>Un donjon, un raid ou une tanière.</summary>
    Dungeon,

    /// <summary>Un chemin.</summary>
    Path,
}

/// <summary>
/// Résultats d'une recherche, rangés par nature.
///
/// Quatre listes plutôt qu'une seule : une zone, un succès, une quête et un
/// donjon ne se choisissent pas de la même façon, et les mêler obligerait à
/// lire chaque ligne pour deviner ce qu'elle est.
/// </summary>
/// <param name="Zones">Rubriques dont le nom correspond.</param>
/// <param name="Successes">Succès dont le nom correspond.</param>
/// <param name="Quests">Quêtes dont le titre correspond.</param>
/// <param name="Dungeons">Donjons, raids et tanières dont le nom correspond.</param>
/// <param name="Paths">Chemins dont le nom correspond.</param>
public sealed record QuestSearchResults(
    IReadOnlyList<QuestSection> Zones,
    IReadOnlyList<string> Successes,
    IReadOnlyList<QuestSummary> Quests,
    IReadOnlyList<DungeonSummary> Dungeons,
    IReadOnlyList<PathSummary> Paths)
{
    public static readonly QuestSearchResults Empty = new([], [], [], [], []);

    /// <summary>Vrai quand rien n'a été trouvé, quelle que soit la nature.</summary>
    public bool IsEmpty =>
        Zones.Count == 0
        && Successes.Count == 0
        && Quests.Count == 0
        && Dungeons.Count == 0
        && Paths.Count == 0;

    /// <summary>Les lieux de combat d'un genre donné, dans l'ordre des niveaux.</summary>
    public IEnumerable<DungeonSummary> Of(DungeonKind kind) => Dungeons.Where(d => d.Kind == kind);
}
