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

    private DateTimeOffset? _failedUtc;

    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(30);

    public Exception? LastFailure { get; private set; }

    /// <summary>After a failed read, none is tried before this: a refusing server is not asked at every hover.</summary>
    public TimeSpan FailureCooldown { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The shared HTTP client waits ten minutes; a stalled read must not hold the gate that long.</summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Never throws except on cancellation; on failure the cached source or null (see <see cref="LastFailure"/>).</summary>
    public async Task<ItemSource?> SourceAsync(int itemId, string language, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            _document ??= await store.LoadAsync(ct).ConfigureAwait(false);

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

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ReadTimeout);

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
                    await SaveAsync(ct).ConfigureAwait(false);
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
                await SaveAsync(ct).ConfigureAwait(false);

                return source;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
            _gate.Release();
        }
    }

    /// <summary>A failed write keeps the source in memory: the hover still shows it.</summary>
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await store.SaveAsync(_document!, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
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

        return new(sources, gathered, craft, sources.Length == 0 && gathered.Length == 0 && craft.Length == 0);
    }

    /// <summary>The whole Skills class (about 200 rows, 90 KB in 2026), the only way from an item to its harvest.</summary>
    private async Task<Dictionary<int, JobLevel[]>> ReadHarvestAsync(string language, CancellationToken ct)
    {
        var skills = (await client.GetAsync("Skills", null, language, ct).ConfigureAwait(false)).Values
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
            : await client.GetAsync(dataClass, [.. ids.Distinct()], language, ct).ConfigureAwait(false);

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
