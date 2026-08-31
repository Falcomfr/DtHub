namespace DtHub.Core.Papycha;

/// <summary>
/// Une rubrique telle que le site la présente sur sa page « Quêtes ».
///
/// Ce ne sont pas des catégories WordPress mais des pages tenues à la main.
/// La distinction compte : mesuré sur les 782 quêtes, les catégories en
/// rangent 632 et laissent les 150 autres sans rubrique, atteignables par la
/// seule recherche. Ces pages en réclament 120 de plus et nomment des
/// ensembles qu'aucune catégorie ne porte, comme le Krosmoz, Sufokia ou les
/// Bulles Temporelles.
/// </summary>
public sealed record QuestPageSection
{
    /// <summary>Intitulé donné par le site, « Quêtes du Krosmoz » par exemple.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Adresse de la page.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Adresses des quêtes qu'elle énumère.</summary>
    public IReadOnlyList<string> QuestUrls { get; init; } = [];

    /// <summary>Groupes de la page, dans son ordre.</summary>
    public IReadOnlyList<QuestPageGroup> Groups { get; init; } = [];
}

/// <summary>
/// Un intertitre d'une page de rubrique et les quêtes qu'il coiffe.
///
/// Le site groupe ses quêtes par succès, sous la forme
/// <c>&lt;strong&gt;[Succès] Nom :&lt;/strong&gt;</c> suivie d'une liste. C'est
/// le seul endroit où le rattachement d'une quête à son succès soit lisible
/// sans ouvrir la page de la quête : le bloc d'intro le porte aussi, mais il
/// faudrait sept cent quatre-vingt-deux requêtes pour le lire partout.
///
/// Tous les intertitres ne sont pas des succès : une page dit aussi « Divers »
/// ou « Quêtes des Calanques d'Astrub ». Seuls les succès sont retenus comme
/// tels, le reste ne prétend pas en être.
/// </summary>
public sealed record QuestPageGroup
{
    /// <summary>Intitulé, sans la marque « [Succès] » ni le deux-points final.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Vrai si l'intertitre annonce un succès.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Adresses des quêtes qu'il coiffe.</summary>
    public IReadOnlyList<string> QuestUrls { get; init; } = [];
}
