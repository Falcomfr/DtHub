using System.Text.Json;

using DtHub.Core.Papycha;

namespace DtHub.Tests.Fakes;

/// <summary>
/// Invented game data: every row of id n has the nameId "nom n lang", like the real server.
/// A dungeon of id n has the key n + 1000. Counts the requests per class.
/// </summary>
public sealed class FakeDofusTouchClient : IDofusTouchClient
{
    public Dictionary<string, List<int[]>> Requested { get; } = [];

    public Exception? Failure { get; set; }

    /// <summary>Answers rows without a name, as a server that lost its texts would.</summary>
    public bool Nameless { get; set; }

    public Task<IReadOnlyDictionary<int, JsonElement>> GetAsync(
        string dataClass, IReadOnlyCollection<int>? ids, string language, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        if (!Requested.TryGetValue(dataClass, out var calls))
        {
            Requested[dataClass] = calls = [];
        }

        calls.Add([.. ids!]);

        return Task.FromResult<IReadOnlyDictionary<int, JsonElement>>(ids!.ToDictionary(
            id => id,
            id => JsonDocument.Parse(
                $$"""{"nameId":{{(Nameless ? "null" : $"\"nom {id} {language}\"")}},"levelMin":10,"levelMax":20,"stepIds":[{{id}}],"dungeonKey":{{id + 1000}}}""").RootElement));
    }

    public Task<IReadOnlyDictionary<int, string>> GetTextsAsync(
        IReadOnlyCollection<int> ids, string language, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Names come from nameId, not from texts.");
}
