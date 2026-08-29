using DtHub.Core.Dependencies;
using DtHub.Core.Scrcpy;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Infrastructure.Scrcpy;

/// <summary>
/// Fournit le chemin de scrcpy, en le téléchargeant depuis l'archive
/// officielle du projet au premier besoin.
/// </summary>
public sealed class ScrcpyLocator : IScrcpyLocator, IDisposable
{
    /// <summary>Clé de la dépendance dans <c>build/dependencies.json</c>.</summary>
    public const string DependencyKey = "scrcpy";

    private readonly IDependencyProvisioner _provisioner;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _resolved;

    public ScrcpyLocator(IDependencyProvisioner provisioner) => _provisioner = provisioner;

    /// <summary>Avancement de l'installation, à brancher sur l'interface.</summary>
    public IProgress<ProvisioningProgress>? Progress { get; set; }

    public string Version => DependencyManifest.Get(DependencyKey).Version;

    public async Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _resolved ??= await _provisioner
                .EnsureAvailableAsync(DependencyManifest.Get(DependencyKey), Progress, cancellationToken)
                .ConfigureAwait(false);

            return _resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
