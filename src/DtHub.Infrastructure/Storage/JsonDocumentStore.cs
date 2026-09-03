using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// Stockage JSON d'un document de configuration. L'écriture passe par un
/// fichier temporaire puis un remplacement, de sorte qu'une coupure ne laisse
/// jamais un fichier tronqué. Un contenu illisible est archivé à côté plutôt
/// que supprimé : l'utilisateur garde une chance de récupérer ses réglages.
/// </summary>
public sealed partial class JsonDocumentStore<T> : IDocumentStore<T>, IDisposable
    where T : class, new()
{
    /// <summary>
    /// Options partagées. Les fichiers sont indentés et les énumérations
    /// écrites en clair : ils restent lisibles et modifiables à la main.
    ///
    /// Un nom d'énumération inconnu ne fait pas échouer la lecture. Le
    /// convertisseur standard, lui, refusait le fichier entier sur ce seul
    /// mot : retirer un palier de qualité effaçait les instances, les
    /// raccourcis et la géométrie des fenêtres de tous ceux qui l'avaient
    /// choisi.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new TolerantEnumConverterFactory() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonDocumentStore(string filePath, ILogger logger)
    {
        FilePath = filePath;
        _logger = logger;
    }

    public string FilePath { get; }

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                return new T();
            }

            string json;
            try
            {
                json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                LogUnreadable(FilePath, exception.Message);
                return new T();
            }
            catch (UnauthorizedAccessException exception)
            {
                LogUnreadable(FilePath, exception.Message);
                return new T();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
            }
            catch (JsonException exception)
            {
                var archived = Quarantine();
                LogCorrupted(FilePath, archived ?? "(archivage impossible)", exception.Message);
                return new T();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(document, SerializerOptions);
            var temporary = FilePath + ".nouveau";

            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);

            // Remplacement en une opération : le fichier définitif est soit
            // l'ancien, soit le nouveau, jamais un mélange des deux.
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Met le fichier fautif de côté sous un nom horodaté et rend son chemin.
    /// </summary>
    private string? Quarantine()
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var archived = $"{FilePath}.corrompu-{stamp}";

            File.Move(FilePath, archived, overwrite: true);
            return archived;
        }
        catch (IOException)
        {
            // Silence assumé pour les deux : l'archivage d'un fichier corrompu
            // est un secours. S'il échoue, l'appelant dit quand même que le
            // fichier était illisible, avec la raison.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{path} est illisible ({reason}) ; configuration par défaut utilisée.")]
    private partial void LogUnreadable(string path, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{path} est corrompu et a été archivé sous {archived} ({reason}) ; configuration par défaut recréée.")]
    private partial void LogCorrupted(string path, string archived, string reason);
}
