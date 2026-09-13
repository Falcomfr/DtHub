using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Scrcpy;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.App.Services;

/// <summary>
/// Knows which third-party components are missing, and sets them
/// up.
///
/// Until now they arrived by accident: cleaning up leftover
/// windows needed scrcpy's path, and launching instances needed
/// ADB's. Nineteen megabytes were therefore downloaded before the
/// first window, with nothing announcing it, with a network
/// timeout set to ten minutes. The first launch now goes through
/// here, where the setup is requested for its own sake and can be
/// shown.
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
    /// The missing components, in the order they will be used.
    /// Empty list on every launch except the first, and then
    /// nothing should be displayed.
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
    /// Sets up a component. The provisioner is called directly
    /// rather than through the locators: they cannot report
    /// progress, and they will find the file already in place
    /// without downloading it again.
    /// </summary>
    /// <exception cref="DependencyProvisioningException">
    /// Download impossible, hash mismatch, or extraction failed.
    /// </exception>
    public Task<string> InstallAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken = default) =>
        _provisioner.EnsureAvailableAsync(dependency, progress, cancellationToken);
}
