using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Simulated site client. The test project must never touch the
/// network: everything the catalog can do is verified here.
/// </summary>
public sealed class FakePapychaClient : IPapychaClient
{
    private readonly List<QuestSummary> _quests = [];
    private readonly List<QuestSection> _sections = [];

    /// <summary>
    /// Error to throw on the next call, to exercise failures.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    /// Number of indexing requests made, to verify the cache.
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

    /// <summary>What the sentinel will read, category by category.</summary>
    public List<CategoryStamp> CategoryStamps { get; } = [];

    public Task<IReadOnlyList<CategoryStamp>> GetCategoryStampsAsync(
        CancellationToken cancellationToken = default)
    {
        StampCalls++;

        return Task.FromResult<IReadOnlyList<CategoryStamp>>(CategoryStamps);
    }

    public Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuestSection>>(_sections);


    private readonly List<QuestPageSection> _pages = [];

    /// <summary>
    /// Section maintained by hand on the site, with the quests it
    /// lists.
    /// </summary>
    public FakePapychaClient WithPage(string name, string url, params int[] questIds)
    {
        _pages.Add(new QuestPageSection
        {
            Name = name,
            Url = url,
            QuestUrls = [.. questIds.Select(Address)],
        });

        return this;
    }

    /// <summary>
    /// Achievement announced by a subheading of the last section
    /// added.
    /// </summary>
    public FakePapychaClient WithSuccess(string name, params int[] questIds) =>
        WithHeading(name, isSuccess: true, questIds);

    /// <summary>
    /// Bold subheading that the site has not marked "[Succès]". It
    /// does not attach any quest to an achievement, but it does
    /// group them.
    /// </summary>
    public FakePapychaClient WithHeading(string name, params int[] questIds) =>
        WithHeading(name, isSuccess: false, questIds);

    private FakePapychaClient WithHeading(string name, bool isSuccess, int[] questIds)
    {
        var page = _pages[^1];

        _pages[^1] = page with
        {
            QuestUrls = [.. page.QuestUrls.Union(questIds.Select(Address), StringComparer.Ordinal)],
            Groups =
            [
                .. page.Groups,
                new QuestPageGroup
                {
                    Name = name,
                    IsSuccess = isSuccess,
                    QuestUrls = [.. questIds.Select(Address)],
                },
            ],
        };

        return this;
    }

    private static string Address(int id) => $"https://exemple.invalid/quete-{id}";

    public Task<IReadOnlyList<QuestPageSection>> GetPageSectionsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuestPageSection>>(_pages);

    /// <summary>Dungeons returned by the fake client.</summary>
    public List<DungeonSummary> Dungeons { get; } = [];

    public Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DungeonSummary>>(Dungeons);

    /// <summary>Paths returned by the fake client.</summary>
    public List<PathSummary> Paths { get; } = [];

    public Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        IReadOnlyList<string> dungeonTitles,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PathSummary>>(Paths);
}
