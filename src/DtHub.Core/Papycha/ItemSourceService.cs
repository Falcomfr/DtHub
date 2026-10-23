using System.Text.Json;

using DtHub.Core.Storage;

namespace DtHub.Core.Papycha;

/// <summary>
/// Where an item asked by a quest comes from: the monsters that drop it
/// (archimonsters with their normal version), the harvest and the recipe,
/// read from the game by ids on demand and kept per language.
/// </summary>
public sealed class ItemSourceService(IDofusTouchClient client, IDocumentStore<ItemSourcesDocument> store) : IDisposable
{
    /// <summary>One read or write at a time, so two hovers on one item ask once.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Items the game does not know, by language: asked once per session.</summary>
    private readonly HashSet<(string Language, int ItemId)> _unknown = [];

    private ItemSourcesDocument? _document;

    /// <summary>True when <see cref="_document"/> stands for a file the store could not read.</summary>
    private bool _documentUnread;

    /// <summary>When the store was last read.</summary>
    private DateTimeOffset _loadedUtc;

    /// <summary>How often a cache file the store could not read is tried again, as in the settings: not on every hover.</summary>
    public TimeSpan UnreadRetryDelay { get; init; } = TimeSpan.FromSeconds(10);

    private DateTimeOffset? _failedUtc;

    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(30);

    public Exception? LastFailure { get; private set; }

