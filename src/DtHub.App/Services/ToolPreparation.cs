using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Scrcpy;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.App.Services;

/// <summary>
/// Sait quels composants tiers manquent, et les met en place.
///
/// Ils arrivaient jusqu'ici par accident : le ramassage des fenêtres restées
/// demandait le chemin de scrcpy, et le lancement des instances demandait celui
/// d'ADB. Dix-neuf mégaoctets se téléchargeaient donc avant la première
/// fenêtre, sans que rien ne l'annonce, avec un délai réseau réglé à dix
/// minutes. Le premier lancement passe désormais par ici, où la mise en place
/// est demandée pour elle-même et peut se montrer.
/// </summary>
public sealed class ToolPreparation
{
    private readonly IDependencyProvisioner _provisioner;
    private readonly IAdbLocator _adb;
    private readonly IScrcpyLocator _scrcpy;

    public ToolPreparation(
        IDependencyProvisioner provisioner,
        IAdbLocator adb,
        IScrcpyLocator scrcpy)
    {
        _provisioner = provisioner;
        _adb = adb;
        _scrcpy = scrcpy;
    }

    /// <summary>
    /// Les composants absents, dans l'ordre où ils serviront. Liste vide à tous
    /// les lancements sauf le premier, et alors rien ne doit s'afficher.
    /// </summary>
    public IReadOnlyList<ExternalDependency> Missing()
    {
        var missing = new List<ExternalDependency>(2);

        if (_adb.TryGetInstalledPath() is null)
        {
            missing.Add(DependencyManifest.Get(DependencyManifest.PlatformToolsKey));
        }

        if (_scrcpy.TryGetInstalledPath() is null)
        {
            missing.Add(DependencyManifest.Get(DependencyManifest.ScrcpyKey));
        }

        return missing;
    }

    /// <summary>
    /// Met un composant en place. Le provisionneur est appelé directement
    /// plutôt que par les localisateurs : eux ne savent pas rendre compte de
    /// l'avancement, et ils retrouveront le fichier posé sans rien retélécharger.
    /// </summary>
    /// <exception cref="DependencyProvisioningException">
    /// Téléchargement impossible, empreinte non conforme, ou extraction en échec.
    /// </exception>
    public Task<string> InstallAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken = default) =>
        _provisioner.EnsureAvailableAsync(dependency, progress, cancellationToken);
}
