using DtHub.Core.Dependencies;
using DtHub.Infrastructure.Dependencies;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's ToolPreparation (src/DtHub.App/Services/ToolPreparation.cs)
/// with the manifest's macOS archives: the PC's asks for the Windows keys.
///
/// What counts as missing is DT Hub's own copy, not any copy: a player
/// with Homebrew's adb still gets the pinned one, and Homebrew's only
/// serves when that download cannot be done (see ProvisionedTool).
/// </summary>
internal sealed class MacToolPreparation
{
    private readonly IDependencyProvisioner _provisioner;

    public MacToolPreparation(IDependencyProvisioner provisioner) => _provisioner = provisioner;

    /// <summary>The missing components, adb first as on the PC.</summary>
    public IReadOnlyList<ExternalDependency> Missing() =>
    [
        .. new[] { DependencyManifest.PlatformToolsMacKey, DependencyManifest.ScrcpyMacKey }
            .Select(DependencyManifest.Get)
            .Where(dependency => _provisioner.TryGetExistingPath(dependency) is null),
    ];

    /// <summary>Sets up a component, reporting its progress.</summary>
    /// <exception cref="DependencyProvisioningException">
    /// Download impossible, hash mismatch, or extraction failed.
    /// </exception>
    public Task<string> InstallAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken = default) =>
        _provisioner.EnsureAvailableAsync(dependency, progress, cancellationToken);
}
