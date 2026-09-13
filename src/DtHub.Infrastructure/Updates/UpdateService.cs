using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

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
/// new version.
///
/// No failure is a fault: no network, missing repository, wrong
/// digest, locked file, the application keeps going with the version
/// it has.
/// </summary>
public sealed partial class UpdateService(
    IReleaseSource source,
    IAppPaths paths,
    UpdateTarget target,
    ILogger<UpdateService> logger)
{
    private readonly IReleaseSource _source = source;
    private readonly IAppPaths _paths = paths;
    private readonly UpdateTarget _target = target;
    private readonly ILogger<UpdateService> _logger = logger;

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

        Available = latest;
        LogFound(latest.Version.ToString(), Running.ToString());
        Changed?.Invoke(this, EventArgs.Empty);

        if (automatic)
        {
            _ = await PrepareAsync(cancellationToken).ConfigureAwait(true);
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
    /// Places the prepared update, at shutdown. True if the executable
    /// was indeed replaced.
    ///
    /// A running executable cannot be overwritten, but it can be
    /// renamed: the old one steps aside, the new one takes its place,
    /// and the next startup sweeps away what remains. If the second
    /// half fails, the first is undone: better the old version than no
    /// application at all.
    /// </summary>
    public bool Apply()
    {
        var current = _target.ExecutablePath;

        if (!Ready
            || Available is not { } release
            || string.IsNullOrEmpty(current)
            || !UpdatePaths.CanReplace(current, File.Exists))
        {
            return false;
        }

        var staged = UpdatePaths.Staged(_paths.UpdatesDirectory, release.Version);

        if (!File.Exists(staged))
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
                File.Move(staged, current);
            }
            catch (IOException)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} ignorée : l'exécutable sort d'un arbre de sources.")]
    private partial void LogSourceTree(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} téléchargée et vérifiée.")]
    private partial void LogReady(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'empreinte de la livraison {version} ne correspond pas.")]
    private partial void LogDigestMismatch(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison n'a pas pu être préparée.")]
    private partial void LogPrepareFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {version} posée, elle démarrera au prochain lancement.")]
    private partial void LogApplied(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La mise à jour n'a pas pu être posée.")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le ménage des mises à jour a échoué.")]
    private partial void LogSweepFailed(Exception exception);
}