    /// <summary>After a failed read, none is tried before this: a refusing server is not asked at every hover.</summary>
    public TimeSpan FailureCooldown { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The shared HTTP client waits ten minutes; a stalled read must not hold the gate that long.</summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Two requests to the game server are at least this far apart: a guide asks for several items at once.</summary>
    public TimeSpan RequestSpacing { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The wait between two requests; tests record it instead of sleeping.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>When the last request to the game server ended; guarded by the gate.</summary>
    private DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;

    /// <summary>A guide reads this many items at most: each one costs up to five requests.</summary>
    public const int MaxItemsPerQuest = 5;

    /// <summary>The items a quest asks to bring (3) or show (2), whose id is the second parameter, at most <see cref="MaxItemsPerQuest"/>.</summary>
    public static int[] ItemsAsked(TouchQuestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        return [.. facts.Objectives.Where(o => o.TypeId is 2 or 3 && o.Parameters.Length > 1)
            .Select(o => o.Parameters[1]).Distinct().Take(MaxItemsPerQuest)];
    }

    /// <summary>
    /// Never throws except on cancellation before the first request; once one is sent the read ends and is kept. On
    /// failure the cached source or null (see <see cref="LastFailure"/>).
    /// </summary>
    public async Task<ItemSource?> SourceAsync(int itemId, string language, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // A file not read (held by another program) gave a blank document: tried again now and then, and while it
            // stays unreadable the document in memory, with what this session read, is kept over the blank one.
            if (_document is null || (_documentUnread && DateTimeOffset.UtcNow - _loadedUtc >= UnreadRetryDelay))
            {
                var loaded = await store.LoadAsync(ct).ConfigureAwait(false);
                _documentUnread = store.IsUnread;
                _loadedUtc = DateTimeOffset.UtcNow;

                if (_document is null || !_documentUnread)
                {
                    _document = loaded;
                }
            }

            if (!_document.Languages.TryGetValue(language, out var known))
            {
                _document.Languages[language] = known = new ItemSourcesLanguage();
            }

            var cached = known.Items.GetValueOrDefault(itemId);

            if ((cached is not null && DateTimeOffset.UtcNow - cached.ReadUtc < Freshness)
                || _unknown.Contains((language, itemId))
                || (_failedUtc is { } failed && DateTimeOffset.UtcNow - failed < FailureCooldown))
            {
                return cached?.Source;
            }

            ct.ThrowIfCancellationRequested();

            // Not linked to the caller: a hover that goes must not throw away requests already sent, the read ends and is kept.
            using var timeout = new CancellationTokenSource(ReadTimeout);

            try
            {
                if (DateTimeOffset.UtcNow - known.HarvestReadUtc >= Freshness)
                {
                    // Kept at once: a later failure must not throw away the one whole-class read.
                    _document.Languages[language] = known = known with
                    {
                        Harvest = await ReadHarvestAsync(language, timeout.Token).ConfigureAwait(false),
                        HarvestReadUtc = DateTimeOffset.UtcNow,
                    };
                    await SaveAsync().ConfigureAwait(false);
                }

                var source = await ReadAsync(itemId, known.Harvest, language, timeout.Token).ConfigureAwait(false);
                _failedUtc = null;
                LastFailure = null;

                if (source is null)
                {
                    // Caching an unknown item for a month would hide a real one the server failed to send.
                    _unknown.Add((language, itemId));

                    return cached?.Source;
                }

                known.Items[itemId] = new(DateTimeOffset.UtcNow, source);
                await SaveAsync().ConfigureAwait(false);

                return source;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Network down, server closed, read timed out: the window hides the hint.
                _failedUtc = DateTimeOffset.UtcNow;
                LastFailure = exception;

                return cached?.Source;
            }
        }
        finally
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException)
            {
                // The app is closing: the container disposed the service while a read was still running, nobody waits.
            }
        }
    }

    /// <summary>A failed write keeps the source in memory: the hover still shows it.</summary>
    private async Task SaveAsync()
    {
        try
        {
            await store.SaveAsync(_document!).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Disk full or locked: the caller reads LastFailure, the next hover reads the item again.
            LastFailure = exception;
        }
    }

    /// <summary>Null when the game does not know the item.</summary>
    private async Task<ItemSource?> ReadAsync(int itemId, Dictionary<int, JobLevel[]> harvest, string language, CancellationToken ct)
    {
        var item = (await GetAsync("Items", [itemId], language, ct).ConfigureAwait(false)).GetValueOrDefault(itemId);

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // As the game lists them, conditional ones (usually while the quest is active) last; reading every dropper to sort them would cost about 30 KB each.
        var always = Ints(item, "dropMonsterIds");
        var dropIds = always.Concat(Ints(item, "conditionnalDropMonsterIds")).Distinct().Take(5).ToArray();
        var monsters = await GetAsync("Monsters", dropIds, language, ct).ConfigureAwait(false);
        var droppers = dropIds.Where(id => monsters.ContainsKey(id) && Name(monsters[id]) is not null).ToArray();
        var subAreas = await GetAsync(
            "SubAreas", [.. droppers.SelectMany(id => Ints(monsters[id], "subareas").Take(5))], language, ct).ConfigureAwait(false);

        // The normal version points to its archimonster, not the reverse: look among the droppers, then the monsters of the archimonster's first zone.
        var archis = droppers.Where(id => Bool(monsters[id], "isMiniBoss")).ToArray();
        // shortcut: 30 neighbours at most (a zone counts about 20), read more if a normal version goes missing.
        var neighbourIds = archis
            .SelectMany(id => Ints(monsters[id], "subareas").Take(1))
            .Where(subAreas.ContainsKey)
            .SelectMany(id => Ints(subAreas[id], "monsters"))
            .Where(id => !monsters.ContainsKey(id))
            .Distinct()
            .Take(30)
            .ToArray();
        var neighbours = await GetAsync("Monsters", neighbourIds, language, ct).ConfigureAwait(false);

        string? NormalOf(int archiId) =>
            monsters.Values.Concat(neighbours.Values)
                .Where(n => Int(n, "correspondingMiniBossId") == archiId)
                .Select(Name)
                .FirstOrDefault();

        MonsterSource Source(int id)
        {
            var m = monsters[id];

            return new(
                id,
                Name(m)!,
                m.TryGetProperty("grades", out var grades) && grades.ValueKind == JsonValueKind.Array
                    ? grades.EnumerateArray().Select(g => Int(g, "level")).DefaultIfEmpty().Max()
                    : 0,
                Bool(m, "isMiniBoss"),
                Bool(m, "isMiniBoss") ? NormalOf(id) : null,
                [.. Ints(m, "subareas").Take(5).Where(subAreas.ContainsKey).Select(a => Name(subAreas[a])).OfType<string>()],
                !always.Contains(id));
        }

        var recipe = (await GetAsync("Recipes", [itemId], language, ct).ConfigureAwait(false)).GetValueOrDefault(itemId);
        JobLevel[] craft = recipe.ValueKind == JsonValueKind.Object && Text(recipe, "jobName") is { } job
            ? [new JobLevel(job, Int(recipe, "resultLevel"))]
            : [];
        var gathered = harvest.GetValueOrDefault(itemId) ?? [];
        MonsterSource[] sources = [.. droppers.Select(Source)];

        return new(sources, gathered, craft, sources.Length == 0 && gathered.Length == 0 && craft.Length == 0, Name(item));
    }

    /// <summary>The whole Skills class (about 200 rows, 90 KB in 2026), the only way from an item to its harvest.</summary>
    private async Task<Dictionary<int, JobLevel[]>> ReadHarvestAsync(string language, CancellationToken ct)
    {
        var skills = (await AskAsync("Skills", null, language, ct).ConfigureAwait(false)).Values
            .Where(s => Int(s, "gatheredRessourceItem") > 0)
            .ToArray();
        var jobs = await GetAsync("Jobs", [.. skills.Select(s => Int(s, "parentJobId"))], language, ct).ConfigureAwait(false);

        return skills
            .Where(s => jobs.ContainsKey(Int(s, "parentJobId")) && Name(jobs[Int(s, "parentJobId")]) is not null)
            .GroupBy(s => Int(s, "gatheredRessourceItem"))
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => new JobLevel(Name(jobs[Int(s, "parentJobId")])!, Int(s, "levelMin"))).Distinct().ToArray());
    }

    /// <summary>An empty set returns nothing without asking: the server may answer the whole class.</summary>
    private async Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass, int[] ids, string language, CancellationToken ct) =>
        ids.Length == 0
            ? new Dictionary<int, JsonElement>()
            : await AskAsync(dataClass, [.. ids.Distinct()], language, ct).ConfigureAwait(false);

    /// <summary>Every request to the game server, spaced by <see cref="RequestSpacing"/>; called under the gate.</summary>
    private async Task<IReadOnlyDictionary<int, JsonElement>> AskAsync(
        string dataClass, int[]? ids, string language, CancellationToken ct)
    {
        var wait = _lastRequestUtc + RequestSpacing - DateTimeOffset.UtcNow;

        if (wait > TimeSpan.Zero)
        {
            await Delay(wait, ct).ConfigureAwait(false);
        }

        try
        {
            return await client.GetAsync(dataClass, ids, language, ct).ConfigureAwait(false);
        }
        finally
        {
            _lastRequestUtc = DateTimeOffset.UtcNow;
        }
    }

    private static string? Name(JsonElement row) => Text(row, "nameId");

    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static int[] Ints(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.TryGetInt32(out _)).Select(v => v.GetInt32())]
            : [];

    public void Dispose() => _gate.Dispose();
}
