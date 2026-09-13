namespace DtHub.Core.Devices;

/// <summary>
/// Memory of devices between two launches: custom names, last
/// known address, primary device. Never contains a pairing code.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>Remembered devices, connected or not.</summary>
    Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a device, keeping the user's choices.
    /// </summary>
    Task UpsertAsync(AndroidDevice device, CancellationToken cancellationToken = default);

    /// <summary>Adds or updates several devices in a single write.</summary>
    Task UpsertRangeAsync(IEnumerable<AndroidDevice> devices, CancellationToken cancellationToken = default);

    /// <summary>Forgets a device and everything about it.</summary>
    Task ForgetAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the device and remembers that it is no longer
    /// wanted: it will neither be re-registered by a scan, nor
    /// picked up again by automatic reconnection.
    /// </summary>
    Task DiscardAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Lifts the discard, which only a new pairing does.</summary>
    Task WelcomeBackAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The remembered devices and the discarded ones, in a single
    /// read. This is what a scan asks for, since it needs both.
    /// </summary>
    Task<DeviceRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a device. An empty name restores the detected name.
    /// </summary>
    Task RenameAsync(string deviceId, string? customName, CancellationToken cancellationToken = default);

    /// <summary>Designates the primary device. Only one at a time.</summary>
    Task SetPrimaryAsync(string deviceId, CancellationToken cancellationToken = default);
}
