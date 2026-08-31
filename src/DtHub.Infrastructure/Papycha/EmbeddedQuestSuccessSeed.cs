using System.Text.Json;

using DtHub.Core.Papycha;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Lit la carte des succès embarquée dans l'assemblage.
///
/// Embarquée et non posée à côté : un fichier voisin peut manquer, être
/// remplacé ou vieillir seul, alors que la ressource suit l'exécutable.
/// </summary>
public sealed partial class EmbeddedQuestSuccessSeed : IQuestSuccessSeed
{
    private const string ResourceName = "DtHub.Infrastructure.quest-successes.json";

    /// <summary>
    /// Les clés du fichier sont écrites en minuscules, les propriétés en C# ne
    /// le sont pas. Sans cette tolérance, la lecture rendait une carte vide
    /// sans lever la moindre erreur : chaque entrée était bien lue, mais
    /// dépourvue de son succès, et le compte tombait à zéro en silence.
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

            var map = raw
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.S))
                .ToDictionary(
                    pair => pair.Key,
                    pair => new QuestSeedEntry(pair.Value.S!, pair.Value.N, pair.Value.O),
                    StringComparer.Ordinal);

            if (map.Count == 0 && raw.Count > 0)
            {
                // Le fichier est là et se lit, mais aucune entrée n'a de
                // succès : c'est un désaccord de forme, pas une carte vide.
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
            // Une carte illisible ne doit pas empêcher l'indexation : elle ne
            // fait que compléter ce que les pages de rubrique donnent déjà.
            LogUnavailable(exception.Message);

            return _cache = new Dictionary<string, QuestSeedEntry>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Forme du fichier : « s » le succès, « n » le rang de chaîne, « o » la
    /// place dans le succès.
    /// </summary>
    private sealed class Entry
    {
        public string? S { get; set; }

        public int N { get; set; }

        public int O { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carte des succès embarquée : {count} quête(s).")]
    private partial void LogLoaded(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Carte des succès illisible ({reason}) ; les intertitres des pages suffiront.")]
    private partial void LogUnavailable(string reason);
}
