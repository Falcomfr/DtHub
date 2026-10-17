using System.Text.Json;

using DtHub.Core.Papycha;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Papycha;

/// <summary>
/// Reads the catalog embedded in the assembly, written in the same shape
/// as the cache on disk.
///
/// Embedded rather than placed alongside: a neighboring file can go
/// missing, get replaced, or age on its own, whereas the resource follows
/// the executable.
/// </summary>
public sealed partial class EmbeddedQuestCatalogSnapshot : IQuestCatalogSnapshot
{
    private const string ResourceName = "DtHub.Infrastructure.papycha-catalog.json";

    private readonly ILogger<EmbeddedQuestCatalogSnapshot> _logger;

    public EmbeddedQuestCatalogSnapshot(ILogger<EmbeddedQuestCatalogSnapshot> logger) =>
        _logger = logger;

    /// <summary>
    /// Read again on each call rather than kept: the service merges into
    /// the document it receives, and it is only asked for when there is no
    /// cache, so once per session at most.
    /// </summary>
    public QuestCatalogDocument Load()
    {
        try
        {
            using var stream = typeof(EmbeddedQuestCatalogSnapshot).Assembly
                .GetManifestResourceStream(ResourceName);

            if (stream is null)
            {
                LogUnavailable("ressource absente de l'assemblage");

                return new QuestCatalogDocument();
            }

            var document = JsonSerializer.Deserialize<QuestCatalogDocument>(
                stream,
                JsonDocumentStore<QuestCatalogDocument>.SerializerOptions) ?? new QuestCatalogDocument();

            LogLoaded(document.Quests.Count);

            return document;
        }
        catch (JsonException exception)
        {
            // An unreadable snapshot leaves an empty base: the merge with the
            // site's API then fills what it can.
            LogUnavailable(exception.Message);

            return new QuestCatalogDocument();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue embarqué : {count} quête(s).")]
    private partial void LogLoaded(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Catalogue embarqué illisible ({reason}) ; l'API de papycha comblera ce qu'elle peut.")]
    private partial void LogUnavailable(string reason);
}
