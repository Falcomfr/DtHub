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
    /// <summary>Guards the document; held only for disk work, so <see cref="CachedAsync"/> answers at once.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>One network read at a time, so a second refresh finds the first one's names instead of asking again.</summary>
    private readonly SemaphoreSlim _read = new(1, 1);

    /// <summary>One bulk facts read at a time; its pauses must not hold <see cref="_read"/>.</summary>
    private readonly SemaphoreSlim _facts = new(1, 1);

    /// <summary>The last refusal of a names or bulk read: neither is retried before <see cref="FactsRetry"/>.</summary>
    private DateTimeOffset? _refusedUtc;

    /// <summary>The last failed names read: not retried before <see cref="NamesRetry"/>.</summary>
    private DateTimeOffset? _namesFailedUtc;

    /// <summary>When the last names read went out: the bulk read keeps <see cref="FactsPause"/> from it.</summary>
    private DateTimeOffset? _namesAskedUtc;

    private readonly Lazy<TouchIdTable> _table = new(table.Load);
    private TouchNamesDocument? _document;

    /// <summary>Quests whose cached steps lacked rewards and were read again: a quest with none is not re-read at each open.</summary>
    private readonly HashSet<int> _rewardsReread = [];

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
        var previous = await CachedAsync(language, ct).ConfigureAwait(false);

        if (IsFresh(previous))
        {
            return previous;
        }

        await _read.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // A refresh that held the read while this one waited may have just read them.
            previous = await CachedAsync(language, ct).ConfigureAwait(false);

            // A server that just refused is spared, as by the bulk read: each show of the window asked again.
            if (IsFresh(previous) || IsRefusing() || (_namesFailedUtc is { } failed && DateTimeOffset.UtcNow - failed < NamesRetry))
            {
                return previous;
            }

            try
            {
                // Read before the swap: a half-read language must not replace a whole one.
                var names = await ReadAsync(language, previous, ct).ConfigureAwait(false);
                await SaveAsync(document => document.Languages[language] = names, ct).ConfigureAwait(false);
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
                _namesFailedUtc = DateTimeOffset.UtcNow;
                LastFailure = exception;

                return previous;
            }
            finally
            {
                _namesAskedUtc = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _read.Release();
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
        // Before the read gate: RefreshAsync takes it itself.
        var known = await CachedAsync(language, ct).ConfigureAwait(false)
            ?? await RefreshAsync(language, ct).ConfigureAwait(false);

        if (known is null || !Table.Quests.TryGetValue(papychaQuestId, out var gameIds))
        {
            return [];
        }

        await _read.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // A refresh may have swapped the record while this waited: write into the live one.
            known = await CachedAsync(language, ct).ConfigureAwait(false) ?? known;

            // A cache from before the rewards existed has none: read it again, once.
            if (known.Steps.TryGetValue(papychaQuestId, out var cached)
                && (cached.All(s => s.Reward is not null) || _rewardsReread.Contains(papychaQuestId)))
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
                LastFailure = null;
                var (steps, types, items) = await ReadStepsAsync(known, stepIds, language, ct).ConfigureAwait(false);
                _rewardsReread.Add(papychaQuestId);
                await SaveAsync(
                    document =>
                    {
                        var live = document.Languages.GetValueOrDefault(language, known);
                        live.Steps[papychaQuestId] = steps;

                        // New dictionaries swapped under the gate: the window and a facts save may be reading the old ones.
                        document.Languages[language] = live with
                        {
                            ObjectiveTypes = Merged(live.ObjectiveTypes, types),
                            Items = Merged(live.Items, items),
                        };
                    },
                    ct).ConfigureAwait(false);

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
            _read.Release();
        }
    }

    /// <summary>After a refused bulk or names read, none is retried before this.</summary>
    public TimeSpan FactsRetry { get; init; } = TimeSpan.FromHours(6);

    /// <summary>After a failed names read, none is retried before this: short, a network down at launch is common.</summary>
    public TimeSpan NamesRetry { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Spacing between the bulk reads, so the game's server is never hammered.</summary>
    public TimeSpan FactsPause { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Every paired quest's reward formula and objectives, by Papycha quest id,
    /// read in four spaced requests (the fourth, objective texts, only when some
    /// objective has one) and kept for a week. Never throws except on
    /// cancellation; on failure returns what the cache had (see <see cref="LastFailure"/>).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, TouchQuestFacts>> FactsAsync(string language, CancellationToken ct = default)
    {
        var known = await CachedAsync(language, ct).ConfigureAwait(false)
            ?? await RefreshAsync(language, ct).ConfigureAwait(false);

        if (known is null)
        {
            return new Dictionary<int, TouchQuestFacts>();
        }

        if (FactsAreSettled(known))
        {
            return known.Facts;
        }

        // Only one bulk read at a time; it holds _read per request, never across the pauses.
        await _facts.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            known = await CachedAsync(language, ct).ConfigureAwait(false) ?? known;

            if (FactsAreSettled(known))
            {
                return known.Facts;
            }

            try
            {
                var facts = await ReadFactsAsync(known, language, ct).ConfigureAwait(false);

                // The live record: a names refresh may have landed during a pause.
                await SaveAsync(
                    document => document.Languages[language] =
                        document.Languages.GetValueOrDefault(language, known) with { Facts = facts, FactsReadUtc = DateTimeOffset.UtcNow }, ct)
                    .ConfigureAwait(false);
                _refusedUtc = null;
                LastFailure = null;

                return facts;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The caller reads LastFailure; last week's facts beat none, and the cooldown spares a server that just refused.
                _refusedUtc = DateTimeOffset.UtcNow;
                LastFailure = exception;

                return known.Facts;
            }
        }
        finally
        {
            _facts.Release();
        }
    }

    // A cache from before the objective texts, or before their coordinates, has null ones: read again, once.
    private bool FactsAreSettled(TouchLanguageNames known) =>
        (DateTimeOffset.UtcNow - known.FactsReadUtc < Freshness
            && !known.Facts.Values.Any(f => f.Objectives.Any(o => o.Text is null || o.Step is null)))
        || IsRefusing();

    private bool IsRefusing() => _refusedUtc is { } refused && DateTimeOffset.UtcNow - refused < FactsRetry;

    private async Task<Dictionary<int, TouchQuestFacts>> ReadFactsAsync(
        TouchLanguageNames known, string language, CancellationToken ct)
    {
        // The first game quest only: "L'Ascension." and "L'Ascension" on one page summed two quests' rewards and time.
        var stepIdsOf = Table.Quests.ToDictionary(
            q => q.Key,
            q => q.Value
                .Where(known.Quests.ContainsKey)
                .Take(1)
                .SelectMany(id => known.Quests[id].StepIds)
                .ToArray());

        async Task<T> Locked<T>(Func<Task<T>> read)
        {
            await _read.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                return await read().ConfigureAwait(false);
            }
            finally
            {
                _read.Release();
            }
        }

        // The weekly names expire with the facts: their five requests may have just gone out.
        if (_namesAskedUtc is { } asked && FactsPause - (DateTimeOffset.UtcNow - asked) is { Ticks: > 0 } wait)
        {
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }

        var steps = await Locked(() => GetAsync("QuestSteps", [.. stepIdsOf.Values.SelectMany(ids => ids)], language, ct)).ConfigureAwait(false);

        if (steps.Count == 0)
        {
            throw new InvalidOperationException("The game's server returned no quest step.");
        }

        await Task.Delay(FactsPause, ct).ConfigureAwait(false);
        var objectives = await Locked(() => GetAsync(
            "QuestObjectives", [.. steps.Values.SelectMany(step => Ints(step, "objectiveIds"))], language, ct)).ConfigureAwait(false);

        await Task.Delay(FactsPause, ct).ConfigureAwait(false);
        var rewardRows = await Locked(() => ReadRewardRowsAsync(steps.Values, language, ct)).ConfigureAwait(false);

        // Objectives worded by a data/text (types 0, 4, 10): where the dungeons are named.
        int? TextId(JsonElement objective) =>
            Ints(objective, "parameters") is [var first, ..] && TouchObjectiveText.KindOf(Int(objective, "typeId"), 1) == TouchHole.Text
                ? first
                : null;
        int[] textIds = [.. objectives.Values.Select(TextId).OfType<int>().Distinct()];
        IReadOnlyDictionary<int, string> texts = new Dictionary<int, string>();

        if (textIds.Length > 0)
        {
            await Task.Delay(FactsPause, ct).ConfigureAwait(false);
            texts = await Locked(() => client.GetTextsAsync(textIds, language, ct)).ConfigureAwait(false);
        }

        return stepIdsOf
            .Where(q => q.Value.Any(steps.ContainsKey))
            .ToDictionary(
                q => q.Key,
                q =>
                {
                    var own = q.Value.Where(steps.ContainsKey).Select(id => steps[id]).ToArray();
                    // A step the server left out hides the reward: the others alone would be a partial sum (D218).
                    var whole = own.Length == q.Value.Length;

                    return new TouchQuestFacts(
                        [.. own.Select(step => whole ? BuildReward(step, rewardRows) : null)],
                        [.. own.SelectMany((step, index) => Ints(step, "objectiveIds")
                            .Where(objectives.ContainsKey)
                            .Select(id =>
                            {
                                var o = objectives[id];
                                var at = Coords(o);

                                return new TouchObjective(
                                    Int(o, "typeId"), Ints(o, "parameters"), (int)(Single(step, "optimalLevel") ?? 0),
                                    TextId(o) is { } text ? texts.GetValueOrDefault(text, "") : "", at?.X, at?.Y, index);
                            }))]);
                });
    }

    private bool IsFresh(TouchLanguageNames? names) =>
        names is not null && DateTimeOffset.UtcNow - names.ReadUtc < Freshness;

    /// <summary>Only the swap and the write take the gate, never a network read: the window waits on it.</summary>
    private async Task SaveAsync(Action<TouchNamesDocument> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var document = await LoadAsync(ct).ConfigureAwait(false);
            change(document);
            await store.SaveAsync(document, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static Dictionary<int, string> Merged(Dictionary<int, string> old, Dictionary<int, string> added)
    {
        if (added.Count == 0)
        {
            return old;
        }

        var merged = new Dictionary<int, string>(old);

        foreach (var (id, name) in added)
        {
            merged[id] = name;
        }

        return merged;
    }

    /// <summary>The steps, with the objective types read (none when the cache had them) and the reward items' names.</summary>
    private async Task<(TouchStep[] Steps, Dictionary<int, string> Types, Dictionary<int, string> Items)> ReadStepsAsync(
        TouchLanguageNames known, int[] stepIds, string language, CancellationToken ct)
    {
        var rows = await GetAsync("QuestSteps", stepIds, language, ct).ConfigureAwait(false);
        var steps = stepIds.Where(rows.ContainsKey).Select(id => rows[id]).ToArray();

        if (steps.Length == 0)
        {
            // Reachable but empty: caching nothing-to-show would hide the steps for good.
            throw new InvalidOperationException("The game's server returned no quest step.");
        }

        var objectives = await GetAsync(
            "QuestObjectives", [.. steps.SelectMany(step => Ints(step, "objectiveIds"))], language, ct).ConfigureAwait(false);

        var types = new Dictionary<int, string>();

        if (known.ObjectiveTypes.Count == 0)
        {
            // The whole class, 35 rows: the only read without ids.
            foreach (var type in await client.GetAsync("QuestObjectiveTypes", null, language, ct).ConfigureAwait(false))
            {
                if (Name(type.Value) is { } name)
                {
                    types[type.Key] = name;
                }
            }
        }

        var templates = types.Count > 0 ? types : known.ObjectiveTypes;

        if (templates.Count == 0)
        {
            throw new InvalidOperationException("The game's server returned no objective type.");
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
            var template = templates.GetValueOrDefault(type, "");

            return new(
                TouchObjectiveText.Fill(template, type, Ints(objective, "parameters"), NameOf),
                Coords(objective) is { } at ? $"[{at.X},{at.Y}]" : "");
        }

        Dictionary<int, TouchRewardRow>? rewardRows;
        var items = new Dictionary<int, string>();

        try
        {
            rewardRows = await ReadRewardRowsAsync(steps, language, ct).ConfigureAwait(false);

            // Reward item names share the dungeon keys' map.
            var rewardItems = await GetAsync(
                "Items", [.. rewardRows.Values.SelectMany(r => r.Items).Select(i => i.ItemId)], language, ct).ConfigureAwait(false);

            foreach (var item in rewardItems.Where(i => Name(i.Value) is not null))
            {
                items[item.Key] = Name(item.Value)!;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            // The steps still show; only the reward line goes.
            LastFailure = exception;
            rewardRows = null;
        }

        TouchStep[] read = [.. steps.Select(step => new TouchStep(
            Name(step) ?? "",
            Text(step, "descriptionId"),
            [.. Ints(step, "objectiveIds").Where(objectives.ContainsKey).Select(id => Line(objectives[id]))],
            BuildReward(step, rewardRows)))];

        return (read, types, items);
    }

    private static bool HasFormula(JsonElement step) =>
        Single(step, "optimalLevel") is not null && Single(step, "duration") is not null
        && Single(step, "xpRatio") is not null && Single(step, "kamasRatio") is not null;

    /// <summary>Null when the step lacks a formula field or the rewards could not be read.</summary>
    private static TouchStepReward? BuildReward(JsonElement step, Dictionary<int, TouchRewardRow>? rewardRows) =>
        rewardRows is not null && HasFormula(step)
            ? new((int)Single(step, "optimalLevel")!, Single(step, "duration")!.Value, Single(step, "xpRatio")!.Value, Single(step, "kamasRatio")!.Value,
                step.TryGetProperty("kamasScaleWithPlayerLevel", out var scale) && scale.ValueKind == JsonValueKind.True,
                [.. Ints(step, "rewardsIds").Where(rewardRows.ContainsKey).Select(id => rewardRows[id])])
            : null;

    private async Task<Dictionary<int, TouchRewardRow>> ReadRewardRowsAsync(
        IEnumerable<JsonElement> steps, string language, CancellationToken ct)
    {
        var rewards = await GetAsync(
            "QuestStepRewards", [.. steps.Where(HasFormula).SelectMany(step => Ints(step, "rewardsIds"))], language, ct).ConfigureAwait(false);

        static TouchItemReward[] ItemRewards(JsonElement row) =>
            row.TryGetProperty("itemsReward", out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray()
                    .Where(pair => pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() >= 2)
                    .Select(pair => new TouchItemReward(
                        pair[0].TryGetInt32(out var id) ? id : 0,
                        pair[1].TryGetInt32(out var quantity) ? quantity : 0))
                    .Where(item => item.ItemId != 0)]
                : [];

        return rewards.ToDictionary(r => r.Key, r => new TouchRewardRow(
            Int(r.Value, "levelMin"),
            Int(r.Value, "levelMax"),
            ItemRewards(r.Value),
            Ints(r.Value, "emotesReward").Length,
            Ints(r.Value, "spellsReward").Length,
            Ints(r.Value, "titlesReward").Length,
            Ints(r.Value, "ornamentsReward").Length,
            Ints(r.Value, "companionsReward").Length,
            Ints(r.Value, "jobsReward").Length));
    }

    private static float? Single(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;

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
            // Steps age with the names; the objective types are templates and stay.
            Steps = [],
            Quests = questNames
                .ToDictionary(
                    r => r.Key,
                    r => new TouchQuest(
                        Name(r.Value)!, Int(r.Value, "levelMin"), Int(r.Value, "levelMax"), Ints(r.Value, "stepIds"))),
            Dungeons = dungeons
                .Where(r => Name(r.Value) is not null)
                .ToDictionary(r => r.Key, r => new TouchDungeon(Name(r.Value)!, Int(r.Value, "dungeonKey"), Ints(r.Value, "mapIds").Length)),
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

    /// <summary>An objective's <c>coords</c>, <c>{"x":..,"y":..}</c>, or <c>{}</c> when it has no map.</summary>
    private static (int X, int Y)? Coords(JsonElement objective) =>
        objective.TryGetProperty("coords", out var c) && c.ValueKind == JsonValueKind.Object
            && c.TryGetProperty("x", out _) && c.TryGetProperty("y", out _)
            ? (Int(c, "x"), Int(c, "y"))
            : null;

    private static int Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static int[] Ints(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(v => v.TryGetInt32(out _)).Select(v => v.GetInt32())]
            : [];

    public void Dispose()
    {
        _gate.Dispose();
        _read.Dispose();
        _facts.Dispose();
    }
}
