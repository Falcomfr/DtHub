using DtHub.Core.Storage;

namespace DtHub.Core.Papycha;

/// <summary>
/// Tient le catalogue des quêtes : le relit du cache, le reconstruit quand il
/// le faut, et sert les recherches.
///
/// Il vit dans le noyau parce qu'il ne fait ni réseau ni disque lui-même : il
/// s'appuie sur deux interfaces, ce qui le rend vérifiable sans l'un ni
/// l'autre.
/// </summary>
public sealed class QuestCatalogService : IDisposable
{
    private readonly IPapychaClient _client;
    private readonly IDocumentStore<QuestCatalogDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private QuestCatalogDocument? _current;

    public QuestCatalogService(
        IPapychaClient client,
        IDocumentStore<QuestCatalogDocument> store)
    {
        _client = client;
        _store = store;
    }

    /// <summary>
    /// Au-delà de ce délai, le catalogue est réindexé à la prochaine ouverture.
    /// Le site ajoute des quêtes au fil des mises à jour du jeu, pas tous les
    /// jours : une semaine suffit, et évite d'aller les déranger pour rien.
    /// </summary>
    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Catalogue en mémoire, éventuellement vide.</summary>
    public QuestCatalogDocument Catalog => _current ?? new QuestCatalogDocument();

    /// <summary>
    /// Rend le catalogue, en l'indexant s'il manque ou s'il a vieilli.
    ///
    /// Une indexation qui échoue ne vide jamais ce qu'on avait : on rend le
    /// catalogue périmé et l'appelant décide s'il le signale. Chercher dans une
    /// liste d'hier vaut mieux que ne rien pouvoir chercher.
    /// </summary>
    public async Task<QuestCatalogDocument> GetAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

            if (!IsStale(_current))
            {
                return _current;
            }

            return await RebuildAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Réindexe sur demande, quelle que soit la fraîcheur.</summary>
    public async Task<QuestCatalogDocument> RefreshAsync(
        IProgress<QuestIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await RebuildAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cherche dans le catalogue en mémoire, sans jamais aller au réseau.</summary>
    public IReadOnlyList<QuestSummary> Search(string? query, int limit = 50) =>
        QuestSearch.Filter(Catalog.Quests, query, limit);

    /// <summary>
    /// Quêtes d'une rubrique, triées par titre. Les rubriques du site sont
    /// posées à plat sur chaque quête : une quête peut en porter plusieurs.
    /// </summary>
    public IReadOnlyList<QuestSummary> InSection(int sectionId) =>
    [
        .. Catalog.Quests
            .Where(q => q.Categories.Contains(sectionId))
            .OrderBy(q => q.Title, StringComparer.CurrentCulture),
    ];

    /// <summary>
    /// Recopie dans chaque quête les noms de ses rubriques, sous forme
    /// comparable.
    ///
    /// C'est ici et nulle part ailleurs : le client lit les quêtes et les
    /// rubriques par deux appels séparés, et personne d'autre ne tient les deux
    /// à la fois.
    /// </summary>
    private static IEnumerable<QuestSummary> WithSectionKeys(
        IEnumerable<QuestSummary> quests,
        IEnumerable<QuestSection> sections)
    {
        var names = sections.ToDictionary(s => s.Id, s => s.SearchKey);

        return quests.Select(q => q with
        {
            SectionKey = string.Join(
                ' ',
                q.Categories
                    .Select(c => names.GetValueOrDefault(c, string.Empty))
                    .Where(n => n.Length > 0)),
        });
    }

    private bool IsStale(QuestCatalogDocument document) =>
        document.NeedsRebuild
        || document.IndexedUtc is not { } indexed
        || DateTimeOffset.UtcNow - indexed > Freshness;

    /// <summary>À appeler sous verrou.</summary>
    private async Task<QuestCatalogDocument> RebuildAsync(
        IProgress<QuestIndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Le cache est relu s'il ne l'a pas encore été : une réindexation
        // demandée d'emblée, réseau coupé, jetterait sinon un catalogue qu'on
        // avait pourtant sur le disque.
        _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        var previous = _current;

        try
        {
            var quests = await _client.GetQuestsAsync(progress, cancellationToken).ConfigureAwait(false);

            if (quests.Count == 0)
            {
                // Un site joignable mais qui ne rend rien : garder ce qu'on
                // avait plutôt que d'écraser le cache par du vide.
                return previous;
            }

            var sections = await _client.GetSectionsAsync(cancellationToken).ConfigureAwait(false);

            var document = new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow,
                Quests = [.. WithSectionKeys(quests, sections)],
                Sections = [.. sections],
            };

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);

            _current = document;

            return document;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Réseau coupé, site en panne, réponse illisible : la recherche
            // continue sur ce qu'on avait. L'appelant lit LastFailure pour le
            // dire à l'écran.
            LastFailure = exception;
            _current = previous;

            return previous;
        }
    }

    /// <summary>Dernier échec d'indexation, pour que la fenêtre puisse le dire.</summary>
    public Exception? LastFailure { get; private set; }

    public void Dispose() => _gate.Dispose();
}
