using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Infrastructure.Adb;

/// <summary>
/// Determines ADB's absolute path: that of the copy installed by
/// DT Hub, downloaded on demand. PATH is never consulted, and no
/// path chosen elsewhere is accepted.
///
/// This is D4's rule, taken literally. The code carried a branch
/// for a path forced in the settings, but that setting never
/// existed: nothing set it, no window asked for it, and the branch
/// never ran. It mostly promised exactly what the decision rules
/// out, depending on a third-party installation, to save eight
/// megabytes once. The code reads adb's output to find profiles,
/// displays and packages: a version that is not under our control
/// does not break loudly, it returns slightly different output
/// that the parsing misreads.
/// </summary>
public sealed class AdbLocator : IAdbLocator, IDisposable
{
    private readonly IDependencyProvisioner _provisioner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _resolved;

    public AdbLocator(IDependencyProvisioner provisioner) => _provisioner = provisioner;

    public string? TryGetInstalledPath() =>
        _resolved ?? _provisioner.TryGetExistingPath(
            DependencyManifest.Get(DependencyManifest.PlatformToolsKey));

    public async Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default)
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

            var dependency = DependencyManifest.Get(DependencyManifest.PlatformToolsKey);

            try
            {
                _resolved = await _provisioner
                    .EnsureAvailableAsync(dependency, progress: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DependencyProvisioningException exception)
            {
                throw new AdbException(
                    AdbErrorKind.AdbUnavailable,
                    exception.Message,
                    $"Mise en place de {dependency.DisplayName} {dependency.Version}",
                    exception);
            }

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
