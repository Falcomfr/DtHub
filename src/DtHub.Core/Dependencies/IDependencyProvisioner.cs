namespace DtHub.Core.Dependencies;

/// <summary>Current stage, to inform the user while waiting.</summary>
public enum ProvisioningStage
{
    Downloading,
    Verifying,
    Extracting,
    Done,
}

/// <summary>Progress of a setup, meant for the interface.</summary>
public sealed record ProvisioningProgress(
    ProvisioningStage Stage,
    long BytesReceived = 0,
    long? TotalBytes = null)
{
    /// <summary>
    /// Downloaded fraction, or <c>null</c> if the size is unknown.
    /// </summary>
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;
}

/// <summary>
/// Sets up a third-party component in the user's data folder:
/// download from the official source, hash verification, then
/// extraction.
/// </summary>
public interface IDependencyProvisioner
{
    /// <summary>
    /// Returns the absolute path of the component's executable,
    /// downloading it if it is not already in place.
    /// </summary>
    /// <exception cref="DependencyProvisioningException">
    /// Download impossible, hash mismatch, or extraction failed.
    /// </exception>
    Task<string> EnsureAvailableAsync(
        ExternalDependency dependency,
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executable path if it is already in place, without
    /// downloading anything.
    /// </summary>
    string? TryGetExistingPath(ExternalDependency dependency);
}
