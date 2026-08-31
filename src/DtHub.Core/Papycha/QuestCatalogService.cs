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
    /// Quêtes d'une rubrique, triées par titre.
    ///
    /// Sur la rubrique retenue pour la quête, et non sur toutes celles qu'elle
    /// porte : une quête d'Astrub porte aussi « Amakna », et la lister sous les
    /// deux la ferait compter deux fois et apparaître deux fois.
    /// </summary>
    public IReadOnlyList<QuestSummary> InSection(int sectionId) =>
    [
        .. Catalog.Quests
            .Where(q => q.SectionId == sectionId)
            .OrderBy(q => q.Title, StringComparer.CurrentCulture),
    ];

    /// <summary>Rubrique de recueil des quêtes qu'aucune autre ne réclame.</summary>
    public const int OtherSectionId = -1;

    /// <summary>
    /// La catégorie racine du site. Elle porte les 782 quêtes et ne range donc
    /// rien : la retenir comme rubrique reviendrait à n'en avoir aucune.
    /// </summary>
    private const int RootCategory = 7;

    /// <summary>
    /// Range chaque quête sous une rubrique et une seule, et rend la liste des
    /// rubriques qui en portent au moins une.
    ///
    /// Deux sources, et il en faut deux. Les catégories du site sont précises
    /// mais incomplètes : mesuré sur les 782 quêtes, elles en laissent 150 sans
    /// rubrique, atteignables par la seule recherche. Les pages que le site
    /// tient à la main en réclament 120 de plus et nomment des ensembles
    /// qu'aucune catégorie ne porte.
    ///
    /// Une page qui désigne le même endroit qu'une catégorie ne devient pas une
    /// rubrique de plus : ses quêtes rejoignent la catégorie. Sans quoi la
    /// liste offrirait « Île de Frigost » et « Quêtes de Frigost » côte à côte,
    /// pour un même endroit.
    /// </summary>
    private static (
        IReadOnlyList<QuestSummary> Quests,
        IReadOnlyList<QuestSection> Sections,
        IReadOnlyList<string> Order) Arrange(
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<QuestSection> sections,
        IReadOnlyList<QuestPageSection> pages)
    {
        var known = sections.ToDictionary(s => s.Id, s => s);
        var extra = ExtraSections(pages, sections);
        var claimed = Claims(pages, extra, sections);
        var successes = Successes(pages);

        // Le tableau de la page « Quêtes » est le seul endroit où le site
        // publie son propre classement, et il le publie en clair. Le menu, qui
        // servait jusqu'ici, ne rendait rien d'exploitable et faisait retomber
        // la liste sur un classement par nombre de quêtes.
        List<string> ranking = [.. pages.Select(p => QuestSearch.Normalize(p.Name))];

        List<QuestSummary> arranged = new(quests.Count);

        foreach (var quest in quests)
        {
            var key = QuestSectionPageParser.Key(quest.Url);
            var section = PrimarySection(quest, known);

            if (section == 0)
            {
                claimed.TryGetValue(key, out section);
            }

            arranged.Add(quest with
            {
                SuccessName = successes.GetValueOrDefault(key, string.Empty),
                SectionKey = string.Join(
                    ' ',
                    quest.Categories
                        .Select(c => known.TryGetValue(c, out var s) ? s.SearchKey : string.Empty)
                        .Where(n => n.Length > 0)),
                SectionId = section == 0 ? OtherSectionId : section,
            });
        }

        var counts = arranged
            .GroupBy(q => q.SectionId)
            .ToDictionary(g => g.Key, g => g.Count());

        List<QuestSection> kept =
        [
            .. sections
                .Concat(extra.Values)
                .Where(s => counts.ContainsKey(s.Id))
                .Select(s => s with { Count = counts[s.Id] }),
        ];

        if (counts.TryGetValue(OtherSectionId, out var others))
        {
            kept.Add(new QuestSection
            {
                Id = OtherSectionId,
                Name = "Autres quêtes",
                Count = others,
                SearchKey = QuestSearch.Normalize("Autres quêtes"),
            });
        }

        return (arranged, Order(kept, ranking), ranking);
    }

    /// <summary>
    /// Rubriques que les pages du site apportent en propre, c'est-à-dire celles
    /// qu'aucune catégorie ne désigne déjà. Leur identifiant est négatif : il ne
    /// vient pas du site et ne doit jamais croiser celui d'une catégorie.
    /// </summary>
    private static Dictionary<string, QuestSection> ExtraSections(
        IReadOnlyList<QuestPageSection> pages,
        IReadOnlyList<QuestSection> sections)
    {
        Dictionary<string, QuestSection> extra = new(StringComparer.Ordinal);
        var next = OtherSectionId - 1;

        foreach (var page in pages)
        {
            if (MatchingCategory(page, sections) is not null)
            {
                continue;
            }

            extra[page.Url] = new QuestSection
            {
                Id = next--,
                Name = page.Name,
                SearchKey = QuestSearch.Normalize(page.Name),
            };
        }

        return extra;
    }

    /// <summary>
    /// Catégorie que cette page désigne, ou <c>null</c> si elle nomme un
    /// ensemble à elle. On retient celle qui partage le plus de mots
    /// distinctifs : « Quêtes du Château d'Amakna » en partage deux avec
    /// « Château d'Amakna » et un seul avec « Amakna ».
    /// </summary>
    private static QuestSection? MatchingCategory(
        QuestPageSection page,
        IReadOnlyList<QuestSection> sections)
    {
        var key = QuestSearch.Normalize(page.Name);

        return sections
            .Where(s => s.Id != RootCategory)
            .Select(s => (Section: s, Score: QuestMenuParser.Kinship(key, s.SearchKey)))
            .Where(p => p.Score > 0)
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Section.Count)
            .Select(p => (QuestSection?)p.Section)
            .FirstOrDefault();
    }

    /// <summary>
    /// Rubrique à donner à chaque quête que les pages énumèrent.
    ///
    /// Les pages se recoupent largement : dix-sept des dix-huit quêtes des
    /// Bulles Temporelles figurent aussi sur la page du Krosmoz, qui les
    /// englobe. La plus petite l'emporte donc, comme pour les catégories : elle
    /// est la plus précise, donc celle qui situe. Prendre la première dans
    /// l'ordre du site laissait « Quêtes des Bulles Temporelles » avec une
    /// seule quête.
    /// </summary>
    /// <summary>
    /// Succès de chaque quête, lu sur les intertitres des pages de rubrique.
    ///
    /// Le premier qui la nomme l'emporte : une quête n'appartient qu'à un
    /// succès, et les pages ne se contredisent pas sur ce point.
    /// </summary>
    private static Dictionary<string, string> Successes(IReadOnlyList<QuestPageSection> pages)
    {
        Dictionary<string, string> successes = new(StringComparer.Ordinal);

        foreach (var group in pages.SelectMany(p => p.Groups).Where(g => g.IsSuccess))
        {
            foreach (var url in group.QuestUrls)
            {
                successes.TryAdd(url, group.Name);
            }
        }

        return successes;
    }

    private static Dictionary<string, int> Claims(
        IReadOnlyList<QuestPageSection> pages,
        Dictionary<string, QuestSection> extra,
        IReadOnlyList<QuestSection> sections)
    {
        Dictionary<string, int> claims = new(StringComparer.Ordinal);

        foreach (var page in pages.OrderBy(p => p.QuestUrls.Count))
        {
            // Une page qui désigne une catégorie lui remet ses quêtes plutôt
            // que d'ouvrir une rubrique jumelle : la page « Quêtes de Cania »
            // en apporte douze que la catégorie « Bonta & Cania » ignore.
            var section = extra.TryGetValue(page.Url, out var own)
                ? own
                : MatchingCategory(page, sections);

            if (section is null)
            {
                continue;
            }

            foreach (var url in page.QuestUrls)
            {
                if (url.Length > 0)
                {
                    claims.TryAdd(url, section.Id);
                }
            }
        }

        return claims;
    }

    /// <summary>
    /// Range les rubriques dans l'ordre du site, celles qu'il ne nomme pas
    /// venant ensuite, de la plus fournie à la moins fournie.
    /// </summary>
    private static IReadOnlyList<QuestSection> Order(
        IReadOnlyList<QuestSection> sections,
        IReadOnlyList<string> order) =>
    [
        .. sections
            .OrderBy(s => QuestMenuParser.RankOf(order, s.SearchKey))
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture),
    ];

    /// <summary>
    /// La rubrique la moins fournie parmi celles de la quête : c'est la plus
    /// précise, donc celle qui situe. « Astrub » plutôt que « Quêtes ».
    ///
    /// Zéro quand la quête n'en porte aucune, la racine du site ne comptant
    /// pas : à elle seule, elle ne range rien.
    /// </summary>
    private static int PrimarySection(QuestSummary quest, Dictionary<int, QuestSection> known) =>
        quest.Categories
            .Where(c => c != RootCategory && known.ContainsKey(c))
            .OrderBy(c => known[c].Count)
            .Select(c => (int?)c)
            .FirstOrDefault() ?? 0;

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
            var pages = await _client.GetPageSectionsAsync(cancellationToken).ConfigureAwait(false);

            var (arranged, ordered, ranking) = Arrange(quests, sections, pages);

            var document = new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow,
                Quests = [.. arranged],
                Sections = [.. ordered],
                SectionOrder = [.. ranking],
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
