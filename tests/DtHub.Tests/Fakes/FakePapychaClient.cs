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

    public Task<IReadOnlyList<QuestSection>> GetSectionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuestSection>>(_sections);

    /// <summary>Ordre annoncé par le site, vide par défaut.</summary>
    public List<string> Order { get; } = [];

    public Task<IReadOnlyList<string>> GetSectionOrderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Order);
}
