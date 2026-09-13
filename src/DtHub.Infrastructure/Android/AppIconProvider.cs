using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Android;

/// <summary>
/// Extracts an application's icon from its archive on the phone,
/// and keeps it.
///
/// Three commands are enough, and none of them transfers the
/// archive: <c>pm path</c> says where it is, <c>unzip -l</c> says
/// what it contains, and <c>exec-out unzip -p</c> pulls out just
/// the one entry we want. Measured on the targeted game, seventeen
/// kilobytes pass through for a fourteen-megabyte archive that,
/// itself, never moves.
///
/// None of this is essential, and that is what dictates the shape:
/// every failure silently returns <c>null</c>.
/// </summary>
public sealed partial class AppIconProvider : IAppIconProvider
{
    private readonly IAdbClient _adb;
    private readonly IAppPaths _paths;
    private readonly ILogger<AppIconProvider> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<string?>> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset?> _absent = new(StringComparer.Ordinal);

    public AppIconProvider(IAdbClient adb, IAppPaths paths, ILogger<AppIconProvider> logger)
    {
        _adb = adb;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Delay before retrying an extraction that failed for a
    /// transient reason. A phone in the middle of reconnecting
    /// should not doom its icon until the next launch.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The time allotted to each command. The archive is read, not
    /// transferred.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public string? Find(string deviceId, string packageName)
    {
        var key = Key(deviceId, packageName);

        lock (_gate)
        {
            if (_known.TryGetValue(key, out var path))
            {
                return path;
            }
        }

        var file = FileFor(key);

        if (!File.Exists(file))
        {
            return null;
        }

        lock (_gate)
        {
            _known[key] = file;
        }

        return file;
    }

    public Task<string?> GetAsync(AppIconRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId)
            || string.IsNullOrWhiteSpace(request.Serial)
            || string.IsNullOrWhiteSpace(request.PackageName))
        {
            return Task.FromResult<string?>(null);
        }

        if (Find(request.DeviceId, request.PackageName) is { } known)
        {
            return Task.FromResult<string?>(known);
        }

        var key = Key(request.DeviceId, request.PackageName);

        lock (_gate)
        {
            // A permanent absence is not retried: the phone will
            // not change its mind about an archive with no raster
            // icon.
            if (_absent.TryGetValue(key, out var since))
            {
                if (since is null || DateTimeOffset.UtcNow - since.Value < RetryDelay)
                {
                    return Task.FromResult<string?>(null);
                }

                _absent.Remove(key);
            }

            // Two profiles on the same phone request the same icon
            // at the same instant: only one extraction runs, and
            // both wait on it.
            if (_running.TryGetValue(key, out var running))
            {
                return running;
            }

            var work = ExtractAsync(request, key, cancellationToken);
            _running[key] = work;

            return work;
        }
    }

    private async Task<string?> ExtractAsync(
        AppIconRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = await ExtractCoreAsync(request, key, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (path is not null)
                {
                    _known[key] = path;
                }
            }

            return path;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Including a cancellation: the caller is not awaiting
            // it, and nothing should escape from a task nobody is
            // watching.
            LogFailed(request.PackageName, exception.Message);

            lock (_gate)
            {
                _absent[key] = DateTimeOffset.UtcNow;
            }

            return null;
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(key);
            }
        }
    }

    private async Task<string?> ExtractCoreAsync(
        AppIconRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var user = request.UserId.ToString(CultureInfo.InvariantCulture);

        var paths = ApkListing.ParsePaths(await _adb.ShellAsync(
            request.Serial,
            ["pm", "path", "--user", user, request.PackageName],
            Timeout,
            cancellationToken).ConfigureAwait(false));

        if (paths.Count == 0)
        {
            Forget(key, "le paquet n'est pas installé sur ce profil", request.PackageName);

            return null;
        }

        foreach (var apk in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var quoted = AndroidShell.Quote(apk);

            // The listing is restricted to the icon folders: a
            // fourteen-megabyte archive holds thousands of entries,
            // and shipping all of them across just to keep six
            // would be absurd.
            var listing = await _adb.ShellAsync(
                request.Serial,
                ["unzip", "-l", quoted, "'res/mipmap*'"],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            if (LauncherIconChoice.Choose(ApkListing.ParseEntries(listing)) is not { } entry)
            {
                continue;
            }

            // Unquoted, unlike the listing just above, and that is
            // not an oversight. "adb shell" passes the command
            // through a shell that strips quotes; "adb exec-out"
            // hands the arguments back as they are, so an added
            // quote mark becomes part of the file name. Measured:
            // quoted, the archive returned "couldn't open ... : I/O
            // error"; bare, it returns its fifty-one kilobytes.
            // This is also safer, since nothing gets reinterpreted.
            var bytes = await _adb.ExecOutAsync(
                request.Serial,
                ["unzip", "-p", apk, entry],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            if (!bytes.Succeeded || !IsPng(bytes.StandardOutput))
            {
                continue;
            }

            var file = Write(key, bytes.StandardOutput);

            if (file is not null)
            {
                LogExtracted(request.PackageName, entry, bytes.StandardOutput.Length);
            }

            return file;
        }

        Forget(key, "aucune icône matricielle dans l'archive", request.PackageName);

        return null;
    }

    /// <summary>
    /// Remembers a permanent absence: neither the package nor its
    /// archive will change while the application is running.
    /// </summary>
    private void Forget(string key, string reason, string packageName)
    {
        LogAbsent(packageName, reason);

        lock (_gate)
        {
            _absent[key] = null;
        }
    }

    /// <summary>
    /// A PNG and nothing else. Without this check, the error
    /// message an <c>unzip</c> writes to its standard output would
    /// end up in a file named ".png" that the display would then
    /// silently refuse.
    /// </summary>
    private static bool IsPng(byte[] content) =>
        content.Length > 8
        && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47
        && content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A;

    private string? Write(string key, byte[] content)
    {
        try
        {
            var file = FileFor(key);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, content);

            return file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(key, exception.Message);

            return null;
        }
    }

    private string FileFor(string key) =>
        Path.Combine(_paths.CacheDirectory, "icons", "apps", Readable(key) + ".png");

    private static string Key(string deviceId, string packageName) =>
        deviceId + "|" + packageName;

    /// <summary>
    /// A readable file name, followed by a hash of the exact key.
    ///
    /// Readable so that you know what it is when opening the
    /// folder; followed by a hash because the identity of a
    /// wireless device that has never answered is its address,
    /// "192.168.1.25:5555", whose colons are forbidden in a
    /// Windows file name. Replacing them would make two
    /// neighboring devices indistinguishable; the hash keeps them
    /// apart.
    /// </summary>
    private static string Readable(string key)
    {
        var cleaned = new StringBuilder(key.Length);

        foreach (var character in key)
        {
            cleaned.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '_');
        }

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

        var head = cleaned.ToString();

        return (head.Length > 60 ? head[^60..] : head) + "-" + hash;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Icône de {package} extraite de {entry} : {bytes} octets.")]
    private partial void LogExtracted(string package, string entry, int bytes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pas d'icône pour {package} : {reason}.")]
    private partial void LogAbsent(string package, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icône de {package} non extraite : {reason}")]
    private partial void LogFailed(string package, string reason);
}
