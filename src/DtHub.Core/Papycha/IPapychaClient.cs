namespace DtHub.Core.Papycha;

/// <summary>
/// Accès en lecture au site, isolé derrière une interface pour que le noyau
/// reste sans réseau et que le catalogue se vérifie sur des réponses
/// enregistrées.
/// </summary>
public interface IPapychaClient
{
    /// <summary>
    /// Récupère toutes les quêtes, page après page.
    ///
    /// <paramref name="progress"/> reçoit le nombre de quêtes déjà lues et le
    /// total annoncé par le site : l'indexation dure quelques secondes et doit
    /// se voir.
    /// </summary>
    Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Récupère les rubriques qui rangent les quêtes.</summary>
    Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère le classement que le site tient à la main sur sa page
    /// « Quêtes », et les quêtes que chaque rubrique énumère.
    ///
    /// Les catégories ne suffisent pas : mesuré sur les 782 quêtes, elles en
    /// laissent 150 sans rubrique. Ces pages en réclament 120 et nomment des
    /// ensembles qu'aucune catégorie ne porte.
    ///
    /// Rend une liste vide si le site ne répond pas : le catalogue reste
    /// utilisable sur ses seules catégories.
    /// </summary>
    Task<IReadOnlyList<QuestPageSection>> GetPageSectionsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Avancement d'une indexation.</summary>
/// <param name="Loaded">Quêtes déjà lues.</param>
/// <param name="Total">Total annoncé, ou zéro tant qu'il est inconnu.</param>
public readonly record struct QuestIndexingProgress(int Loaded, int Total)
{
    /// <summary>Part accomplie, de zéro à un. Zéro tant que le total manque.</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Loaded / Total, 0, 1) : 0;
}
