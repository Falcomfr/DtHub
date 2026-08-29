using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;

namespace DtHub.Core.Settings;

/// <summary>
/// Point d'accès unique aux réglages. Chargés une fois, tenus en mémoire, et
/// écrits à chaque modification : il n'y a pas de bouton Enregistrer à oublier.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly IDocumentStore<AppSettingsDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettingsDocument? _current;

    public SettingsService(IDocumentStore<AppSettingsDocument> store) => _store = store;

    /// <summary>Déclenché après chaque écriture réussie.</summary>
    public event EventHandler<AppSettingsDocument>? Changed;

    public async Task<AppSettingsDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Modifie les réglages et les écrit.</summary>
    public async Task UpdateAsync(
        Action<AppSettingsDocument> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsDocument document;

        try
        {
            _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            mutate(_current);
            _current.SchemaVersion = AppSettingsDocument.CurrentSchemaVersion;

            await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
            document = _current;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, document);
    }

    /// <summary>Force la relecture depuis le disque au prochain accès.</summary>
    public void Invalidate() => _current = null;

    /// <summary>Réglages de mirroring dérivés des préférences.</summary>
    public async Task<ScrcpyOptions> GetScrcpyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new ScrcpyOptions
        {
            MaxFps = settings.MaxFps,
            VideoBitrateKbps = settings.VideoBitrateKbps,
            AudioEnabled = settings.AudioEnabled,
            ClipboardSyncEnabled = settings.ClipboardSyncEnabled,
            VirtualDisplayWidth = settings.VirtualDisplayWidth,
            VirtualDisplayHeight = settings.VirtualDisplayHeight,
            VirtualDisplayDpi = settings.VirtualDisplayDpi,
        }.Sanitized();
    }

    /// <summary>Raccourcis configurés, réparés si le fichier est incohérent.</summary>
    public async Task<HotkeySet> GetHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        var bindings = settings.Hotkeys
            .Select(h => h.ToBinding())
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        return HotkeySet.FromBindings(bindings.Count == 0 ? null : bindings);
    }

    public Task SaveHotkeysAsync(HotkeySet hotkeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return UpdateAsync(
            settings => settings.Hotkeys = [.. hotkeys.Bindings.Select(StoredHotkey.From)],
            cancellationToken);
    }

    /// <summary>
    /// Fusionne les instances découvertes avec celles qui étaient mémorisées.
    /// Le nom choisi par l'utilisateur et la case de lancement lui
    /// appartiennent : une redécouverte ne les écrase jamais.
    /// </summary>
    public async Task<IReadOnlyList<DofusInstance>> MergeInstancesAsync(
        IReadOnlyList<DofusInstance> discovered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        IReadOnlyList<DofusInstance> merged = [];

        await UpdateAsync(settings =>
        {
            var stored = settings.Instances.ToDictionary(i => i.Key, StringComparer.Ordinal);

            foreach (var instance in discovered)
            {
                if (stored.TryGetValue(instance.Key, out var existing))
                {
                    existing.DeviceName = instance.DeviceName;
                    existing.UserName = instance.UserName;
                    existing.LaunchComponent = instance.LaunchComponent ?? existing.LaunchComponent;
                    continue;
                }

                var entry = new StoredInstance
                {
                    DeviceId = instance.DeviceId,
                    UserId = instance.UserId,
                    PackageName = instance.PackageName,
                    DeviceName = instance.DeviceName,
                    UserName = instance.UserName,
                    LaunchComponent = instance.LaunchComponent,
                    IsEnabled = false,
                    Order = settings.Instances.Count,
                };

                settings.Instances.Add(entry);
                stored[entry.Key] = entry;
            }

            var live = discovered.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            merged = [.. settings.Instances
                .OrderBy(i => i.Order)
                .Select(i => new DofusInstance
                {
                    DeviceId = i.DeviceId,
                    DeviceName = i.DeviceName,
                    UserId = i.UserId,
                    UserName = i.UserName,
                    PackageName = i.PackageName,
                    LaunchComponent = i.LaunchComponent,
                    CustomName = i.CustomName,
                    IsEnabled = i.IsEnabled,
                    IsDeviceConnected = live.Contains(i.Key),
                })];
        }, cancellationToken).ConfigureAwait(false);

        return merged;
    }

    /// <summary>Coche ou décoche une instance pour le lancement automatique.</summary>
    public Task SetInstanceEnabledAsync(string key, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
            if (instance is not null)
            {
                instance.IsEnabled = enabled;
            }
        }, cancellationToken);

    /// <summary>Renomme une instance. Un nom vide rétablit le nom du profil Android.</summary>
    public Task RenameInstanceAsync(string key, string? name, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
            if (instance is not null)
            {
                instance.CustomName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
        }, cancellationToken);

    /// <summary>Oublie les instances d'un téléphone retiré.</summary>
    public Task ForgetDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
            settings.Instances.RemoveAll(i => string.Equals(i.DeviceId, deviceId, StringComparison.Ordinal)),
            cancellationToken);

    public void Dispose() => _gate.Dispose();
}
