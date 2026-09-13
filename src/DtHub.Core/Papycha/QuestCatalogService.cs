using DtHub.Core.Storage;

namespace DtHub.Core.Papycha;

/// <summary>
/// Holds the quest catalog: reloads it from the cache, rebuilds it when
/// needed, and serves searches.
///
/// It lives in the core because it does no network or disk work itself: it
/// relies on two interfaces, which makes it testable without either one.
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
    /// Past this delay, the catalog is reindexed the next time it is opened.
    /// The site adds quests as the game gets updated, not every day: a week is
    /// enough, and it avoids bothering it for nothing.
    /// </summary>
    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Below this delay, we do not even ask the site whether it has changed.
    ///
    /// Opening and closing the window ten times in a row must not produce ten
    /// requests. A quarter of an hour is enough to guard against that, and the
    /// request costs two hundred bytes: better to check often than to give
    /// someone a button to do it for us.
    ///
    /// It used to be one hour, back when a refresh button existed; it is gone
    /// now, and this delay is what replaces it.
    /// </summary>
    public TimeSpan Patience { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>In-memory catalog, possibly empty.</summary>
    public QuestCatalogDocument Catalog => _current ?? new QuestCatalogDocument();

    /// <summary>
    /// Returns the catalog, indexing it if it is missing or has gone stale.
    ///
    /// A failed indexing never clears what we had: it returns the stale
    /// catalog and lets the caller decide whether to report it. Searching in
    /// yesterday's list beats being unable to search at all.
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

    /// <summary>Reindexes on demand, regardless of freshness.</summary>
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

    /// <summary>
    /// Searches the in-memory catalog, never touching the network.
    /// </summary>
    public IReadOnlyList<QuestSummary> Search(string? query, int limit = 50) =>
        QuestSearch.Filter(Catalog.Quests, query, limit);

    /// <summary>
    /// Searches zones, successes, and quests all at once, never touching the
    /// network.
    /// </summary>
    public QuestSearchResults SearchAll(string? query, int limit = 50) =>
        QuestSearch.Search(
            Catalog.Quests, Catalog.Sections, Catalog.Dungeons, Catalog.Paths, query, limit);

    /// <summary>
    /// Quests in a section, sorted by title.
    ///
    /// Across every section the quest belongs to: the site files "Le dragon
    /// d'Astrub" under both its main quests and its Astrub quests, and
    /// keeping only one would empty out the cross-cutting sections.
    /// </summary>
    public IReadOnlyList<QuestSummary> InSection(int sectionId) =>
    [
        .. Catalog.Quests
            .Where(q => q.SectionIds.Contains(sectionId))
            .OrderBy(q => q.Title, StringComparer.CurrentCulture),
    ];

    /// <summary>
    /// Catch-all section for quests no other section claims.
    /// </summary>
    public const int OtherSectionId = -1;

    /// <summary>
    /// The site's root category. It carries all 782 quests and therefore sorts
    /// nothing: keeping it as a section would be the same as having none.
    /// </summary>
    private const int RootCategory = 7;

    /// <summary>
    /// Sorts quests under their sections and returns those that carry at least
    /// one.
    ///
    /// Two sources, and both are needed. The site's categories are precise but
    /// incomplete: measured across the 782 quests, they leave 150 without a
    /// section, reachable only through search. The pages the site curates by
    /// hand additionally name groupings that no category carries, from the
    /// Krosmoz to the Bulles Temporelles.
    ///
    /// A quest belongs to every section that claims it, not just one. The site
    /// files "Le dragon d'Astrub" under both its main quests and its Astrub
    /// quests: a quest is both a place and a storyline. Forcing a single
    /// choice would empty out the cross-cutting sections; the main quests page
    /// lists seventy-three, of which only twelve remained once a zone was
    /// required.
    ///
    /// A page that names the same place as a category does not become an extra
    /// section: its quests join the category instead. Otherwise the list would
    /// offer "Île de Frigost" and "Quêtes de Frigost" side by side for the
    /// same place.
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

        // The table on the "Quêtes" page is the only place where the site
        // publishes its own ranking, and it publishes it in the open. The
        // menu, which served this purpose until now, yielded nothing usable
        // and made the list fall back to a ranking by quest count.
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

                // Both sources combined: the free text in the metadata, and
                // the quests the page names beforehand. The first says "Être
                // le 3 Août", the second "L'essentiel est dans le Lac gelé
                //"; both are prerequisites, and a quest can have both.
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

        // The section that places a quest in a search is the smallest of those
        // that claim it: "Astrub" says more than "Quêtes principales".
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
    /// Successes in the order the pages present them, each taken at its first
    /// appearance.
    ///
    /// The rank is taken from the quests the subheading covers, not from what
    /// the subheading writes. The two places where the site names a success do
    /// not spell it the same way: the subheading says "Brûler le pissenlit à
    /// la racine", "Fri Carré", "Etre plus royaliste que le roi", while
    /// the quest says "par la racine", "Fri carré", "Étre". Elsewhere it
    /// is a plain typo, "Globlitération" versus "Goblitération". And it is
    /// the quest's name that is authoritative everywhere else. Matching the
    /// two by their text lost the rank: across ninety-six headings recorded,
    /// thirty named no success in the catalog at all, and forty-nine successes
    /// out of one hundred fifteen ended up without a rank. Following the URLs
    /// instead, only eighteen remain.
    ///
    /// Every bold subheading counts, not only those marked "[Succès]": three
    /// successes have no subheading other than their bare name, and counting
    /// them does not reverse any of the ranks the site marks.
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

            // A subheading whose quests carry no success sorts nothing. That
            // is the case for a group that only points to pages absent from
            // the catalog.
            if (!string.IsNullOrEmpty(name) && seen.Add(name))
            {
                order.Add(name);
            }
        }

        return order;
    }

    /// <summary>
    /// Widens each section to the successes it has started.
    ///
    /// A success sometimes spans two zones: "Se mettre au ver" counts four
    /// quests, three under Amakna and one elsewhere, so Amakna announced it
    /// with three. A success is played as one piece, and it is read as one
    /// piece: the section that claims one of its quests claims all of them.
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

            // The catch-all section no longer has a reason to exist once a
            // real section claims the success.
            if (membership[key].Count > 1)
            {
                membership[key].Remove(OtherSectionId);
            }
        }
    }

    /// <summary>
    /// The written page for each section, as the "Quêtes" table designates
    /// it.
    ///
    /// Two matching passes, in this order. The name first, stripped of its
    /// prefix: the site writes "Quêtes d'Albuera" in its table and "Albuera
    ///" in its categories. That is enough for thirteen sections out of
    /// twenty-five, and it leaves aside the ones the table names shorter than
    /// the category: "Quêtes de Frigost" versus "Île de Frigost", "Quêtes
    /// de Cania" versus "Bonta &amp; Cania".
    ///
    /// The content next, which does not lie: the section that files the most
    /// of a page's quests is the one the page presents. At least half must be
    /// found there, or the match would come down to chance.
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
    /// Sections that the site's pages contribute on their own, meaning ones no
    /// category already designates. Their identifier is negative: it does not
    /// come from the site and must never collide with a category's.
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
    /// The category this page designates, or <c>null</c> if it names a
    /// grouping of its own.
    ///
    /// We keep the one that shares the most distinctive words: "Quêtes du
    /// Château d'Amakna" shares two with "Château d'Amakna" and only one
    /// with "Amakna". In case of a tie, the one that adds the fewest: "
    /// Quêtes d'Amakna" shares one word with both, but "Amakna" adds
    /// nothing where "Château d'Amakna" adds one word. Without this second
    /// criterion, the choice came down to the categories' quest counts, hence
    /// to chance.
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
    /// The success for each quest, from two sources that complement each
    /// other.
    ///
    /// The section subheadings link 380 of them, the embedded map 505, their
    /// union 505 out of 782. The map wins: it is drawn from each quest's intro
    /// block, that is, from what the quest says about itself, whereas a
    /// subheading is an editorial arrangement. It also carries the official
    /// spelling, the two sources writing "Brûler le pissenlit à la racine"
    /// and "par la racine".
    ///
    /// The subheadings are still read on every indexing pass: they catch the
    /// quests added since the last extraction.
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
    /// Sections that the site's pages give to each quest.
    ///
    /// Every section that names it, not just the first: the pages overlap
    /// heavily, with seventeen of the eighteen Bulles Temporelles quests also
    /// appearing on the Krosmoz page. Keeping only one left some sections
    /// nearly empty.
    /// </summary>
    private static Dictionary<string, HashSet<int>> Claims(
        IReadOnlyList<QuestPageSection> pages,
        Dictionary<string, QuestSection> extra,
        IReadOnlyList<QuestSection> sections)
    {
        Dictionary<string, HashSet<int>> claims = new(StringComparer.Ordinal);

        foreach (var page in pages)
        {
            // A page that designates a category hands it its quests rather
            // than opening a twin section: the "Quêtes de Cania" page brings
            // twelve that the "Bonta & Cania" category ignores.
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
    /// Sorts sections in the site's order, with the ones it does not name
    /// coming after, from the most to the least populated.
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
    /// The least populated section among the quest's own: it is the most
    /// precise, so it is the one that places it. "Astrub" rather than "
    /// Quêtes".
    ///
    /// Zero when the quest carries none, the site's root not counting: on its
    /// own, it sorts nothing.
    /// </summary>
    private static int PrimarySection(QuestSummary quest, Dictionary<int, QuestSection> known) =>
        quest.Categories
            .Where(c => c != RootCategory && known.ContainsKey(c))
            .OrderBy(c => known[c].Count)
            .Select(c => (int?)c)
            .FirstOrDefault() ?? 0;

    /// <summary>
    /// Should the site be reread?
    ///
    /// We ask it directly rather than counting days. The site publishes when
    /// it publishes, and a quest posted this morning used to wait until the
    /// end of a week; now it is seen the same day. The request weighs
    /// ninety-seven bytes, against eighteen million for a full reread.
    ///
    /// The delay stays, as a safety net: if the site stops answering this
    /// particular request, the catalog still ages and eventually gets reread.
    ///
    /// A site that does not answer never triggers a reread: we keep what we
    /// have. Searching in yesterday's list beats being unable to search at
    /// all.
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
    /// True if one of the categories being read has changed since the last
    /// read.
    ///
    /// A category we did not know about yet counts as having changed: that
    /// covers a catalog older than this fingerprint, and a category the site
    /// has just opened.
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

    /// <summary>Call while holding the lock.</summary>
    private async Task<QuestCatalogDocument> RebuildAsync(
        IProgress<QuestIndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        // The cache is reloaded if it has not been yet: a reindex requested
        // right away, with the network down, would otherwise discard a catalog
        // we actually had on disk.
        _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        var previous = _current;
        var chrono = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var quests = await _client.GetQuestsAsync(progress, cancellationToken).ConfigureAwait(false);

            if (quests.Count == 0)
            {
                // A site that is reachable but returns nothing: keep what we
                // had rather than overwrite the cache with emptiness.
                return previous;
            }

            // Each phase announces itself before it starts, not after. Only
            // one used to do it, the quests phase, and it is the shortest: the
            // counter would reach its total within a few seconds and then stay
            // frozen for the rest of the work, with nothing to say it was
            // still going.
            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Sections));

            var sections = await _client.GetSectionsAsync(cancellationToken).ConfigureAwait(false);
            var pages = await _client.GetPageSectionsAsync(cancellationToken).ConfigureAwait(false);

            // The fingerprints are captured during the read, not before: what
            // we keep must describe the site as it was just read.
            var stamp = await _client.GetStampAsync(cancellationToken).ConfigureAwait(false);
            var categories = await _client
                .GetCategoryStampsAsync(cancellationToken)
                .ConfigureAwait(false);
            // Four megabytes, and four fifths of an indexing pass's time: this
            // is the phase most worth announcing.
            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Dungeons));

            var dungeons = await _client.GetDungeonsAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Paths));

            // Paths come after them: which side a path belongs to is decided
            // from dungeon names, which therefore need to be known first.
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
            // Network down, site broken, unreadable response: searching
            // continues on what we had. The caller reads LastFailure to
            // display it on screen.
            LastFailure = exception;
            _current = previous;

            return previous;
        }
    }

    /// <summary>
    /// Duration of the last full indexing pass, or <c>null</c> if none has
    /// happened in this session.
    ///
    /// **Measured because it was not.** The "fifty seconds" cited in the
    /// project's decisions date from a time when indexing made eight requests;
    /// it has made about fifty since. No stopwatch existed, and every
    /// optimization used to be judged by feel.
    /// </summary>
    public TimeSpan? LastIndexing { get; private set; }

    /// <summary>Last indexing failure, so the window can report it.</summary>
    public Exception? LastFailure { get; private set; }

    public void Dispose() => _gate.Dispose();
}
