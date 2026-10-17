namespace DtHub.Core.Papycha;

/// <summary>
/// Read access to the site's public API, isolated behind an interface so
/// that the core stays free of the network and the catalog can be verified
/// against recorded responses.
///
/// It gives what the API gives and nothing more. The hand-kept sections,
/// the achievements and the quests the API lacks come from the catalog DT
/// Hub already has, see <see cref="QuestCatalogMerge"/>.
/// </summary>
public interface IPapychaClient
{
    /// <summary>
    /// Fetches all the quests, page after page.
    ///
    /// <paramref name="progress"/> receives the number of quests
    /// already read and the total announced by the site: reading
    /// takes a few seconds and must be visible.
    /// </summary>
    Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the dungeons, raids and lairs.</summary>
    Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the paths. Their side is decided at merge time, from the
    /// dungeon names.
    /// </summary>
    Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What the API announces as its last update, or <c>null</c> if it
    /// does not respond. Used to know whether it has changed without
    /// rereading it.
    /// </summary>
    Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default);
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

    /// <summary>Dungeons, raids and lairs.</summary>
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
