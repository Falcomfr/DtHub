using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated site client. The test project must never touch the
/// network: everything the catalog can do is verified here.
///
/// It also stands for the catalog shipped with the application: the
/// sections it is given are the ones DT Hub already knows, which the
/// API's quests are filed under.
/// </summary>
public sealed class FakePapychaClient : IPapychaClient, IQuestCatalogSnapshot
{
    private readonly List<QuestSummary> _quests = [];
    private readonly List<QuestSection> _sections = [];

    /// <summary>
    /// Error to throw on the next call, to exercise failures.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Number of reads of the quest list, to verify the cache.
    /// </summary>
    public int Calls { get; private set; }

    public FakePapychaClient WithQuest(int id, string title, params int[] categories)
    {
        _quests.Add(new QuestSummary
        {
            Id = id,
            Title = title,
            Url = $"https://exemple.invalid/quete-{id}/",
            Categories = categories,
            SearchKey = QuestSearch.Normalize(title),
        });

        return this;
    }

    /// <summary>Gives a level to the last quest added.</summary>
    public FakePapychaClient AtLevel(int level)
    {
        _quests[^1] = _quests[^1] with { Level = level };

        return this;
    }

    /// <summary>A section of the catalog DT Hub already knows.</summary>
    public FakePapychaClient WithSection(int id, string name, int count = 1)
    {
        _sections.Add(new QuestSection
        {
            Id = id,
            Name = name,
            Count = count,
            SearchKey = QuestSearch.Normalize(name),
        });

        return this;
    }

    public Task<IReadOnlyList<QuestSummary>> GetQuestsAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;

        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<QuestSummary>>(Failure);
        }

        progress?.Report(new QuestIndexingProgress(_quests.Count, _quests.Count));

        return Task.FromResult<IReadOnlyList<QuestSummary>>(_quests);
    }

    /// <summary>
    /// What the sentinel will read. Null by default: the site stays
    /// silent.
    /// </summary>
    public SiteStamp? Stamp { get; set; }

    /// <summary>How many times the sentinel has queried the site.</summary>
    public int StampCalls { get; private set; }

    public Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default)
    {
        StampCalls++;

        return Task.FromResult(Stamp);
    }

    /// <summary>Dungeons returned by the fake client.</summary>
    public List<DungeonSummary> Dungeons { get; } = [];

    public Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DungeonSummary>>(Dungeons);

    /// <summary>Paths returned by the fake client.</summary>
    public List<PathSummary> Paths { get; } = [];

    public Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PathSummary>>(Paths);

    /// <summary>Quests the shipped catalog already holds.</summary>
    public List<QuestSummary> KnownQuests { get; } = [];

    /// <summary>How many times the shipped catalog was read.</summary>
    public int SnapshotLoads { get; private set; }

    public QuestCatalogDocument Load()
    {
        SnapshotLoads++;

        return new QuestCatalogDocument
        {
            Quests = [.. KnownQuests],
            Sections = [.. _sections],
        };
    }
}
