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
}

/// <summary>
/// Résultats d'une recherche, rangés par nature.
///
/// Trois listes plutôt qu'une seule : une zone, un succès et une quête ne se
/// choisissent pas de la même façon, et les mêler obligerait à lire chaque
/// ligne pour deviner ce qu'elle est. Les donjons prendront naturellement leur
/// place ici.
/// </summary>
/// <param name="Zones">Rubriques dont le nom correspond.</param>
/// <param name="Successes">Succès dont le nom correspond.</param>
/// <param name="Quests">Quêtes dont le titre ou la rubrique correspond.</param>
public sealed record QuestSearchResults(
    IReadOnlyList<QuestSection> Zones,
    IReadOnlyList<string> Successes,
    IReadOnlyList<QuestSummary> Quests)
{
    public static readonly QuestSearchResults Empty = new([], [], []);

    /// <summary>Vrai quand rien n'a été trouvé, quelle que soit la nature.</summary>
    public bool IsEmpty => Zones.Count == 0 && Successes.Count == 0 && Quests.Count == 0;
}
