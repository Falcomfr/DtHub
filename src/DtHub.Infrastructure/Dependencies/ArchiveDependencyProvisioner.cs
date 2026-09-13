using System.IO.Compression;
using System.Security.Cryptography;

using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// Downloads an archive from the official source, checks its size
/// and its SHA-256 hash, then extracts it into the user's data
/// folder. Nothing is executed before the hash matches.
/// </summary>
public sealed partial class ArchiveDependencyProvisioner : IDependencyProvisioner
{
    private readonly HttpClient _httpClient;
    private readonly IAppPaths _paths;
    private readonly ILogger<ArchiveDependencyProvisioner> _logger;

    public ArchiveDependencyProvisioner(
        HttpClient httpClient,
        IAppPaths paths,
        ILogger<ArchiveDependencyProvisioner> logger)
    {
        _httpClient = httpClient;
        _paths = paths;
        _logger = logger;
    }

    public string? TryGetExistingPath(ExternalDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        var path = ExecutablePath(dependency);
        return File.Exists(path) ? path : null;
    }

    public async Task<string> EnsureAvailableAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        if (TryGetExistingPath(dependency) is { } existing)
        {
            return existing;
        }

        // Only HTTPS is accepted: a redirect to plain text must make
        // the setup fail rather than pass unnoticed.
        if (!string.Equals(dependency.Url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format("DependencyUrlNotSecure", dependency.DisplayName));
        }

        var installRoot = Path.Combine(_paths.ToolsDirectory, dependency.InstallDirectoryName);

        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"dthub-{dependency.Key}-{Guid.NewGuid():N}.zip");

        try
        {
            // Inside the try, and not before: a data folder that is
            // not writable, on a machine controlled by a group
            // policy or a restricted roaming profile, used to come
            // out as a raw exception that went through every safety
            // net and closed the application.
            _paths.EnsureCreated();

            await DownloadAsync(dependency, archivePath, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ProvisioningProgress(ProvisioningStage.Verifying));
            await VerifyAsync(dependency, archivePath, cancellationToken).ConfigureAwait(false);

            progress?.Report(new ProvisioningProgress(ProvisioningStage.Extracting));
            Extract(dependency, archivePath, installRoot);
        }
        catch (Exception exception) when (exception is not DependencyProvisioningException
                                          and not OperationCanceledException)
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format("DependencyInstallFailed", dependency.DisplayName, exception.Message),
                exception);
        }
        finally
        {
            DeleteQuietly(archivePath);
        }

        var executable = ExecutablePath(dependency);
        if (!File.Exists(executable))
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format(
                    "DependencyExecutableMissing", dependency.DisplayName, dependency.Executable));
        }

        progress?.Report(new ProvisioningProgress(ProvisioningStage.Done));
        LogInstalled(dependency.DisplayName, dependency.Version, installRoot);

        return executable;
    }

    private string ExecutablePath(ExternalDependency dependency)
    {
        var installRoot = Path.Combine(_paths.ToolsDirectory, dependency.InstallDirectoryName);

        return string.IsNullOrEmpty(dependency.ArchiveRootDirectory)
            ? Path.Combine(installRoot, dependency.Executable)
            : Path.Combine(installRoot, dependency.ArchiveRootDirectory, dependency.Executable);
    }

    private async Task DownloadAsync(
        ExternalDependency dependency,
        string archivePath,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken)
    {
        LogDownloading(dependency.DisplayName, dependency.Url.ToString());

        using var response = await _httpClient
            .GetAsync(dependency.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format(
                    "DependencyDownloadFailed", dependency.DisplayName, (int)response.StatusCode));
        }

        var total = response.Content.Headers.ContentLength ?? dependency.SizeBytes;
        progress?.Report(new ProvisioningProgress(ProvisioningStage.Downloading, 0, total));

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            archivePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        var lastReported = 0L;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            // Notifications are not sent for every block: the
            // interface gains nothing from receiving hundreds of
            // events per second.
            if (received - lastReported >= 256 * 1024)
            {
                lastReported = received;
                progress?.Report(new ProvisioningProgress(ProvisioningStage.Downloading, received, total));
            }
        }

        progress?.Report(new ProvisioningProgress(ProvisioningStage.Downloading, received, total));
    }

    private static async Task VerifyAsync(
        ExternalDependency dependency,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var actualSize = new FileInfo(archivePath).Length;
        if (dependency.SizeBytes > 0 && actualSize != dependency.SizeBytes)
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format("DependencyWrongSize", dependency.DisplayName));
        }

        await using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actual = Convert.ToHexStringLower(hash);

        if (!string.Equals(actual, dependency.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new DependencyProvisioningException(
                dependency.Key,
                Strings.Format("DependencyWrongHash", dependency.DisplayName));
        }
    }

    private static void Extract(ExternalDependency dependency, string archivePath, string installRoot)
    {
        // Extraction into a neighboring folder then swap: an
        // interrupted extraction never leaves a half-done
        // installation.
        var staging = installRoot + ".partiel";

        DeleteDirectoryQuietly(staging);
        Directory.CreateDirectory(staging);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, staging, overwriteFiles: true);

            DeleteDirectoryQuietly(installRoot);
            Directory.Move(staging, installRoot);
        }
        catch
        {
            DeleteDirectoryQuietly(staging);
            throw;
        }

        _ = dependency;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Temporary file still locked: the system will clean it up.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file in the folder is in use; the caller will
            // handle the failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Téléchargement de {name} depuis {url}.")]
    private partial void LogDownloading(string name, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "{name} {version} installé dans {path}.")]
    private partial void LogInstalled(string name, string version, string path);
}
