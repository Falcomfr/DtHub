using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Client de site simulé. Le projet de tests ne doit jamais toucher au réseau :
/// tout ce que le catalogue sait faire se vérifie ici.
/// </summary>
public sealed class FakePapychaClient : IPapychaClient
{
    private readonly List<QuestSummary> _quests = [];
    private readonly List<QuestSection> _sections = [];

    /// <summary>Erreur à lever au prochain appel, pour éprouver les pannes.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Nombre d'indexations demandées, pour vérifier le cache.</summary>
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

    /// <summary>Donne un niveau à la dernière quête ajoutée.</summary>
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

    /// <summary>Ce que la sentinelle lira. Nul par défaut : le site se tait.</summary>
    public SiteStamp? Stamp { get; set; }

    /// <summary>Combien de fois la sentinelle a interrogé le site.</summary>
    public int StampCalls { get; private set; }

    public Task<SiteStamp?> GetStampAsync(CancellationToken cancellationToken = default)
    {
        StampCalls++;

        return Task.FromResult(Stamp);
    }

    public Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuestSection>>(_sections);


    private readonly List<QuestPageSection> _pages = [];

    /// <summary>Rubrique tenue à la main sur le site, avec les quêtes qu'elle énumère.</summary>
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

    /// <summary>Succès annoncé par un intertitre de la dernière rubrique ajoutée.</summary>
    public FakePapychaClient WithSuccess(string name, params int[] questIds)
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
                    IsSuccess = true,
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

    /// <summary>Donjons rendus par le faux client.</summary>
    public List<DungeonSummary> Dungeons { get; } = [];

    public Task<IReadOnlyList<DungeonSummary>> GetDungeonsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DungeonSummary>>(Dungeons);

    /// <summary>Chemins rendus par le faux client.</summary>
    public List<PathSummary> Paths { get; } = [];

    public Task<IReadOnlyList<PathSummary>> GetPathsAsync(
        IReadOnlyList<string> dungeonTitles,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PathSummary>>(Paths);
}
