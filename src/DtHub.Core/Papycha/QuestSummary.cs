namespace DtHub.Core.Papycha;

/// <summary>
/// Une quête, telle que le catalogue la retient.
///
/// Volontairement sans le corps de l'article : l'indexer coûterait vingt-deux
/// mégaoctets et trente fois plus de transfert, pour une information qu'on
/// obtient gratuitement en ouvrant la page. Ce qui est ici suffit à chercher
/// et à ranger.
/// </summary>
public sealed record QuestSummary
{
    /// <summary>Identifiant de l'article sur le site.</summary>
    public int Id { get; init; }

    /// <summary>Titre affiché.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Adresse de la page.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Niveau conseillé, ou zéro quand le site ne le donne pas.</summary>
    public int Level { get; init; }

    /// <summary>Catégories du site, dont la zone.</summary>
    public IReadOnlyList<int> Categories { get; init; } = [];

    /// <summary>Types de quête, au sens de la taxonomie du site.</summary>
    public IReadOnlyList<int> Types { get; init; } = [];

    /// <summary>Titre réduit à une forme comparable, calculé une fois.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>
    /// Noms des rubriques de la quête, sous la même forme réduite.
    ///
    /// Chercher « frigost » ne rendait que quatre quêtes, celles dont le titre
    /// porte le mot, alors que cent quatre-vingt-quatre s'y déroulent. On
    /// cherche un endroit autant qu'un nom.
    /// </summary>
    public string SectionKey { get; init; } = string.Empty;
}

/// <summary>Une rubrique de l'arbre : une catégorie ou un type du site.</summary>
public sealed record QuestSection
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Rubrique parente, ou zéro à la racine.</summary>
    public int Parent { get; init; }

    /// <summary>Nombre de quêtes rangées directement dessous.</summary>
    public int Count { get; init; }

    public string SearchKey { get; init; } = string.Empty;
}
