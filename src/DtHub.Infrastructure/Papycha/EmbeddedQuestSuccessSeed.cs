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

    private readonly ILogger<EmbeddedQuestSuccessSeed> _logger;

    private IReadOnlyDictionary<string, string>? _cache;

    public EmbeddedQuestSuccessSeed(ILogger<EmbeddedQuestSuccessSeed> logger) =>
        _logger = logger;

    public IReadOnlyDictionary<string, string> Load()
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

                return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                      ?? [];

            LogLoaded(map.Count);

            return _cache = map;
        }
        catch (JsonException exception)
        {
            // Une carte illisible ne doit pas empêcher l'indexation : elle ne
            // fait que compléter ce que les pages de rubrique donnent déjà.
            LogUnavailable(exception.Message);

            return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carte des succès embarquée : {count} quête(s).")]
    private partial void LogLoaded(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Carte des succès illisible ({reason}) ; les intertitres des pages suffiront.")]
    private partial void LogUnavailable(string reason);
}
