using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Infrastructure.Adb;

/// <summary>
/// Détermine le chemin absolu d'ADB : celui de la copie installée par DT Hub,
/// téléchargée à la demande. Le PATH n'est jamais consulté, et aucun chemin
/// choisi ailleurs n'est accepté.
///
/// C'est la règle de D4, prise au mot. Le code portait une branche pour un
/// chemin imposé dans les réglages, mais ce réglage n'a jamais existé : rien
/// ne le posait, aucune fenêtre ne le demandait, et la branche n'a jamais
/// tourné. Elle promettait surtout ce que la décision écarte, dépendre d'une
/// installation tierce, pour huit mégaoctets épargnés une fois. Le code lit la
/// sortie d'adb pour trouver profils, afficheurs et paquets : une version
/// qu'on ne maîtrise pas ne casse pas bruyamment, elle rend une sortie un peu
/// différente que l'analyse interprète de travers.
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
