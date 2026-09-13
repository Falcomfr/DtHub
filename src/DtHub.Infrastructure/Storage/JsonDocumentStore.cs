using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// JSON storage for a configuration document. Writing goes through a
/// temporary file then a replacement, so that an interruption never
/// leaves a truncated file. Unreadable content is archived alongside
/// rather than deleted: the user keeps a chance to recover their
/// settings.
/// </summary>
public sealed partial class JsonDocumentStore<T> : IDocumentStore<T>, IDisposable
    where T : class, new()
{
    /// <summary>
    /// Shared options. Files are indented and enums are written as
    /// plain text: they stay readable and editable by hand.
    ///
    /// An unknown enum name does not fail the read. The standard
    /// converter, for its part, used to reject the entire file over
    /// that one word: removing a quality tier wiped out the
    /// instances, hotkeys and window geometry of everyone who had
    /// chosen it.
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

    /// <inheritdoc />
    public string Serialize(T document) => JsonSerializer.Serialize(document, SerializerOptions);

    /// <inheritdoc />
    public T? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            // The contract says so: this method does not throw. The
            // caller has already inspected the shape, and a mistyped
            // field remains possible.
            return null;
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

            // Replacement in a single operation: the final file is
            // either the old one or the new one, never a mix of the
            // two.
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Sets the faulty file aside under a timestamped name and
    /// returns its path.
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
            // Silence is intentional for both: archiving a corrupted
            // file is a fallback. If it fails, the caller still
            // reports that the file was unreadable, along with the
            // reason.
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
