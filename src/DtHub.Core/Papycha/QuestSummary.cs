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
    ///
    /// Bâti sur les rubriques réellement affichées, et sous leur nom d'affichage.
    /// Il l'était sur les catégories brutes du site : les sept rubriques venues
    /// d'une page n'étaient donc cherchables par aucun chemin, et comme la
    /// racine y figurait, les 782 quêtes portaient le mot « quêtes ».
    /// </summary>
    public string SectionKey { get; init; } = string.Empty;

    /// <summary>
    /// Rubrique retenue pour situer la quête dans une liste de recherche.
    ///
    /// La plus petite de celles auxquelles elle appartient : c'est la plus
    /// précise, donc celle qui situe. « Astrub » situe mieux que « Quêtes ».
    /// </summary>
    public int SectionId { get; init; }

    /// <summary>
    /// Toutes les rubriques auxquelles la quête appartient.
    ///
    /// Une seule ne suffisait pas. Le site range « Le dragon d'Astrub » à la
    /// fois dans ses quêtes principales et dans celles d'Astrub : une quête est
    /// un lieu et un cheminement, et forcer un choix vidait les rubriques
    /// transversales. Mesuré : la page des quêtes principales en énumère
    /// soixante-treize, dont douze seulement n'avaient pas de zone et étaient
    /// donc les seules à y rester.
    /// </summary>
    public IReadOnlyList<int> SectionIds { get; init; } = [];

    /// <summary>
    /// Succès dont la quête fait partie, vide quand le site ne le dit pas.
    ///
    /// Le rattachement se lit sur les pages de rubrique, qui groupent leurs
    /// quêtes sous des intertitres. Mesuré : trois cent soixante-treize quêtes
    /// sur sept cent quatre-vingt-deux en portent un. Les autres n'en portent
    /// pas, et rien ne doit leur en inventer.
    /// </summary>
    public string SuccessName { get; init; } = string.Empty;

    /// <summary>
    /// Place de la quête dans sa chaîne de prérequis, zéro si le site ne la
    /// donne pas.
    ///
    /// Ce n'est pas sa place dans son succès : les trois quêtes de « De la
    /// caillasse plein les poches » y valent 1, 6 et 6, et la chaîne qui
    /// compte sept étapes traverse plusieurs succès. C'est tout de même le seul
    /// ordre de jeu que le site publie, et il vaut mieux que l'ordre
    /// alphabétique pour présenter les quêtes d'un succès.
    /// </summary>
    public int ChainStep { get; init; }

    /// <summary>
    /// Place de la quête dans son succès, zéro si on ne la connaît pas.
    ///
    /// Calculée à partir des prérequis que le site publie, qui donnent un ordre
    /// partiel : « Les rescapés de Frigost » exige « [FIN] L'essentiel est dans
    /// le Lac gelé », donc celle-ci vient avant. Le site ne publie cet ordre
    /// nulle part ailleurs pour la plupart des succès.
    /// </summary>
    public int PlayOrder { get; init; }
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
