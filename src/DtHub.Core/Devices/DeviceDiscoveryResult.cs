namespace DtHub.Core.Devices;

/// <summary>
/// Result of a scan. Warnings carry the non-blocking incidents: a
/// device whose properties could not be read still appears, with
/// what is already known about it.
/// </summary>
public sealed record DeviceDiscoveryResult(
    IReadOnlyList<AndroidDevice> Devices,
    IReadOnlyList<string> Warnings)
{
    public static readonly DeviceDiscoveryResult Empty = new([], []);

    public IEnumerable<AndroidDevice> Connected => Devices.Where(d => d.IsConnected);

    /// <summary>
    /// True when adb did not answer: the devices are then the remembered
    /// ones, not what is plugged in.
    /// </summary>
    public bool AdbFailed { get; init; }
}
