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
}
