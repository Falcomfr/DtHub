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

    /// <summary>
    /// The official steps of a Papycha quest, read once and kept. A page can
    /// pair with many game quests sharing one name (homonyms): only the first
    /// game quest of each distinct name is read. Never throws except on
    /// cancellation; empty when unpaired or on failure (see <see cref="LastFailure"/>).
    /// </summary>
    public async Task<IReadOnlyList<TouchStep>> StepsAsync(
        int papychaQuestId, string language, CancellationToken ct = default)
    {
        // Outside the gate: both calls take it themselves.
        var known = await CachedAsync(language, ct).ConfigureAwait(false)
            ?? await RefreshAsync(language, ct).ConfigureAwait(false);

        if (known is null || !Table.Quests.TryGetValue(papychaQuestId, out var gameIds))
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (known.Steps.TryGetValue(papychaQuestId, out var cached))
            {
                return cached;
            }

            var stepIds = gameIds
                .Where(known.Quests.ContainsKey)
                .GroupBy(id => known.Quests[id].Name)
                .SelectMany(g => known.Quests[g.First()].StepIds)
                .ToArray();

            if (stepIds.Length == 0)
            {
                return [];
            }

            try
            {
                var steps = await ReadStepsAsync(known, stepIds, language, ct).ConfigureAwait(false);
                known.Steps[papychaQuestId] = steps;
                await store.SaveAsync(await LoadAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                LastFailure = null;

                return steps;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The window reads LastFailure and logs it once.
                LastFailure = exception;

                return [];
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TouchStep[]> ReadStepsAsync(
        TouchLanguageNames known, int[] stepIds, string language, CancellationToken ct)
    {
        var rows = await GetAsync("QuestSteps", stepIds, language, ct).ConfigureAwait(false);
        var steps = stepIds.Where(rows.ContainsKey).Select(id => rows[id]).ToArray();
        var objectives = await GetAsync(
            "QuestObjectives", [.. steps.SelectMany(step => Ints(step, "objectiveIds"))], language, ct).ConfigureAwait(false);

        if (known.ObjectiveTypes.Count == 0)
        {
            // The whole class, 35 rows: the only read without ids.
            var types = await client.GetAsync("QuestObjectiveTypes", null, language, ct).ConfigureAwait(false);

            foreach (var type in types.Where(t => Name(t.Value) is not null))
            {
                known.ObjectiveTypes[type.Key] = Name(type.Value)!;
            }
        }

        var holes = objectives.Values
            .SelectMany(o => Ints(o, "parameters").Select((value, index) => (Kind: TouchObjectiveText.KindOf(Int(o, "typeId"), index + 1), value)))
            .Where(h => h.Kind != TouchHole.Count)
            .ToLookup(h => h.Kind, h => h.value);

        var names = new Dictionary<(TouchHole, int), string>();

        foreach (var (kind, dataClass) in new[] { (TouchHole.Npc, "Npcs"), (TouchHole.Item, "Items"), (TouchHole.Monster, "Monsters"), (TouchHole.Area, "SubAreas") })
        {
            foreach (var row in await GetAsync(dataClass, [.. holes[kind]], language, ct).ConfigureAwait(false))
            {
                if (Name(row.Value) is { } name)
                {
                    names[(kind, row.Key)] = name;
                }
            }
        }

        var texts = holes[TouchHole.Text].Distinct().ToArray();

        if (texts.Length > 0)
        {
            foreach (var text in await client.GetTextsAsync(texts, language, ct).ConfigureAwait(false))
            {
                names[(TouchHole.Text, text.Key)] = text.Value;
            }
        }

        string? NameOf(TouchHole kind, int id) => names.GetValueOrDefault((kind, id));

        TouchObjectiveLine Line(JsonElement objective)
        {
            var type = Int(objective, "typeId");
            var template = known.ObjectiveTypes.GetValueOrDefault(type, "");

            return new(
                TouchObjectiveText.Fill(template, type, Ints(objective, "parameters"), NameOf),
                objective.TryGetProperty("coords", out var c) && c.TryGetProperty("x", out _) && c.TryGetProperty("y", out _)
                    ? $"[{Int(c, "x")},{Int(c, "y")}]"
                    : "");
        }

        return [.. steps.Select(step => new TouchStep(
            Name(step) ?? "",
            Text(step, "descriptionId"),
            [.. Ints(step, "objectiveIds").Where(objectives.ContainsKey).Select(id => Line(objectives[id]))]))];
    }

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

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
