using DtHub.Core.Devices;

namespace DtHub.Tests.Fakes;

/// <summary>Registre en mémoire, avec la même sémantique que celui sur disque.</summary>
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

    public Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AndroidDevice>>([.. _devices.Values]);

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
