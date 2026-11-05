using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Scrcpy;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Dependencies;

/// <summary>
/// The macOS locator: DT Hub's own copy of a tool, downloaded from the
/// manifest on first need like the PC's, and the player's installed copy
/// only when that download cannot be done (a first launch offline, with
/// Homebrew's tools already there).
///
/// The PC's AdbLocator and ScrcpyLocator stay as they are: they apply the
/// Windows-only SDL fix and never fall back, by D4's rule. On a Mac an
/// application opened from the Finder has no shell PATH and most players
/// have no Homebrew, so the download comes first here too; the fallback
/// is a way out, not a second source of truth.
///
/// Whatever is found first is kept for the session: switching from one
/// adb to another midway would make the two fight over the ADB server.
/// </summary>
public sealed partial class ProvisionedTool : IAdbLocator, IScrcpyLocator, IDisposable
{
    private readonly IDependencyProvisioner _provisioner;
    private readonly InstalledTool? _fallback;
    private readonly ILogger<ProvisionedTool> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _resolved;
    private bool _fellBack;

    public ProvisionedTool(
        IDependencyProvisioner provisioner,
        ExternalDependency dependency,
        InstalledTool? fallback,
        ILogger<ProvisionedTool> logger)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        _provisioner = provisioner;
        Dependency = dependency;
        _fallback = fallback;
        _logger = logger;
    }

    /// <summary>The manifest entry this locator sets up.</summary>
    public ExternalDependency Dependency { get; }

    /// <summary>The pinned version, or the installed copy's when it is the one in use.</summary>
    public string Version => _fellBack && _fallback is not null ? _fallback.Version : Dependency.Version;

    /// <summary>
    /// DT Hub's copy if it is in place, otherwise the installed one:
    /// whichever <see cref="ResolveAsync" /> would use without the network.
    /// </summary>
    public string? TryGetInstalledPath() =>
        _resolved
        ?? _provisioner.TryGetExistingPath(Dependency)
        ?? _fallback?.TryGetInstalledPath();

    public async Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ResolveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DependencyProvisioningException exception)
        {
            throw new AdbException(
                AdbErrorKind.AdbUnavailable,
                exception.Message,
                $"Mise en place de {Dependency.DisplayName} {Dependency.Version}",
                exception);
        }
    }

    public Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default) =>
        ResolveAsync(cancellationToken);

    /// <summary>
    /// The tool's path: DT Hub's copy, downloaded if need be, or the
    /// installed one when the download fails.
    /// </summary>
    /// <exception cref="DependencyProvisioningException">
    /// The download failed and no installed copy exists.
    /// </exception>
    public async Task<string> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_resolved is not null)
            {
                return _resolved;
            }

            try
            {
                _resolved = await _provisioner
                    .EnsureAvailableAsync(Dependency, progress: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DependencyProvisioningException exception)
                when (_fallback?.TryGetInstalledPath() is not null)
            {
                _resolved = _fallback.TryGetInstalledPath()!;
                _fellBack = true;
                LogFallback(Dependency.DisplayName, _resolved, exception.Message);
            }

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Mise en place de {name} impossible, repli sur la copie installée {path} : {reason}")]
    private partial void LogFallback(string name, string path, string reason);
}
