namespace DtHub.Core.Papycha;

/// <summary>
/// Read access to the site, isolated behind an interface so that the
/// core stays free of the network and the catalog can be verified
/// against recorded responses.
/// </summary>
public interface IPapychaClient
{
    /// <summary>
    /// Fetches all the quests, page after page.
    ///
    /// <paramref name="progress"/> receives the number of quests
    /// already read and the total announced by the site: indexing
    /// takes a few seconds and must be visible.
    /// </summary>
    Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the sections that organize the quests.</summary>
    Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The site's fingerprint, or <c>null</c> if it does not respond.
    /// Used to know whether it has changed without rereading it.
    /// </summary>
    Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The fingerprint of each category read, or an empty list if the
    /// site does not respond. Used to reread only what has changed.
    /// </summary>
    Task<IReadOnlyList<CategoryStamp>> GetCategoryStampsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the ranking that the site keeps by hand on its "Quêtes"
    /// (Quests) page, and the quests that each section lists.
    ///
    /// The categories are not enough: measured against the 782 quests,
    /// they leave 150 without a section. These pages call for 120 more
    /// and name sets that no category covers.
    ///
    /// Returns an empty list if the site does not respond: the catalog
    /// stays usable on its categories alone.
    /// </summary>
    Task<IReadOnlyList<QuestPageSection>> GetPageSectionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the dungeons, page content included.
    ///
    /// The content is requested along with the rest rather than page
    /// by page: the level, the location and the character are in the
    /// metadata, but the key and the soul stone only live in the body
    /// of the article. Requesting them separately would cost
    /// eighty-three requests where one is enough.
    /// </summary>
    Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the paths, and decides which side each one belongs to.
    ///
    /// The dungeon names are requested because the decision depends on
    /// them: a path goes with the dungeons when its title names one.
    /// </summary>
    Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        IReadOnlyList<string> dungeonTitles,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What the indexing is currently reading.
///
/// **These phases exist because the wait used to lie.** Only one of
/// them reported its progress, the quests, and it is the shortest one.
/// The counter would therefore climb to "782 / 782" within a few
/// seconds, then stay frozen there for four fifths of the time, the
/// dungeons alone weighing in at four megabytes.
/// </summary>
public enum QuestIndexingPhase
{
    /// <summary>Quests, the only phase that counts itself.</summary>
    Quests,

    /// <summary>Sections and their pages, about twenty reads.</summary>
    Sections,

    /// <summary>Dungeons, raids and lairs. By far the heaviest.</summary>
    Dungeons,

    /// <summary>Paths, which are inferred from the dungeons.</summary>
    Paths,

    /// <summary>Arranging, which no longer touches the network.</summary>
    Arranging,
}

/// <summary>Progress of an indexing run.</summary>
/// <param name="Loaded">Elements already read.</param>
/// <param name="Total">Total announced, or zero while unknown.</param>
/// <param name="Phase">What is being read right now.</param>
public readonly record struct QuestIndexingProgress(
    int Loaded,
    int Total,
    QuestIndexingPhase Phase = QuestIndexingPhase.Quests)
{
    /// <summary>
    /// Share completed, from zero to one. Zero while the total is
    /// missing.
    /// </summary>
    public double Fraction => Total > 0 ? Math.Clamp((double)Loaded / Total, 0, 1) : 0;
}
