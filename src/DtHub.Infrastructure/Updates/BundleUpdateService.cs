using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

using DtHub.Core.Localization;
using DtHub.Core.Processes;
using DtHub.Core.Storage;
using DtHub.Core.Updates;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Updates;

/// <summary>
/// The Mac's <see cref="UpdateService"/>: the same thread, on a "DT
/// Hub.app" rather than a DtHub.exe. At startup the latest release is
/// asked for; if it is more recent and automatic updates are checked, its
/// DtHub-mac.zip is downloaded in the background, its digest verified, and
/// it is unpacked into the updates folder, its signature checked. The
/// swap happens at shutdown, the notes wait for the next startup.
///
/// A running application can be moved on macOS: the old bundle steps
/// aside into the updates folder, the new one takes its place, and the
/// next startup sweeps away what remains. If the second half fails, the
/// first is undone.
///
/// Only the Mac front-end uses it; the PC's update stays the PC's. No
/// failure is a fault: the application keeps the version it has.
/// </summary>
/// <param name="bundlePath">
/// The running "DT Hub.app", or an empty string when the application does
/// not run from a bundle (a development build, Linux): nothing is then
/// offered.
/// </param>
public sealed partial class BundleUpdateService(
    IReleaseSource source,
    IAppPaths paths,
    IProcessRunner runner,
    Version running,
    string bundlePath,
    ILogger<BundleUpdateService> logger)
{
    /// <summary>The release asset the Mac downloads.</summary>
    public const string Asset = "DtHub-mac.zip";

    /// <summary>The executable inside the bundle, the Mac project's assembly name.</summary>
    public const string Executable = "DtHub.Mac";

    /// <summary>The bundle the release archive holds, whatever the player renamed his copy to.</summary>
    public const string ArchivedBundle = "DT Hub.app";

    private const string Unpacked = "nouvelle";
    private const string Retired = "DT Hub.app.ancien";

    private readonly IReleaseSource _source = source;
    private readonly IAppPaths _paths = paths;
    private readonly IProcessRunner _runner = runner;
    private readonly string _bundle = bundlePath ?? string.Empty;
    private readonly ILogger<BundleUpdateService> _logger = logger;

    /// <summary>The version that is running.</summary>
    public Version Running { get; } = ReleaseParser.Normalize(running);

    /// <summary>The more recent release found, if there is one.</summary>
    public AppRelease? Available { get; private set; }

    /// <summary>True when this release is downloaded, verified and unpacked.</summary>
    public bool Ready { get; private set; }

    /// <summary>Raised when either of the previous two changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The bundle holding an executable, or an empty string when it does
    /// not sit in one or sits in a source tree, where a build would
    /// overwrite the update within the minute.
    /// </summary>
    public static string BundleOf(string? executablePath, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        // ".../DT Hub.app/Contents/MacOS/DtHub.Mac"
        var macOs = Path.GetDirectoryName(executablePath ?? string.Empty);
        var contents = Path.GetDirectoryName(macOs ?? string.Empty);
        var bundle = Path.GetDirectoryName(contents ?? string.Empty);

        return bundle is not null
            && Path.GetFileName(macOs) == "MacOS"
            && Path.GetFileName(contents) == "Contents"
            && bundle.EndsWith(".app", StringComparison.Ordinal)
            && UpdatePaths.CanReplace(executablePath, exists)
            ? bundle
            : string.Empty;
    }

    private string Folder => _paths.UpdatesDirectory;

    private string Archive(Version version) =>
        Path.Combine(Folder, $"DtHub-mac-{version.Major}.{version.Minor}.{version.Build}.zip");

    private string Incoming => Path.Combine(Folder, Unpacked);

    private string IncomingBundle => Path.Combine(Incoming, ArchivedBundle);

    /// <summary>
    /// Looks for a more recent release, and prepares it if asked to do so
    /// on its own.
    /// </summary>
    public async Task CheckAsync(bool automatic, CancellationToken cancellationToken = default)
    {
        if (_bundle.Length == 0)
        {
            return;
        }

        var latest = await _source.GetLatestAsync(cancellationToken).ConfigureAwait(true);

        if (latest is null || latest.Version <= Running)
        {
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

    private async Task<AppRelease> TranslatedAsync(AppRelease release, CancellationToken cancellationToken)
    {
        var language = Strings.Spoken.TwoLetterISOLanguageName;

        if (!release.NoteUrls.TryGetValue(language, out var url))
        {
            return release;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(UpdateService.NoteWait);

        try
        {
            var notes = (await _source.ReadAsync(url, deadline.Token).ConfigureAwait(true))
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();

            return notes.Length > 0 ? release with { Notes = notes } : release;
        }
        catch (Exception exception) when (exception is HttpRequestException
            or InvalidOperationException or UriFormatException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogTranslationUnread(language);

            return release;
        }
    }

    /// <summary>
    /// Downloads the release, verifies its digest, unpacks it and checks
    /// its signature. True if it is ready to be placed.
    /// </summary>
    public async Task<bool> PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release || _bundle.Length == 0)
        {
            return false;
        }

        var archive = Archive(release.Version);
        var part = archive + ".part";

        try
        {
            _ = Directory.CreateDirectory(Folder);

            if (!File.Exists(archive))
            {
                await _source
                    .DownloadAsync(release.DownloadUrl, part, progress: null, cancellationToken)
                    .ConfigureAwait(true);

                File.Move(part, archive, overwrite: true);
            }

            var expected = Digest(await _source.ReadAsync(release.DigestUrl, cancellationToken).ConfigureAwait(true));
            var actual = await Sha256Async(archive, cancellationToken).ConfigureAwait(true);

            if (expected.Length == 0 || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                LogDigestMismatch(release.Version.ToString());
                DeleteFile(archive);

                return false;
            }

            if (!await UnpackAsync(archive, release.Version, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            await File
                .WriteAllTextAsync(
                    UpdatePaths.Notes(Folder, release.Version),
                    ReleaseNotes.Readable(release.Notes),
                    cancellationToken)
                .ConfigureAwait(true);

            Ready = true;
            LogReady(release.Version.ToString());
            Changed?.Invoke(this, EventArgs.Empty);

            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or HttpRequestException or TaskCanceledException
            or ProcessLaunchException)
        {
            // The archive stays when it is whole: a digest file that did not
            // come, sixty-four bytes, must not cost the sixty megabytes again.
            // One that does not match its digest was deleted above.
            LogPrepareFailed(exception);
            DeleteFile(part);
            DeleteDirectory(Incoming);

            return false;
        }
    }

    /// <summary>
    /// Unpacks with ditto, as the archive was made, and keeps the bundle
    /// only if it holds an executable under our name and its signature
    /// holds: a bundle that would not start must not replace one that does.
    /// </summary>
    private async Task<bool> UnpackAsync(string archive, Version version, CancellationToken cancellationToken)
    {
        DeleteDirectory(Incoming);

        var unpack = await _runner
            .RunAsync(new ProcessRequest("/usr/bin/ditto", "-x", "-k", archive, Incoming) { Timeout = TimeSpan.FromMinutes(2) }, cancellationToken)
            .ConfigureAwait(true);

        var executable = Path.Combine(IncomingBundle, "Contents", "MacOS", Executable);

        if (unpack.ExitCode != 0 || !Directory.Exists(IncomingBundle) || !File.Exists(executable))
        {
            LogUnpackFailed(version.ToString(), unpack.StandardError.Trim());
            DeleteDirectory(Incoming);
            DeleteFile(archive);

            return false;
        }

        var check = await _runner
            .RunAsync(new ProcessRequest("/usr/bin/codesign", "--verify", "--deep", "--strict", IncomingBundle) { Timeout = TimeSpan.FromMinutes(2) }, cancellationToken)
            .ConfigureAwait(true);

        if (check.ExitCode != 0)
        {
            LogSignatureBroken(version.ToString(), check.StandardError.Trim());
            DeleteDirectory(Incoming);
            DeleteFile(archive);

            return false;
        }

        return await SameSignerAsync(version, archive, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// A running copy signed with a certificate only gives way to a bundle
    /// signed by the same one: its designated requirement, "this identifier
    /// and this certificate", must hold for the new bundle. An ad hoc copy
    /// has nothing to compare, its requirement being its own digest, which
    /// no other version can meet.
    /// </summary>
    private async Task<bool> SameSignerAsync(Version version, string archive, CancellationToken cancellationToken)
    {
        var own = await _runner
            .RunAsync(new ProcessRequest("/usr/bin/codesign", "--display", "--requirements", "-", _bundle) { Timeout = TimeSpan.FromMinutes(1) }, cancellationToken)
            .ConfigureAwait(true);

        // "designated => identifier "…" and certificate leaf = H"…"",
        // on standard output or error depending on the macOS version.
        var designated = (own.StandardOutput + "\n" + own.StandardError)
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("designated => ", StringComparison.Ordinal))?
            ["designated => ".Length..];

        if (own.ExitCode != 0 || designated is null || !designated.Contains("certificate", StringComparison.Ordinal))
        {
            return true;
        }

        var check = await _runner
            .RunAsync(new ProcessRequest("/usr/bin/codesign", "--verify", "-R=" + designated, IncomingBundle) { Timeout = TimeSpan.FromMinutes(1) }, cancellationToken)
            .ConfigureAwait(true);

        if (check.ExitCode == 0)
        {
            return true;
        }

        LogOtherSigner(version.ToString(), check.StandardError.Trim());
        DeleteDirectory(Incoming);
        DeleteFile(archive);

        return false;
    }

    /// <summary>
    /// Places the prepared update, at shutdown. True if the bundle was
    /// indeed replaced.
    /// </summary>
    /// <param name="automatic">The box as it stands at shutdown.</param>
    public bool Apply(bool automatic)
    {
        if (!automatic || !Ready || Available is not { } release
            || _bundle.Length == 0 || !Directory.Exists(IncomingBundle))
        {
            return false;
        }

        var retired = Path.Combine(Folder, Retired);

        try
        {
            DeleteDirectory(retired);

            // A rename: the updates folder sits on the same disk as the
            // Applications folder on any Mac set up as it ships. On
            // another disk the move throws before anything is touched.
            Directory.Move(_bundle, retired);

            try
            {
                Directory.Move(IncomingBundle, _bundle);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                PutBack(retired);

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
    /// The old bundle back in its place. Refused twice, a copy still leaves
    /// something to start; the retired one then stays where it is, and the
    /// sweep leaves it alone while the bundle is missing.
    /// </summary>
    private void PutBack(string retired)
    {
        try
        {
            Directory.Move(retired, _bundle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogPutBackFailed(exception);
            DeleteDirectory(_bundle);

            try
            {
                Copy(retired, _bundle);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // A half copy would pass for the application, and the sweep
                // would then throw the whole one away.
                DeleteDirectory(_bundle);

                throw;
            }
        }
    }

    /// <summary>A bundle copied whole, its links as links: a framework's are part of its signature.</summary>
    private static void Copy(string from, string to)
    {
        _ = Directory.CreateDirectory(to);

        foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
        {
            var target = Path.Combine(to, entry.Name);

            if (entry.LinkTarget is { } link)
            {
                _ = File.CreateSymbolicLink(target, link);
            }
            else if (entry is DirectoryInfo directory)
            {
                Copy(directory.FullName, target);
            }
            else
            {
                var file = (FileInfo)entry;
                file.CopyTo(target);
                File.SetUnixFileMode(target, File.GetUnixFileMode(file.FullName));
            }
        }
    }

    /// <summary>
    /// What the version just placed announces, read once, or an empty
    /// string.
    /// </summary>
    public string TakeNotes()
    {
        var file = UpdatePaths.Notes(Folder, Running);

        try
        {
            if (!File.Exists(file))
            {
                return string.Empty;
            }

            var notes = File.ReadAllText(file);
            DeleteFile(file);

            return notes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Silence assumed: the notes are a nicety.
            return string.Empty;
        }
    }

    /// <summary>
    /// Sweeps away what the previous update left: the old bundle, an
    /// unpacked one never placed, archives no newer than the one running,
    /// downloads cut short.
    /// </summary>
    public void Sweep()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            // Never the last copy: a bundle missing means the old one is
            // all there is.
            if (_bundle.Length == 0 || Directory.Exists(_bundle))
            {
                DeleteDirectory(Path.Combine(Folder, Retired));
            }

            DeleteDirectory(Incoming);

            foreach (var file in Directory.EnumerateFiles(Folder, "DtHub-mac-*.zip"))
            {
                var version = ReleaseParser.VersionOf(
                    Path.GetFileNameWithoutExtension(file).Replace("DtHub-mac-", string.Empty, StringComparison.Ordinal));

                if (version is null || version <= Running)
                {
                    DeleteFile(file);
                }
            }

            foreach (var file in Directory.EnumerateFiles(Folder, "DtHub-mac-*.part"))
            {
                DeleteFile(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogSweepFailed(exception);
        }
    }

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

        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static void DeleteFile(string path)
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
            // Picked up at the next sweep.
            _ = exception;
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Picked up at the next sweep.
            _ = exception;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} disponible, la version en cours est {running}.")]
    private partial void LogFound(string version, string running);

    [LoggerMessage(Level = LogLevel.Information, Message = "Note de version en {language} illisible, l'anglais reste.")]
    private partial void LogTranslationUnread(string language);

    [LoggerMessage(Level = LogLevel.Information, Message = "Livraison {version} téléchargée et vérifiée.")]
    private partial void LogReady(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "L'empreinte de la livraison {version} ne correspond pas.")]
    private partial void LogDigestMismatch(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison {version} n'a pas pu être décompressée : {detail}")]
    private partial void LogUnpackFailed(string version, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La signature de la livraison {version} ne tient pas : {detail}")]
    private partial void LogSignatureBroken(string version, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison {version} n'est pas signée par le même certificat que l'application en place : {detail}")]
    private partial void LogOtherSigner(string version, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "L'ancienne application n'a pas pu reprendre sa place, une copie la remplace.")]
    private partial void LogPutBackFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La livraison n'a pas pu être préparée.")]
    private partial void LogPrepareFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Version {version} posée, elle démarrera au prochain lancement.")]
    private partial void LogApplied(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La mise à jour n'a pas pu être posée.")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Le ménage des mises à jour a échoué.")]
    private partial void LogSweepFailed(Exception exception);
}
