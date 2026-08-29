using DtHub.Core.Devices;
using DtHub.Core.Storage;

namespace DtHub.Infrastructure.Devices;

/// <summary>
/// Registre adossé à <c>devices.json</c>. Le document complet est relu à
/// chaque opération : il compte quelques dizaines d'entrées au plus, et cela
/// évite toute divergence si le fichier est modifié à la main.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry, IDisposable
{
    private readonly IDocumentStore<DeviceRegistryDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceRegistry(IDocumentStore<DeviceRegistryDocument> store) => _store = store;

    public async Task<IReadOnlyList<AndroidDevice>> GetKnownAsync(CancellationToken cancellationToken = default)
    {
        var document = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        return [.. document.Devices.Where(d => !string.IsNullOrEmpty(d.Id)).Select(d => d.ToDevice())];
    }

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

                // Le nom personnalisé et le marquage principal appartiennent à
                // l'utilisateur : une découverte ne les remet jamais à zéro.
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

    /// <summary>Lecture, modification et écriture sous verrou, pour éviter les pertes croisées.</summary>
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
