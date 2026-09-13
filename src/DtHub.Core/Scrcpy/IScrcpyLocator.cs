namespace DtHub.Core.Scrcpy;

/// <summary>
/// Provides the absolute path of the scrcpy executable used by DT
/// Hub, setting it up if necessary.
/// </summary>
public interface IScrcpyLocator
{
    /// <exception cref="Dependencies.DependencyProvisioningException">
    /// scrcpy is unavailable and could not be installed.
    /// </exception>
    Task<string> GetScrcpyPathAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// scrcpy's path if it is already in place, without
    /// downloading anything. Used at startup: asking for the plain
    /// path used to trigger the setup of eleven megabytes before
    /// the first window, for a cleanup of leftover windows that by
    /// construction has nothing to clean up as long as scrcpy has
    /// never run.
    /// </summary>
    string? TryGetInstalledPath();

    /// <summary>Installed version, for the diagnostics page.</summary>
    string Version { get; }
}
