using System.Text.Json;

using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Invented game data: every row of id n has the nameId "nom n lang", like the real server.
/// A dungeon of id n has the key n + 1000. Counts the requests per class, texts under "text".
/// </summary>
public sealed class FakeDofusTouchClient : IDofusTouchClient
{
    public Dictionary<string, List<int[]>> Requested { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>Answers rows without a name, as a server that lost its texts would.</summary>
    public bool Nameless { get; set; }

    /// <summary>Rows served as they are (class, then id) instead of the generated ones; a whole-class read gets them all.</summary>
    public Dictionary<string, Dictionary<int, string>> Rows { get; } = [];

    /// <summary>Texts by id; an id without text is left out of the answer.</summary>
    public Dictionary<int, string> Texts { get; } = [];

    /// <summary>When set, every class read waits for it, as a slow server would.</summary>
    public TaskCompletionSource? Pause { get; set; }

    /// <summary>Classes whose read fails while the others answer.</summary>
    public HashSet<string> FailingClasses { get; } = [];

    public async Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass, IReadOnlyCollection<int>? ids, string language, CancellationToken cancellationToken = default)
    {
        if (Pause is not null)
        {
            await Pause.Task.WaitAsync(cancellationToken);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        if (FailingClasses.Contains(dataClass))
        {
            throw new HttpRequestException(dataClass);
        }

        if (!Requested.TryGetValue(dataClass, out var calls))
        {
            Requested[dataClass] = calls = [];
        }

        calls.Add([.. ids ?? []]);

        if (Rows.TryGetValue(dataClass, out var rows))
        {
            return rows
                .Where(r => ids is null || ids.Contains(r.Key))
                .ToDictionary(r => r.Key, r => JsonDocument.Parse(r.Value).RootElement);
        }

        return ids!.ToDictionary(
            id => id,
            id => JsonDocument.Parse(
                $$"""{"nameId":{{(Nameless ? "null" : $"\"nom {id} {language}\"")}},"levelMin":10,"levelMax":20,"stepIds":[{{id}}],"dungeonKey":{{id + 1000}}}""").RootElement);
    }

    public Task<IReadOnlyDictionary<int, string>> GetTextsAsync(
        IReadOnlyCollection<int> ids, string language, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (!Requested.TryGetValue("text", out var calls))
        {
            Requested["text"] = calls = [];
        }

        calls.Add([.. ids]);

        return Task.FromResult<IReadOnlyDictionary<int, string>>(
            ids.Where(Texts.ContainsKey).ToDictionary(id => id, id => Texts[id]));
    }
}
