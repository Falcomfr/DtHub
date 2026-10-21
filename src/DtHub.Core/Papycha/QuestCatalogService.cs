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
    private readonly IQuestCatalogSnapshot? _snapshot;
    private readonly TouchNameService? _touch;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private QuestCatalogDocument? _current;
    private QuestCatalogDocument? _view;

    public QuestCatalogService(
        IPapychaClient client,
        IDocumentStore<QuestCatalogDocument> store,
        IQuestCatalogSnapshot? snapshot = null,
        TouchNameService? touch = null)
    {
        _touch = touch;
        _client = client;
        _store = store;
        _snapshot = snapshot;
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

    /// <summary>
    /// The catalog Papycha wrote, in French: the one merged and saved, and the
    /// one paths are matched against.
    /// </summary>
    public QuestCatalogDocument Source => _current ?? new QuestCatalogDocument();

    /// <summary>
    /// What the window shows: the catalog in the game's names when they are
    /// cached for the interface language, otherwise <see cref="Source"/>.
    /// </summary>
    public QuestCatalogDocument Catalog => _view ?? Source;

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
            var baseline = await BaselineAsync(cancellationToken).ConfigureAwait(false);

            if (!await IsStaleAsync(baseline, cancellationToken).ConfigureAwait(false))
            {
                await RebuildViewAsync(cancellationToken).ConfigureAwait(false);

                return Catalog;
            }

            await RebuildAsync(progress, cancellationToken).ConfigureAwait(false);
            await RebuildViewAsync(cancellationToken).ConfigureAwait(false);

            return Catalog;
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
            await RebuildAsync(progress, cancellationToken).ConfigureAwait(false);
            await RebuildViewAsync(cancellationToken).ConfigureAwait(false);

            return Catalog;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the game's names for the interface language (no request while
    /// they are fresh) and rebuilds the view. Never throws except on
    /// cancellation: the failure stays in <see cref="NamesFailure"/>.
    /// </summary>
    public async Task RefreshNamesAsync(CancellationToken cancellationToken = default)
    {
        if (_touch is null)
        {
            return;
        }

        await _touch.RefreshAsync(TouchNameService.CurrentLanguage, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await BaselineAsync(cancellationToken).ConfigureAwait(false);
            await RebuildViewAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Last failure to read the game's names, so the window can report it.</summary>
    public Exception? NamesFailure => _touch?.LastFailure;

    /// <summary>
    /// Call while holding the lock. Without cached names, or if localizing
    /// fails, the view is the source: French beats an error.
    /// </summary>
    private async Task RebuildViewAsync(CancellationToken cancellationToken)
    {
        _view = null;

        if (_touch is null || _current is null)
        {
            return;
        }

        try
        {
            var language = TouchNameService.CurrentLanguage;

            if (await _touch.CachedAsync(language, cancellationToken).ConfigureAwait(false) is { } names)
            {
                _view = TouchLocalization.Apply(_current, _touch.Table, names, language != "fr");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A table or cache that cannot be read costs the names, not the
            // catalog: the view falls back to Papycha's French.
            _view = null;
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
    public const int OtherSectionId = QuestCatalogMerge.OtherSectionId;

    /// <summary>
    /// Should the site be read again?
    ///
    /// We ask it directly rather than counting days: the API announces the
    /// date of its last update, for the price of one small request. A quest
    /// posted this morning is seen the same day.
    ///
    /// The delay stays, as a safety net: if the API stops answering this
    /// particular request, the catalog still ages and eventually gets read
    /// again.
    ///
    /// An API that does not answer never triggers a read: we keep what we
    /// have. Searching in yesterday's list beats being unable to search at
    /// all.
    /// </summary>
    private async Task<bool> IsStaleAsync(
        QuestCatalogDocument document,
        CancellationToken cancellationToken)
    {
        if (document.IndexedUtc is not { } indexed)
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

        var stamp = await _client.GetStampAsync(cancellationToken).ConfigureAwait(false);

        return stamp is not null && stamp.Modified != document.SiteModifiedUtc;
    }

    /// <summary>
    /// The catalog the API is merged over: the cache when it is usable,
    /// otherwise the one shipped with the application.
    ///
    /// The cache is reloaded if it has not been yet: a read requested right
    /// away, with the network down, would otherwise discard a catalog we
    /// actually had on disk.
    /// </summary>
    private async Task<QuestCatalogDocument> BaselineAsync(CancellationToken cancellationToken)
    {
        _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (_current.NeedsRebuild)
        {
            _current = _snapshot?.Load() ?? new QuestCatalogDocument();
        }

        return _current;
    }

    /// <summary>Call while holding the lock.</summary>
    private async Task<QuestCatalogDocument> RebuildAsync(
        IProgress<QuestIndexingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var previous = await BaselineAsync(cancellationToken).ConfigureAwait(false);
        var chrono = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Taken before the read, not after: a quest edited during the
            // read would otherwise be stamped as seen, and every later check
            // would find the stamp unchanged for the seven days of freshness.
            // Older than the data, it makes the next check read again.
            var stamp = await _client.GetStampAsync(cancellationToken).ConfigureAwait(false);

            var quests = await _client.GetQuestsAsync(progress, cancellationToken).ConfigureAwait(false);

            if (quests.Count == 0)
            {
                // An API that is reachable but returns nothing: keep what we
                // had rather than take it for a site without quests.
                return previous;
            }

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Dungeons));

            var dungeons = await _client.GetDungeonsAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Paths));

            var paths = await _client.GetPathsAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new QuestIndexingProgress(0, 0, QuestIndexingPhase.Arranging));

            var document = QuestCatalogMerge.Apply(
                previous, quests, dungeons, paths, stamp?.Modified, DateTimeOffset.UtcNow);

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);

            _current = document;
            LastIndexing = chrono.Elapsed;
            LastFailure = null;

            return document;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Network down, API closed, unreadable response: searching
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
