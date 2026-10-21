using System.Text.Json;

using DtHub.Core.Localization;
using DtHub.Core.Storage;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads the game's names for the ids of the table, keeps them per language
/// and serves them from the cache. A refusal keeps what we had: showing
/// last week's names beats showing none.
/// </summary>
public sealed class TouchNameService(
    IDofusTouchClient client,
    IDocumentStore<TouchNamesDocument> store,
    ITouchIdTableSource table) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lazy<TouchIdTable> _table = new(table.Load);
    private TouchNamesDocument? _document;

    /// <summary>The game ships fr, en and es: anything else reads English.</summary>
    public static string CurrentLanguage =>
        Strings.Spoken.TwoLetterISOLanguageName is "fr" or "es"
            ? Strings.Spoken.TwoLetterISOLanguageName
            : "en";

    public TouchIdTable Table => _table.Value;

    public TimeSpan Freshness { get; init; } = TimeSpan.FromDays(7);

    public Exception? LastFailure { get; private set; }

    public async Task<TouchLanguageNames?> CachedAsync(string language, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return (await LoadAsync(ct).ConfigureAwait(false)).Languages.GetValueOrDefault(language);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the names unless the cache is fresh; never throws except on cancellation.</summary>
    public async Task<TouchLanguageNames?> RefreshAsync(string language, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var document = await LoadAsync(ct).ConfigureAwait(false);
            var previous = document.Languages.GetValueOrDefault(language);

            if (previous is not null && DateTimeOffset.UtcNow - previous.ReadUtc < Freshness)
            {
                return previous;
            }

            try
            {
                // Read before the swap: a half-read language must not replace a whole one.
                var names = await ReadAsync(language, previous, ct).ConfigureAwait(false);
                document.Languages[language] = names;
                await store.SaveAsync(document, ct).ConfigureAwait(false);
                LastFailure = null;

                return names;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Network down, server closed, unreadable answer: the caller reads LastFailure.
                LastFailure = exception;

                return previous;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TouchNamesDocument> LoadAsync(CancellationToken ct) =>
        _document ??= await store.LoadAsync(ct).ConfigureAwait(false);

    private async Task<TouchLanguageNames> ReadAsync(
        string language, TouchLanguageNames? previous, CancellationToken ct)
    {
        var quests = await GetAsync("Quests", [.. Table.Quests.Values.SelectMany(ids => ids)], language, ct).ConfigureAwait(false);
        var dungeons = await GetAsync("Dungeons", [.. Table.Dungeons.Values.SelectMany(d => d.Dungeons)], language, ct).ConfigureAwait(false);
        var monsters = await GetAsync("Monsters", [.. Table.Dungeons.Values.Select(d => d.Monster).Where(id => id != 0)], language, ct).ConfigureAwait(false);
        var items = await GetAsync("Items", [.. dungeons.Values.Select(d => Int(d, "dungeonKey")).Where(id => id != 0)], language, ct).ConfigureAwait(false);
        var achievements = await GetAsync("Achievements", [.. Table.Series.Values.Concat(Table.Links.Values)], language, ct).ConfigureAwait(false);

        Dictionary<int, string> Names(IReadOnlyDictionary<int, JsonElement> rows) =>
            rows.Select(r => (r.Key, Name: Name(r.Value)))
                .Where(r => r.Name is not null)
                .ToDictionary(r => r.Key, r => r.Name!);

        var questNames = quests.Where(r => Name(r.Value) is not null).ToArray();

        if (questNames.Length == 0)
        {
            // Reachable but empty: caching nothing for a week would hide the names.
            throw new InvalidOperationException("The game's server returned no quest name.");
        }

        return (previous ?? new TouchLanguageNames()) with
        {
            ReadUtc = DateTimeOffset.UtcNow,
            Quests = questNames
                .ToDictionary(
                    r => r.Key,
                    r => new TouchQuest(
                        Name(r.Value)!, Int(r.Value, "levelMin"), Int(r.Value, "levelMax"), Ints(r.Value, "stepIds"))),
            Dungeons = dungeons
                .Where(r => Name(r.Value) is not null)
                .ToDictionary(r => r.Key, r => new TouchDungeon(Name(r.Value)!, Int(r.Value, "dungeonKey"))),
            Monsters = Names(monsters),
            Items = Names(items),
            Achievements = Names(achievements),
        };
    }

    /// <summary>An empty set returns nothing without asking: the server may answer the whole class.</summary>
    private async Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass, int[] ids, string language, CancellationToken ct) =>
        ids.Length == 0
            ? new Dictionary<int, JsonElement>()
            : await client.GetAsync(dataClass, [.. ids.Distinct()], language, ct).ConfigureAwait(false);

    /// <summary>The server sends the localized name itself in nameId.</summary>
    private static string? Name(JsonElement row) =>
        row.TryGetProperty("nameId", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static int[] Ints(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.TryGetInt32(out _)).Select(v => v.GetInt32())]
            : [];

    public void Dispose() => _gate.Dispose();
}
