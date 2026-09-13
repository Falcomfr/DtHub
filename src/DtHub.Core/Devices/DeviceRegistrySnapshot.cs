namespace DtHub.Core.Devices;

/// <summary>
/// What the registry knows, rendered in a single read: the
/// remembered devices and those whose pairing has been broken.
///
/// Both live in the same file, and the registry rereads it on every
/// request. Requesting them separately therefore made it read
/// twice per scan, for nothing.
/// </summary>
/// <param name="Known">Remembered devices, connected or not.</param>
/// <param name="Discarded">Identifiers of discarded devices.</param>
public readonly record struct DeviceRegistrySnapshot(
    IReadOnlyList<AndroidDevice> Known,
    IReadOnlySet<string> Discarded);
