using System.Text.Json;

using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the achievement map embedded in the assembly.
///
/// Embedded rather than placed alongside: a neighboring file can go
/// missing, get replaced, or age on its own, whereas the resource
/// follows the executable.
/// </summary>
public sealed partial class EmbeddedQuestSuccessSeed : IQuestSuccessSeed
{
    private const string ResourceName = "DtHub.Infrastructure.quest-successes.json";

    /// <summary>
    /// The file's keys are written in lowercase, the C# properties
    /// are not. Without this tolerance, reading returned an empty
    /// map without raising the slightest error: every entry was read
    /// correctly, but stripped of its achievement, and the count
    /// silently fell to zero.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<EmbeddedQuestSuccessSeed> _logger;

    private IReadOnlyDictionary<string, QuestSeedEntry>? _cache;

    public EmbeddedQuestSuccessSeed(ILogger<EmbeddedQuestSuccessSeed> logger) =>
        _logger = logger;

    public IReadOnlyDictionary<string, QuestSeedEntry> Load()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            using var stream = typeof(EmbeddedQuestSuccessSeed).Assembly
                .GetManifestResourceStream(ResourceName);

            if (stream is null)
            {
                LogUnavailable("ressource absente de l'assemblage");

                return _cache = new Dictionary<string, QuestSeedEntry>(StringComparer.Ordinal);
            }

            var raw = JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream, Json) ?? [];

            // An entry without an achievement is not empty: it can
            // carry only prerequisites. Filtering on the achievement
            // alone would have silently discarded two hundred
            // fourteen of them.
            var map = raw
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.S) || pair.Value.P is { Count: > 0 })
                .ToDictionary(
                    pair => pair.Key,
                    pair => new QuestSeedEntry(
                        pair.Value.S ?? string.Empty,
                        pair.Value.N,
                        pair.Value.O,
                        pair.Value.P ?? []),
                    StringComparer.Ordinal);

            if (map.Count == 0 && raw.Count > 0)
            {
                // The file is there and readable, but no entry has
                // an achievement: this is a format mismatch, not an
                // empty map.
                LogUnavailable($"{raw.Count} entrée(s) sans succès lisible");
            }
            else
            {
                LogLoaded(map.Count);
            }

            return _cache = map;
        }
        catch (JsonException exception)
        {
            // An unreadable map must not prevent indexing: it only
            // supplements what the category pages already provide.
            LogUnavailable(exception.Message);

            return _cache = new Dictionary<string, QuestSeedEntry>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// File format: "s" the achievement, "n" the chain rank, "o" the
    /// place within the achievement, "p" the prerequisites.
    /// </summary>
    private sealed class Entry
    {
        public string? S { get; set; }

        public int N { get; set; }

        public int O { get; set; }

        public List<string>? P { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carte des succès embarquée : {count} quête(s).")]
    private partial void LogLoaded(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Carte des succès illisible ({reason}) ; les intertitres des pages suffiront.")]
    private partial void LogUnavailable(string reason);
}
