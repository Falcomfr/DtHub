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
    private readonly IQuestSuccessSeed? _seed;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private QuestCatalogDocument? _current;

    public QuestCatalogService(
        IPapychaClient client,
        IDocumentStore<QuestCatalogDocument> store,
        IQuestSuccessSeed? seed = null)
    {
        _client = client;
        _store = store;
        _seed = seed;
    }

    /// <summary>
    /// Au-delà de ce délai, le catalogue est réindexé à la prochaine ouverture.
    /// Le site ajoute des quêtes au fil des mises à jour du jeu, pas tous les
    /// jours : une semaine suffit, et évite d'aller les déranger pour rien.
    /// </summary>
    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// En deçà de ce délai, on ne demande même pas au site s'il a bougé.
    ///
    /// Ouvrir et refermer la fenêtre dix fois de suite ne doit pas produire dix
    /// demandes. Un quart d'heure suffit à s'en garder, et la demande coûte
    /// deux cents octets : mieux vaut regarder souvent que donner à quelqu'un
    /// un bouton pour le faire à notre place.
    ///
    /// Elle était d'une heure quand un bouton de relecture existait ; il a
    /// disparu, et ce délai est ce qui le remplace.
    /// </summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromMinutes(15);

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

            if (!await IsStaleAsync(_current, cancellationToken).ConfigureAwait(false))
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
    /// Cherche les zones, les succès et les quêtes à la fois, sans jamais aller
    /// au réseau.
    /// </summary>
    public QuestSearchResults SearchAll(string? query, int limit = 50) =>
        QuestSearch.Search(
            Catalog.Quests, Catalog.Sections, Catalog.Dungeons, Catalog.Paths, query, limit);

    /// <summary>
    /// Quêtes d'une rubrique, triées par titre.
    ///
    /// Sur toutes les rubriques auxquelles la quête appartient : le site range
    /// « Le dragon d'Astrub » dans ses quêtes principales comme dans celles
    /// d'Astrub, et n'en retenir qu'une vidait les rubriques transversales.
    /// </summary>
    public IReadOnlyList<QuestSummary> InSection(int sectionId) =>
    [
        .. Catalog.Quests
            .Where(q => q.SectionIds.Contains(sectionId))
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
    /// Range les quêtes sous leurs rubriques et rend celles qui en portent au
    /// moins une.
    ///
    /// Deux sources, et il en faut deux. Les catégories du site sont précises
    /// mais incomplètes : mesuré sur les 782 quêtes, elles en laissent 150 sans
    /// rubrique, atteignables par la seule recherche. Les pages que le site
    /// tient à la main nomment en plus des ensembles qu'aucune catégorie ne
    /// porte, du Krosmoz aux Bulles Temporelles.
    ///
    /// Une quête appartient à toutes les rubriques qui la réclament, et non à
    /// une seule. Le site range « Le dragon d'Astrub » dans ses quêtes
    /// principales comme dans celles d'Astrub : une quête est un lieu et un
    /// cheminement. Forcer un choix vidait les rubriques transversales, la page
    /// des quêtes principales en énumérant soixante-treize dont douze
    /// seulement, faute de zone, y restaient.
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
        IReadOnlyList<QuestPageSection> pages,
        IReadOnlyDictionary<string, QuestSeedEntry>? seed)
    {
        var known = sections.ToDictionary(s => s.Id, s => s);
        var extra = ExtraSections(pages, sections);
        var claimed = Claims(pages, extra, sections);
        var successes = Successes(pages, seed);

        // Le tableau de la page « Quêtes » est le seul endroit où le site
        // publie son propre classement, et il le publie en clair. Le menu, qui
        // servait jusqu'ici, ne rendait rien d'exploitable et faisait retomber
        // la liste sur un classement par nombre de quêtes.
        List<string> ranking = [.. pages.Select(p => QuestSearch.Normalize(p.Name))];

        Dictionary<string, HashSet<int>> membership = new(StringComparer.Ordinal);
        Dictionary<string, string> names = new(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            var key = QuestSectionPageParser.Key(quest.Url);
            HashSet<int> mine = [];
            var category = PrimarySection(quest, known);

            if (category != 0)
            {
                mine.Add(category);
            }

            if (claimed.TryGetValue(key, out var pageSections))
            {
                mine.UnionWith(pageSections);
            }

            if (mine.Count == 0)
            {
                mine.Add(OtherSectionId);
            }

            membership[key] = mine;
            names[key] = successes.GetValueOrDefault(key, string.Empty);
        }

        Widen(membership, names);

        List<QuestSummary> arranged = new(quests.Count);

        foreach (var quest in quests)
        {
            var key = QuestSectionPageParser.Key(quest.Url);

            arranged.Add(quest with
            {
                SuccessName = names[key],
                ChainStep = seed is not null && seed.TryGetValue(key, out var entry)
                    ? entry.ChainStep
                    : 0,
                PlayOrder = seed is not null && seed.TryGetValue(key, out var place)
                    ? place.PlayOrder
                    : 0,

                // Les deux gisements réunis : le texte libre des métadonnées et
                // les quêtes que la page nomme en amont. Le premier dit « Être
                // le 3 Août », le second « L'essentiel est dans le Lac gelé » ;
                // les deux sont des prérequis, et une quête peut avoir les deux.
                Prerequisites =
                [
                    .. quest.Prerequisites.Concat(
                        seed is not null && seed.TryGetValue(key, out var before)
                            ? before.Prerequisites
                            : [])
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                ],
                SectionIds = [.. membership[key]],
            });
        }

        var counts = arranged
            .SelectMany(q => q.SectionIds)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        List<QuestSection> present =
        [
            .. sections
                .Concat(extra.Values)
                .Where(s => counts.ContainsKey(s.Id))
                .Select(s => s with { Count = counts[s.Id] }),
        ];

        var pageUrls = PageUrls(present, pages, membership);

        List<QuestSection> kept =
        [
            .. present.Select(s => s with
            {
                Url = pageUrls.GetValueOrDefault(s.Id, string.Empty),
            }),
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

        // La rubrique qui situe une quête dans une recherche est la plus petite
        // de celles qui la réclament : « Astrub » en dit plus que « Quêtes
        // principales ».
        var size = kept.ToDictionary(s => s.Id, s => s.Count);

        return (
            [
                .. arranged.Select(q => q with
                {
                    SectionId = q.SectionIds
                        .OrderBy(id => size.GetValueOrDefault(id, int.MaxValue))
                        .ThenBy(id => id)
                        .First(),
                }),
            ],
            Order(kept, ranking),
            ranking);
    }

    /// <summary>
    /// Succès dans l'ordre où les pages les présentent, chacun pris à sa
    /// première apparition.
    ///
    /// Le rang se prend sur les quêtes que l'intertitre coiffe, et non sur ce
    /// que l'intertitre écrit. Les deux endroits où le site nomme un succès ne
    /// l'écrivent pas pareil : l'intertitre dit « Brûler le pissenlit à la
    /// racine », « Fri Carré », « Etre plus royaliste que le roi », quand la
    /// quête dit « par la racine », « Fri carré », « Étre ». Ailleurs c'est une
    /// coquille franche, « Globlitération » contre « Goblitération ». Or c'est
    /// le nom de la quête qui fait foi partout ailleurs. Rapprocher les deux
    /// par leur texte perdait le rang : sur quatre-vingt-seize intitulés
    /// relevés, trente ne désignaient aucun succès du catalogue, et
    /// quarante-neuf succès sur cent quinze se retrouvaient sans rang. En
    /// suivant les adresses, il en reste dix-huit.
    ///
    /// Tous les intertitres en gras comptent, et non les seuls marqués
    /// « [Succès] » : trois succès n'ont pas d'autre intertitre que leur nom
    /// nu, et les compter n'inverse aucun des rangs que le site marque.
    /// </summary>
    private static List<string> SuccessOrder(
        IReadOnlyList<QuestPageSection> pages,
        IReadOnlyList<QuestSummary> quests)
    {
        Dictionary<string, string> named = new(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            if (quest.SuccessName.Length > 0)
            {
                named.TryAdd(QuestSectionPageParser.Key(quest.Url), quest.SuccessName);
            }
        }

        List<string> order = [];
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (var group in pages.SelectMany(p => p.Groups))
        {
            var name = group.QuestUrls
                .Select(url => named.GetValueOrDefault(url, string.Empty))
                .FirstOrDefault(n => n.Length > 0);

            // Un intertitre dont aucune quête ne porte de succès ne range rien.
            // C'est le cas d'un groupe qui ne renvoie qu'à des pages absentes du
            // catalogue.
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                order.Add(name);
            }
        }

        return order;
    }

    /// <summary>
    /// Étend chaque rubrique aux succès qu'elle a entamés.
    ///
    /// Un succès traverse parfois deux zones : « Se mettre au ver » compte
    /// quatre quêtes, trois sous Amakna et une ailleurs, si bien qu'Amakna
    /// l'annonçait avec trois. Un succès se joue d'un tenant, il se lit d'un
    /// tenant : la rubrique qui en réclame une quête les réclame toutes.
    /// </summary>
    private static void Widen(
        Dictionary<string, HashSet<int>> membership,
        Dictionary<string, string> names)
    {
        Dictionary<string, HashSet<int>> whole = new(StringComparer.Ordinal);

        foreach (var (key, success) in names)
        {
            if (success.Length == 0)
            {
                continue;
            }

            if (!whole.TryGetValue(success, out var all))
            {
                whole[success] = all = [];
            }

            all.UnionWith(membership[key]);
        }

        foreach (var (key, success) in names)
        {
            if (success.Length == 0 || !whole.TryGetValue(success, out var all))
            {
                continue;
            }

            membership[key].UnionWith(all);

            // La rubrique de recueil n'a plus lieu d'être dès qu'une vraie
            // rubrique réclame le succès.
            if (membership[key].Count > 1)
            {
                membership[key].Remove(OtherSectionId);
            }
        }
    }

    /// <summary>
    /// La page rédigée de chaque rubrique, telle que le tableau de « Quêtes »
    /// la désigne.
    ///
    /// Deux rapprochements, dans cet ordre. Le nom d'abord, débarrassé de son
    /// préfixe : le site écrit « Quêtes d'Albuera » dans son tableau et
    /// « Albuera » dans ses catégories. Il suffit pour treize rubriques sur
    /// vingt-cinq, et laisse de côté celles que le tableau nomme plus court
    /// que la catégorie : « Quêtes de Frigost » contre « Île de Frigost »,
    /// « Quêtes de Cania » contre « Bonta &amp; Cania ».
    ///
    /// Le contenu ensuite, qui ne ment pas : la rubrique qui range le plus des
    /// quêtes d'une page est celle que la page présente. La moitié au moins
    /// doit s'y retrouver, faute de quoi le rapprochement tiendrait du hasard.
    /// </summary>
    private static Dictionary<int, string> PageUrls(
        IReadOnlyList<QuestSection> sections,
        IReadOnlyList<QuestPageSection> pages,
        Dictionary<string, HashSet<int>> membership)
    {
        Dictionary<int, string> urls = [];

        var byName = sections
            .GroupBy(s => QuestSearch.Normalize(QuestZoneOrder.DisplayName(s.Name)), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        List<QuestPageSection> pending = [];

        foreach (var page in pages)
        {
            if (string.IsNullOrWhiteSpace(page.Url))
            {
                continue;
            }

            var key = QuestSearch.Normalize(QuestZoneOrder.DisplayName(page.Name));

            if (byName.TryGetValue(key, out var id))
            {
                urls.TryAdd(id, page.Url);
            }
            else
            {
                pending.Add(page);
            }
        }

        foreach (var page in pending)
        {
            Dictionary<int, int> tally = [];

            foreach (var url in page.QuestUrls)
            {
                if (!membership.TryGetValue(QuestSectionPageParser.Key(url), out var owners))
                {
                    continue;
                }

                foreach (var owner in owners)
                {
                    tally[owner] = tally.GetValueOrDefault(owner) + 1;
                }
            }

            var best = tally
                .Where(pair => !urls.ContainsKey(pair.Key))
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .FirstOrDefault();

            if (best.Value * 2 >= page.QuestUrls.Count && best.Value > 0)
            {
                urls[best.Key] = page.Url;
            }
        }

        return urls;
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
    /// ensemble à elle.
    ///
    /// On retient celle qui partage le plus de mots distinctifs : « Quêtes du
    /// Château d'Amakna » en partage deux avec « Château d'Amakna » et un seul
    /// avec « Amakna ». À égalité, celle qui en ajoute le moins : « Quêtes
    /// d'Amakna » partage un mot avec les deux, mais « Amakna » n'ajoute rien
    /// là où « Château d'Amakna » ajoute un mot. Sans ce second critère, le
    /// choix tenait au nombre de quêtes des catégories, donc au hasard.
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
            .ThenBy(p => QuestMenuParser.Surplus(key, p.Section.SearchKey))
            .ThenByDescending(p => p.Section.Count)
            .Select(p => (QuestSection?)p.Section)
            .FirstOrDefault();
    }

    /// <summary>
    /// <summary>
    /// Succès de chaque quête, de deux sources qui se complètent.
    ///
    /// Les intertitres des pages de rubrique en rattachent 380, la carte
    /// embarquée 505, leur union 505 sur 782. La carte l'emporte : elle est
    /// tirée du bloc d'intro de chaque quête, c'est-à-dire de ce que la quête
    /// dit d'elle-même, là où un intertitre est un rangement éditorial. Elle
    /// porte aussi l'orthographe officielle, les deux sources écrivant
    /// « Brûler le pissenlit à la racine » et « par la racine ».
    ///
    /// Les intertitres restent lus à chaque indexation : ils rattrapent les
    /// quêtes ajoutées depuis la dernière extraction.
    /// </summary>
    private static Dictionary<string, string> Successes(
        IReadOnlyList<QuestPageSection> pages,
        IReadOnlyDictionary<string, QuestSeedEntry>? seed)
    {
        Dictionary<string, string> successes = new(StringComparer.Ordinal);

        foreach (var group in pages.SelectMany(p => p.Groups).Where(g => g.IsSuccess))
        {
            foreach (var url in group.QuestUrls)
            {
                successes.TryAdd(url, group.Name);
            }
        }

        if (seed is not null)
        {
            foreach (var (url, entry) in seed)
            {
                successes[QuestSectionPageParser.Key(url)] = entry.Success;
            }
        }

        return successes;
    }

    /// <summary>
    /// Rubriques que les pages du site donnent à chaque quête.
    ///
    /// Toutes celles qui la nomment, et non la première : les pages se
    /// recoupent largement, dix-sept des dix-huit quêtes des Bulles Temporelles
    /// figurant aussi sur la page du Krosmoz. N'en garder qu'une laissait
    /// certaines rubriques presque vides.
    /// </summary>
    private static Dictionary<string, HashSet<int>> Claims(
        IReadOnlyList<QuestPageSection> pages,
        Dictionary<string, QuestSection> extra,
        IReadOnlyList<QuestSection> sections)
    {
        Dictionary<string, HashSet<int>> claims = new(StringComparer.Ordinal);

        foreach (var page in pages)
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
                if (url.Length == 0)
                {
                    continue;
                }

                if (!claims.TryGetValue(url, out var mine))
                {
                    claims[url] = mine = [];
                }

                mine.Add(section.Id);
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

    /// <summary>
    /// Faut-il relire le site ?
    ///
    /// On le lui demande plutôt que de compter les jours. Le site publie quand
    /// il publie, et une quête parue ce matin attendait jusqu'ici la fin d'une
    /// semaine ; elle est vue le jour même. La demande pèse quatre-vingt-dix-sept
    /// octets, contre dix-huit millions pour une relecture.
    ///
    /// Le délai reste, en filet : si le site cesse de répondre à cette
    /// demande-là, le catalogue vieillit quand même et finit par être relu.
    ///
    /// Un site qui ne répond pas ne provoque jamais de relecture : on garde ce
    /// qu'on a. Chercher dans une liste d'hier vaut mieux que ne rien pouvoir
    /// chercher.
    /// </summary>
    private async Task<bool> IsStaleAsync(
        QuestCatalogDocument document,
        CancellationToken cancellationToken)
    {
        if (document.NeedsRebuild || document.IndexedUtc is not { } indexed)
        {
            return true;
        }

        var age = DateTimeOffset.UtcNow - indexed;

        if (age > Freshness)
        {
            return true;
        }

        if (age < Patience)
        {
            return false;
        }

        var stamps = await _client.GetCategoryStampsAsync(cancellationToken).ConfigureAwait(false);

        if (stamps.Count == 0)
        {
            return false;
        }

        return Moved(document.SiteCategories, stamps);
    }

    /// <summary>
    /// Vrai si l'une des catégories qu'on lit a bougé depuis la dernière
    /// lecture.
    ///
    /// Une catégorie qu'on ne connaissait pas encore compte comme ayant bougé :
    /// c'est le cas d'un catalogue plus ancien que cette empreinte, et d'une
    /// catégorie que le site vient d'ouvrir.
    /// </summary>
    private static bool Moved(
        IReadOnlyList<CategoryStamp> seen,
        IReadOnlyList<CategoryStamp> now)
    {
        foreach (var stamp in now)
        {
            var before = seen.FirstOrDefault(s => s.Category == stamp.Category);

            if (before is null || stamp.Posts != before.Posts || stamp.Modified > before.Modified)
            {
                return true;
            }
        }

        return false;
    }

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
        var chrono = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var quests = await _client.GetQuestsAsync(progress, cancellationToken).ConfigureAwait(false);

            if (quests.Count == 0)
            {
                // Un site joignable mais qui ne rend rien : garder ce qu'on
                // avait plutôt que d'écraser le cache par du vide.
                return previous;
            }

            // Chaque étape s'annonce avant de commencer, et non après. Une
            // seule le faisait, celle des quêtes, et c'est la plus courte : le
            // compteur atteignait son total en quelques secondes puis restait
            // figé pendant tout le reste, sans que rien ne dise que le travail
            // continuait.
            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Sections));

            var sections = await _client.GetSectionsAsync(cancellationToken).ConfigureAwait(false);
            var pages = await _client.GetPageSectionsAsync(cancellationToken).ConfigureAwait(false);

            // Les empreintes sont relevées pendant la lecture, et non avant :
            // ce qu'on retient doit décrire le site tel qu'on vient de le lire.
            var stamp = await _client.GetStampAsync(cancellationToken).ConfigureAwait(false);
            var categories = await _client
                .GetCategoryStampsAsync(cancellationToken)
                .ConfigureAwait(false);
            // Quatre mégaoctets, et les quatre cinquièmes du temps d'une
            // indexation : c'est l'étape qu'il importe le plus de nommer.
            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Dungeons));

            var dungeons = await _client.GetDungeonsAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Paths));

            // Les chemins après eux : le côté d'un chemin se décide sur les noms
            // des donjons, qu'il faut donc connaître d'abord.
            var paths = await _client
                .GetPathsAsync(
                    [.. dungeons.Where(d => d.Kind == DungeonKind.Dungeon).Select(d => d.Title)],
                    cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Arranging));

            var (arranged, ordered, ranking) = Arrange(
                quests, sections, pages, _seed?.Load());

            var document = new QuestCatalogDocument
            {
                IndexedUtc = DateTimeOffset.UtcNow,
                SiteModifiedUtc = stamp?.Modified,
                SitePosts = stamp?.Posts ?? 0,
                SiteCategories = [.. categories],
                Quests = [.. arranged],
                Sections = [.. ordered],
                Dungeons = [.. dungeons],
                Paths = [.. paths],
                SectionOrder = [.. ranking],
                SuccessOrder = SuccessOrder(pages, arranged),
            };

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);

            _current = document;
            LastIndexing = chrono.Elapsed;

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

    /// <summary>
    /// Durée de la dernière indexation complète, ou <c>null</c> s'il n'y en a
    /// pas eu dans cette session.
    ///
    /// **Mesurée parce qu'elle ne l'était pas.** Les « cinquante secondes »
    /// citées dans les décisions du projet datent d'une époque où
    /// l'indexation faisait huit requêtes ; elle en fait une cinquantaine
    /// depuis. Aucun chronomètre n'existait, et toute optimisation se jugeait
    /// donc à l'impression.
    /// </summary>
    public TimeSpan? LastIndexing { get; private set; }

    /// <summary>Dernier échec d'indexation, pour que la fenêtre puisse le dire.</summary>
    public Exception? LastFailure { get; private set; }

    public void Dispose() => _gate.Dispose();
}
