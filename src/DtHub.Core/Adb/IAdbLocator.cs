namespace DtHub.Core.Adb;

/// <summary>
/// Provides the absolute path of the ADB executable used by DT Hub. PATH
/// is never consulted: we do not want to depend on any third-party
/// installation, nor risk an incompatible version.
/// </summary>
public interface IAdbLocator
{
    /// <summary>
    /// Returns the absolute path of ADB, setting it up if necessary.
    /// </summary>
    /// <exception cref="AdbException">
    /// ADB is unavailable and could not be installed.
    /// </exception>
    Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Path to ADB if it is already in place, without downloading
    /// anything: the path forced in settings if it still exists,
    /// otherwise DT Hub's own copy. Used to know, at startup, whether
    /// something needs to be set up before opening the first window.
    /// </summary>
    string? TryGetInstalledPath();
}
