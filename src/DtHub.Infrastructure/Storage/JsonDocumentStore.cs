using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using DtHub.Core.Localization;
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

    /// <summary>
    /// True while the file exists but could not be read: the defaults
    /// handed out instead must never replace it. Measured cause, a file
    /// held at startup by an antivirus or by the other copy of DT Hub.
    /// </summary>
    private bool _unread;

    private const int ReadAttempts = 4;

    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(250);

    public JsonDocumentStore(string filePath, ILogger logger)
    {
        FilePath = filePath;
        _logger = logger;
    }

    public string FilePath { get; }

    public bool IsUnread => _unread;

    public async Task<T> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
            {
                // Gone: nothing left to protect.
                _unread = false;
                return new T();
            }

            string json;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
                    _unread = false;
                    break;
                }
                catch (FileNotFoundException)
                {
                    // Gone since the check: nothing to protect.
                    _unread = false;
                    return new T();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    if (attempt == ReadAttempts)
                    {
                        _unread = true;
                        LogUnreadable(FilePath, exception.Message);
                        return new T();
                    }

                    await Task.Delay(ReadRetryDelay, cancellationToken).ConfigureAwait(false);
                }
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
                LogCorrupted(FilePath, archived ?? LogMessages.Translate("(archivage impossible)"), exception.Message);
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
            if (_unread)
            {
                LogSaveSkipped(FilePath);
                return;
            }

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(document, SerializerOptions);

            // Nothing new since the last write: devices.json was rewritten
            // every four to six seconds and settings.json every thirty while
            // the panel showed, most of the time unchanged (2026-10-17). Each
            // write is a new file and a rename, which the antivirus scans.
            //
            // Compared with the file itself rather than with what this store
            // last wrote: a restore or a hand edit in between would otherwise
            // have had its replacement skipped.
            if (File.Exists(FilePath)
                && await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false) == json)
            {
                return;
            }

            var temporary = FilePath + ".nouveau";

            // Flushed to the disk before the rename: after a power cut,
            // NTFS keeps the rename but not always the data, and the file
            // came back full of zeros, the good one already gone.
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

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
        Level = LogLevel.Warning,
        Message = "{path} n'a pas pu être lu : rien n'y sera écrit tant qu'il n'aura pas été relu, pour ne pas le remplacer.")]
    private partial void LogSaveSkipped(string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{path} est corrompu et a été archivé sous {archived} ({reason}) ; configuration par défaut recréée.")]
    private partial void LogCorrupted(string path, string archived, string reason);
}
