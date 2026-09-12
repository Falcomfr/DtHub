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
    /// L'empreinte du site, ou <c>null</c> s'il ne répond pas. Sert à savoir
    /// s'il a bougé sans le relire.
    /// </summary>
    Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// L'empreinte de chacune des catégories qu'on lit, ou une liste vide si le
    /// site ne répond pas. Sert à ne relire que ce qui a bougé.
    /// </summary>
    Task<IReadOnlyList<CategoryStamp>> GetCategoryStampsAsync(
        CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Récupère les donjons, contenu des pages compris.
    ///
    /// Le contenu est demandé avec le reste et non page par page : le niveau,
    /// la position et le personnage sont dans les métadonnées, mais la clef et
    /// la pierre d'âme ne vivent que dans le corps de l'article. Les demander
    /// séparément coûterait quatre-vingt-trois requêtes là où une suffit.
    /// </summary>
    Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les chemins, et décide de quel côté chacun se range.
    ///
    /// Les noms des donjons sont demandés parce que la décision en dépend : un
    /// chemin va aux donjons quand son titre en nomme un.
    /// </summary>
    Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        IReadOnlyList<string> dungeonTitles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Ce que l'indexation est en train de lire.
///
/// **Ces étapes existent parce que l'attente mentait.** Une seule d'entre elles
/// rapportait son avancement, celle des quêtes, et c'est la plus courte. Le
/// compteur montait donc jusqu'à « 782 / 782 » en quelques secondes, puis
/// restait figé là pendant les quatre cinquièmes du temps, les donjons pesant à
/// eux seuls quatre mégaoctets.
/// </summary>
public enum QuestIndexingPhase
{
    /// <summary>Les quêtes, seule étape qui se compte.</summary>
    Quests,

    /// <summary>Les rubriques et leurs pages, une vingtaine de lectures.</summary>
    Sections,

    /// <summary>Les donjons, raids et tanières. La plus lourde de loin.</summary>
    Dungeons,

    /// <summary>Les chemins, qui se déduisent des donjons.</summary>
    Paths,

    /// <summary>Le rangement, qui ne touche plus au réseau.</summary>
    Arranging,
}

/// <summary>Avancement d'une indexation.</summary>
/// <param name="Loaded">Éléments déjà lus.</param>
/// <param name="Total">Total annoncé, ou zéro tant qu'il est inconnu.</param>
/// <param name="Phase">Ce qu'on lit en ce moment.</param>
public readonly record struct QuestIndexingProgress(
    int Loaded,
    int Total,
    QuestIndexingPhase Phase = QuestIndexingPhase.Quests)
{
    /// <summary>Part accomplie, de zéro à un. Zéro tant que le total manque.</summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Loaded / Total, 0, 1) : 0;
}
