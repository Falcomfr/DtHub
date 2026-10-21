using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

using DtHub.Core.Localization;
using DtHub.Core.Storage;
using DtHub.Core.Updates;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Updates;

/// <summary>
/// Keeps the application up to date from the repository's releases.
///
/// The thread is as follows: at startup we ask for the latest release;
/// if it is more recent and automatic updates are checked, we download
/// it in the background, verify its digest, and place it alongside.
/// Nothing is replaced in the middle of a session: the swap happens at
/// shutdown, when nothing is running anymore. The release notes, for
/// their part, wait for the next startup, the one that finally runs the
/// new version. They come in the interface language when the release
/// carries it, and in English otherwise.
///
/// No failure is a fault: no network, missing repository, wrong
/// digest, locked file, the application keeps going with the version
/// it has.
/// </summary>
/// <param name="noteWait">
/// What is granted to the translated note before the English one is
/// kept. The production value is that of <see cref="NoteWait" />;
/// tests shorten it so as not to wait for real.
/// </param>
public sealed partial class UpdateService(
    IReleaseSource source,
    IAppPaths paths,
    UpdateTarget target,
    ILogger<UpdateService> logger,
    TimeSpan? noteWait = null)
{
    private readonly IReleaseSource _source = source;
    private readonly IAppPaths _paths = paths;
    private readonly UpdateTarget _target = target;
    private readonly ILogger<UpdateService> _logger = logger;
    private readonly TimeSpan _noteWait = noteWait ?? NoteWait;

    /// <summary>
    /// What a translated note is granted. The updates client waits ten
    /// minutes, which suits a sixty megabyte download and not a nicety
    /// that holds the whole update back while it is read.
    /// </summary>
    public static readonly TimeSpan NoteWait = TimeSpan.FromSeconds(30);

    /// <summary>The version that is running.</summary>
    public Version Running { get; } = ReleaseParser.Normalize(target?.Running);

    /// <summary>The more recent release found, if there is one.</summary>
    public AppRelease? Available { get; private set; }

    /// <summary>True when this release is downloaded and verified.</summary>
    public bool Ready { get; private set; }

    /// <summary>Raised when either of the previous two changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Looks for a more recent release, and prepares it if asked to do
    /// so on its own.
    /// </summary>
    public async Task CheckAsync(bool automatic, CancellationToken cancellationToken = default)
    {
        var latest = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(true);

        if (latest is null || latest.Version <= Running)
        {
            return;
        }

        // There is no point offering what we could not place: in a
        // source tree, the launcher republishes at every startup and
        // would overwrite the update within the second.
        if (!UpdatePaths.CanReplace(_target.ExecutablePath, File.Exists))
        {
            LogSourceTree(latest.Version.ToString());

            return;
        }

        Available = await TranslatedAsync(latest, cancellationToken).ConfigureAwait(true);
        LogFound(latest.Version.ToString(), Running.ToString());
        Changed?.Invoke(this, EventArgs.Empty);

        if (automatic)
        {
            _ = await PrepareAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// The release with its notes in the interface language, or as it
    /// came when that language has no note of its own. A translation
    /// that does not arrive is no reason to hide the English one.
    /// </summary>
    private async Task<AppRelease> TranslatedAsync(
        AppRelease release,
        CancellationToken cancellationToken)
    {
        var language = Strings.Spoken.TwoLetterISOLanguageName;

        if (!release.NoteUrls.TryGetValue(language, out var url))
        {
            return release;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_noteWait);

        try
        {
            var notes = (await _source.ReadAsync(url, deadline.Token).ConfigureAwait(true))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();

            return notes.Length > 0 ? release with { Notes = notes } : release;
        }
        // A charset .NET does not know throws InvalidOperationException,
        // a malformed address UriFormatException. A cancellation asked by
        // the caller goes back up; one that is only the deadline does not.
        catch (Exception exception) when (exception is HttpRequestException
            or InvalidOperationException or UriFormatException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogTranslationUnread(language);

            return release;
        }
    }

    /// <summary>
    /// Downloads the release and verifies its digest. True if it is
    /// ready to be placed.
    /// </summary>
    public async Task<bool> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release)
        {
            return false;
        }

        var staged = UpdatePaths.Staged(_paths.UpdatesDirectory, release.Version);

        try
        {
            _ = Directory.CreateDirectory(_paths.UpdatesDirectory);

            if (!File.Exists(staged))
            {
                await _source
                    .DownloadAsync(release.DownloadUrl, staged, progress: null, cancellationToken)
                    .ConfigureAwait(true);
            }

            var expected = Digest(await _source
                .ReadAsync(release.DigestUrl, cancellationToken)
                .ConfigureAwait(true));

            var actual = await Sha256Async(staged, cancellationToken).ConfigureAwait(true);

            if (expected.Length == 0
                || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                // An executable that does not match what the
                // repository announces replaces nothing at all, and
                // does not stay on disk.
                LogDigestMismatch(release.Version.ToString());
                Delete(staged);

                return false;
            }

            if (!await CopyBesideAsync(staged, expected, release.Version, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            await File
                .WriteAllTextAsync(
                    UpdatePaths.Notes(_paths.UpdatesDirectory, release.Version),
                    ReleaseNotes.Readable(release.Notes),
                    cancellationToken)
                .ConfigureAwait(true);

            Ready = true;
            LogReady(release.Version.ToString());
            Changed?.Invoke(this, EventArgs.Empty);

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            LogPrepareFailed(exception);
            Delete(staged);

            return false;
        }
    }

    /// <summary>
    /// Copies the verified download beside the executable, so that Apply
    /// only renames. The executable can live on another disk (D10), and a
    /// move across disks is a copy: cut short by a shutdown, it left a
    /// truncated DtHub.exe that no longer started. The copy is checked as
    /// the download was.
    ///
    /// A failed copy keeps the download: a folder DT Hub cannot write to,
    /// Program Files or a protected folder, would otherwise cost sixty
    /// megabytes at every startup and still never be ready.
    /// </summary>
    private async Task<bool> CopyBesideAsync(
        string staged,
        string expected,
        Version version,
        CancellationToken cancellationToken)
    {
        var incoming = Incoming();

        try
        {
            // Off the UI thread, which the startup chain comes back to:
            // sixty megabytes copied and hashed there froze the panel.
            var actual = await Task.Run(
                async () =>
                {
                    File.Copy(staged, incoming, overwrite: true);

                    return await Sha256Async(incoming, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(true);

            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            LogDigestMismatch(version.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCopyFailed(exception);
        }

        Delete(incoming);

        return false;
    }

    /// <summary>
    /// Places the prepared update, at shutdown. True if the executable
    /// was indeed replaced.
    ///
    /// A running executable cannot be overwritten, but it can be
    /// renamed: the old one steps aside, the new one takes its place,
    /// and the next startup sweeps away what remains. If the second
    /// half fails, the first is undone: better the old version than no
    /// application at all.
    /// </summary>
    /// <param name="automatic">
    /// The box as it stands at shutdown, not as it stood when the update
    /// was prepared: unchecked in between, the player keeps his version.
    /// </param>
    public bool Apply(bool automatic)
    {
        var current = _target.ExecutablePath;

        if (!automatic
            || !Ready
            || Available is not { } release
            || string.IsNullOrEmpty(current)
            || !UpdatePaths.CanReplace(current, File.Exists))
        {
            return false;
        }

        var incoming = Incoming();

        if (!File.Exists(incoming))
        {
            return false;
        }

        var retired = Path.Combine(
            Path.GetDirectoryName(current) ?? string.Empty, UpdatePaths.Retired);

        try
        {
            Delete(retired);
            File.Move(current, retired);

            try
            {
                File.Move(incoming, current);
            }
            // Denied too, by an antivirus blocking the new executable: the
            // old one must come back, or nothing would be left to start.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.Move(retired, current);

                throw;
            }

            LogApplied(release.Version.ToString());

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogApplyFailed(exception);

            return false;
        }
    }

    /// <summary>
    /// What the version that was just placed announces, or an empty
    /// string. Read once: the file is deleted along the way, so the
    /// note does not come back at every startup.
    /// </summary>
    public string TakeNotes()
    {
        var file = UpdatePaths.Notes(_paths.UpdatesDirectory, Running);

        try
        {
            if (!File.Exists(file))
            {
                return string.Empty;
            }

            var notes = File.ReadAllText(file);
            Delete(file);

            return notes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Silence assumed: release notes are a nicety. Without
            // them the "What's New" window does not appear, and the
            // update happened all the same.
            return string.Empty;
        }
    }

    /// <summary>
    /// Sweeps away what the previous update left behind: the old
    /// executable set aside, and releases older than the one running.
    /// </summary>
    public void Sweep()
    {
        var directory = Path.GetDirectoryName(_target.ExecutablePath);

        if (directory is not null)
        {
            Delete(Path.Combine(directory, UpdatePaths.Retired));

            // A copy prepared and never placed: the next preparation makes
            // a fresh one, and an unchecked box would leave sixty megabytes.
            Delete(Path.Combine(directory, UpdatePaths.Incoming));
        }

        try
        {
            if (!Directory.Exists(_paths.UpdatesDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_paths.UpdatesDirectory, "DtHub-*.exe"))
            {
                var version = ReleaseParser.VersionOf(
                    Path.GetFileNameWithoutExtension(file).Replace("DtHub-", string.Empty, StringComparison.Ordinal));

                if (version is null || version <= Running)
                {
                    Delete(file);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSweepFailed(exception);
        }
    }

    /// <summary>The verified copy waiting beside the executable.</summary>
    private string Incoming() =>
        Path.Combine(Path.GetDirectoryName(_target.ExecutablePath) ?? string.Empty, UpdatePaths.Incoming);

    /// <summary>
    /// The digest carried by the companion file. The usual format is
    /// "digest  file-name"; only the first is kept.
    /// </summary>
    private static string Digest(string? content)
    {
        var text = (content ?? string.Empty).Trim();
        var cut = text.IndexOfAny([' ', '\t', '\r', '\n']);

        if (cut > 0)
        {
            text = text[..cut];
        }

        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text : string.Empty;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        return Convert.ToHexStringLower(hash);
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be deleted does not deserve to stop
            // anything: it will be picked up at the next cleanup.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} disponible, la version en cours est {running}.")]
    private partial void LogFound(string version, string running);

    [LoggerMessage(Level = LogLevel.Information, Message = "Note de version en {language} illisible, l'anglais reste.")]
    private partial void LogTranslationUnread(string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} ignorée : l'exécutable sort d'un arbre de sources.")]
    private partial void LogSourceTree(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} téléchargée et vérifiée.")]
    private partial void LogReady(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'empreinte de la livraison {version} ne correspond pas.")]
    private partial void LogDigestMismatch(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison n'a pas pu être préparée.")]
    private partial void LogPrepareFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison n'a pas pu être copiée à côté de l'exécutable.")]
    private partial void LogCopyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {version} posée, elle démarrera au prochain lancement.")]
    private partial void LogApplied(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La mise à jour n'a pas pu être posée.")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le ménage des mises à jour a échoué.")]
    private partial void LogSweepFailed(Exception exception);
}
