using DtHub.Core.Devices;
using DtHub.Core.Storage;

namespace DtHub.Infrastructure.Devices;

/// <summary>
/// Registry backed by <c>devices.json</c>. The full document is
/// reread on every operation: it holds a few dozen entries at
/// most, and that avoids any drift if the file is edited by hand.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry, IDisposable
{
    private readonly IDocumentStore<DeviceRegistryDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceRegistry(IDocumentStore<DeviceRegistryDocument> store) => _store = store;

    public async Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default) =>
        DevicesIn(await LoadCurrentAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Rereads the document and upgrades it if it comes from an
    /// earlier version.
    ///
    /// A file written by version 1 may carry the same phone twice,
    /// under its serial number and under its mDNS name. The merge
    /// is written right away: leaving it only in memory would bring
    /// the duplicate back at the next startup.
    /// </summary>
    private async Task<DeviceRegistryDocument> LoadCurrentAsync(CancellationToken cancellationToken)
    {
        var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (document.SchemaVersion < DeviceRegistryDocument.CurrentSchemaVersion
            && document.MergeDuplicates())
        {
            document.SchemaVersion = DeviceRegistryDocument.CurrentSchemaVersion;

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    private static IReadOnlyList<AndroidDevice> DevicesIn(DeviceRegistryDocument document) =>
        [.. document.Devices.Where(d => !string.IsNullOrEmpty(d.Id)).Select(d => d.ToDevice())];

    public Task UpsertAsync(AndroidDevice device, CancellationToken cancellationToken = default) =>
        UpsertRangeAsync([device], cancellationToken);

    public async Task UpsertRangeAsync(
        IEnumerable<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        var incoming = devices.Where(d => !string.IsNullOrEmpty(d.Id)).ToList();
        if (incoming.Count == 0)
        {
            return;
        }

        await MutateAsync(document =>
        {
            foreach (var device in incoming)
            {
                var stored = StoredDevice.From(device);
                var index = document.Devices.FindIndex(d => string.Equals(d.Id, device.Id, StringComparison.Ordinal));

                if (index < 0)
                {
                    document.Devices.Add(stored);
                    continue;
                }

                // The custom name and the primary flag belong to
                // the user: a discovery never resets them.
                var previous = document.Devices[index];
                stored.CustomName = device.CustomName ?? previous.CustomName;
                stored.IsPrimary = device.IsPrimary || previous.IsPrimary;
                stored.IsPaired = device.IsPaired || previous.IsPaired;
                stored.LastKnownAddress ??= previous.LastKnownAddress;
                stored.LastKnownPort ??= previous.LastKnownPort;

                document.Devices[index] = stored;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ForgetAsync(string deviceId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            document => document.Devices.RemoveAll(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal)),
            cancellationToken);

    public Task DiscardAsync(string deviceId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            document =>
            {
                _ = document.Devices.RemoveAll(
                    d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

                if (!string.IsNullOrEmpty(deviceId)
                    && !document.Discarded.Contains(deviceId, StringComparer.Ordinal))
                {
                    document.Discarded.Add(deviceId);
                }
            },
            cancellationToken);

    public Task WelcomeBackAsync(string deviceId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            document => document.Discarded.RemoveAll(
                d => string.Equals(d, deviceId, StringComparison.Ordinal)),
            cancellationToken);

    public async Task<DeviceRegistrySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await LoadCurrentAsync(cancellationToken).ConfigureAwait(false);

        return new DeviceRegistrySnapshot(
            DevicesIn(document),
            document.Discarded.ToHashSet(StringComparer.Ordinal));
    }

    public Task RenameAsync(string deviceId, string? customName, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            var stored = document.Devices.Find(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));
            if (stored is not null)
            {
                stored.CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
            }
        }, cancellationToken);

    public Task SetPrimaryAsync(string deviceId, CancellationToken cancellationToken = default) =>
        MutateAsync(document =>
        {
            foreach (var stored in document.Devices)
            {
                stored.IsPrimary = string.Equals(stored.Id, deviceId, StringComparison.Ordinal);
            }
        }, cancellationToken);

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Read, modify and write under a lock, to avoid lost updates
    /// between concurrent writes.
    /// </summary>
    private async Task MutateAsync(Action<DeviceRegistryDocument> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            mutate(document);
            document.SchemaVersion = DeviceRegistryDocument.CurrentSchemaVersion;
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
