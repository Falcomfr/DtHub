using DtHub.Core.Devices;

namespace DtHub.Tests.Fakes;

/// <summary>
/// In-memory registry, with the same semantics as the one on disk.
/// </summary>
public sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<string, AndroidDevice> _devices = new(StringComparer.Ordinal);

    public InMemoryDeviceRegistry(params AndroidDevice[] devices)
    {
        foreach (var device in devices)
        {
            _devices[device.Id] = device;
        }
    }

    public int UpsertCallCount { get; private set; }

    /// <summary>
    /// Number of reads of the registry. The real registry rereads its
    /// file on every request: this counter exists so that a scan never
    /// asks for more reads than it needs.
    /// </summary>
    public int Reads { get; private set; }

    public Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default)
    {
        Reads++;

        return Task.FromResult<IReadOnlyList<AndroidDevice>>([.. _devices.Values]);
    }

    public Task UpsertAsync(AndroidDevice device, CancellationToken cancellationToken = default) =>
        UpsertRangeAsync([device], cancellationToken);

    public Task UpsertRangeAsync(IEnumerable<AndroidDevice> devices, CancellationToken cancellationToken = default)
    {
        UpsertCallCount++;

        foreach (var device in devices)
        {
            _devices.TryGetValue(device.Id, out var previous);

            _devices[device.Id] = device with
            {
                CustomName = device.CustomName ?? previous?.CustomName,
                IsPrimary = device.IsPrimary || (previous?.IsPrimary ?? false),
                IsPaired = device.IsPaired || (previous?.IsPaired ?? false),
            };
        }

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _devices.Remove(deviceId);
        return Task.CompletedTask;
    }

    /// <summary>Devices whose pairing has been broken.</summary>
    public HashSet<string> Discarded { get; } = new(StringComparer.Ordinal);

    public Task DiscardAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _ = _devices.Remove(deviceId);
        _ = Discarded.Add(deviceId);

        return Task.CompletedTask;
    }

    public Task WelcomeBackAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        _ = Discarded.Remove(deviceId);

        return Task.CompletedTask;
    }

    public Task<DeviceRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        Reads++;

        return Task.FromResult(new DeviceRegistrySnapshot([.. _devices.Values], Discarded));
    }

    public Task RenameAsync(string deviceId, string? customName, CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var device))
        {
            _devices[deviceId] = device with
            {
                CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim(),
            };
        }

        return Task.CompletedTask;
    }

    public Task SetPrimaryAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        foreach (var (id, device) in _devices.ToList())
        {
            _devices[id] = device with { IsPrimary = string.Equals(id, deviceId, StringComparison.Ordinal) };
        }

        return Task.CompletedTask;
    }
}
