using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the identifier table embedded in the assembly.
///
/// Embedded for the same reason as the catalog: the resource follows the
/// executable, a neighboring file can go missing or age on its own.
/// </summary>
public sealed partial class EmbeddedTouchIdTable : ITouchIdTableSource
{
    private const string ResourceName = "DtHub.Infrastructure.dofus-touch-ids.json";

    private readonly ILogger<EmbeddedTouchIdTable> _logger;

    public EmbeddedTouchIdTable(ILogger<EmbeddedTouchIdTable> logger) =>
        _logger = logger;

    /// <summary>
    /// Read again on each call rather than kept: the caller may change what
    /// it gets.
    /// </summary>
    public TouchIdTable Load()
    {
        try
        {
            using var stream = typeof(EmbeddedTouchIdTable).Assembly
                .GetManifestResourceStream(ResourceName);

            if (stream is null)
            {
                LogUnavailable("ressource absente de l'assemblage");

                return new TouchIdTable();
            }

            var document = JsonSerializer.Deserialize<TouchIdTable>(
                stream,
                JsonDocumentStore<TouchIdTable>.SerializerOptions) ?? new TouchIdTable();

            LogLoaded(document.Quests.Count);

            return document;
        }
        catch (JsonException exception)
        {
            // An unreadable table pairs nothing: every name stays French.
            LogUnavailable(exception.Message);

            return new TouchIdTable();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Table d'identifiants embarquée : {count} quête(s) appariée(s).")]
    private partial void LogLoaded(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Table d'identifiants embarquée illisible ({reason}) ; les noms restent en français.")]
    private partial void LogUnavailable(string reason);
}
