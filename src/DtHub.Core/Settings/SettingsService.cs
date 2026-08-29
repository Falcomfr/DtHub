using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Point d'accès unique aux réglages. Chargés une fois, tenus en mémoire, et
/// écrits à chaque modification : il n'y a pas de bouton Enregistrer à oublier.
/// </summary>
public sealed class SettingsService : IDisposable
{
    /// <summary>Tailles livrées jusqu'au schéma 3. La première était trop grande.</summary>
    private static readonly int[] LegacySizePercentages = [55, 70, 85, 100];

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
            return await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Charge le document en le migrant si besoin. À appeler sous verrou.
    ///
    /// C'est le seul chemin de chargement : une écriture qui contournerait la
    /// migration estamperait le fichier à la version courante sans l'avoir
    /// converti, et la migration serait alors perdue pour toujours.
    /// </summary>
    private async Task<AppSettingsDocument> LoadOrMigrateAsync(CancellationToken cancellationToken)
    {
        if (_current is not null)
        {
            return _current;
        }

        _current = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (Migrate(_current))
        {
            await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
        }

        return _current;
    }

    /// <summary>
    /// Met à jour un fichier écrit par une version antérieure. Rend vrai s'il
    /// a été modifié et doit être réécrit.
    /// </summary>
    /// <remarks>
    /// Version 3 : l'écran virtuel passe en paysage. Le jeu s'affiche en
    /// paysage, et un écran vertical le réduisait à une bande au milieu de la
    /// fenêtre. Seule la définition d'origine est corrigée : un réglage
    /// choisi par l'utilisateur est respecté.
    ///
    /// Version 4 : la première taille rapetisse, l'ordre des appareils est
    /// déduit de celui des instances, et les rangs sont resserrés. Ils étaient
    /// creux, faute d'avoir jamais été renumérotés après un oubli d'appareil,
    /// et deux instances pouvaient porter le même. Les cases cochées ne sont
    /// pas touchées : elles restent l'ensemble de démarrage jusqu'à la
    /// première sortie par le bouton Quitter, qui le réécrira.
    /// </remarks>
    private static bool Migrate(AppSettingsDocument settings)
    {
        var changed = false;

        if (settings.SchemaVersion < 3
            && settings is { VirtualDisplayWidth: 1080, VirtualDisplayHeight: 1920, VirtualDisplayDpi: 320 })
        {
            settings.VirtualDisplayWidth = 1920;
            settings.VirtualDisplayHeight = 1080;
            settings.VirtualDisplayDpi = 240;
            changed = true;
        }

        if (settings.SchemaVersion < 4)
        {
            if (settings.SizePercentages.SequenceEqual(LegacySizePercentages))
            {
                settings.SizePercentages = [.. AppSettingsDocument.DefaultSizePercentages];
            }

            InstanceOrdering.Normalize(settings);
            changed = true;
        }

        if (settings.SchemaVersion != AppSettingsDocument.CurrentSchemaVersion)
        {
            settings.SchemaVersion = AppSettingsDocument.CurrentSchemaVersion;
            changed = true;
        }

        return changed;
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
            document = await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);
            mutate(document);

            // L'estampille est posée par la migration, et par elle seule : la
            // poser ici marquerait à jour un document qui ne l'est pas.
            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Tailles configurées, corrigées si le fichier est incohérent.</summary>
    public async Task<WindowSizePresets> GetSizePresetsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new WindowSizePresets { Percentages = settings.SizePercentages }.Sanitized();
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

            // Une instance neuve doit se poser en fin de son propre appareil,
            // pas en fin de la liste entière, sans quoi elle s'intercalerait
            // entre deux téléphones.
            InstanceOrdering.Normalize(settings);

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

    /// <summary>Oublie les instances d'un téléphone retiré, et son rang.</summary>
    public Task ForgetDeviceAsync(string deviceId, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            settings.Instances.RemoveAll(i => string.Equals(i.DeviceId, deviceId, StringComparison.Ordinal));

            // La géométrie mémorisée part avec les instances : elle y est
            // imbriquée. Restent les rangs, qu'il faut resserrer.
            InstanceOrdering.Normalize(settings);
        }, cancellationToken);

    // Géométrie des fenêtres

    /// <summary>Géométries mémorisées, par clé d'instance.</summary>
    public async Task<IReadOnlyDictionary<string, StoredWindowRect>> GetWindowRectsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances
            .Where(i => i.Window is not null)
            .ToDictionary(i => i.Key, i => i.Window!, StringComparer.Ordinal);
    }

    /// <summary>
    /// Enregistre plusieurs géométries en une seule écriture. Les instances
    /// absentes du dictionnaire gardent la leur : une fenêtre qui n'était pas
    /// ouverte ne doit pas perdre l'endroit où elle avait été laissée.
    /// </summary>
    public Task SaveWindowRectsAsync(
        IReadOnlyDictionary<string, StoredWindowRect> rects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rects);

        return UpdateAsync(settings =>
        {
            foreach (var (key, rect) in rects)
            {
                var instance = settings.Instances.Find(
                    i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is not null)
                {
                    instance.Window = rect;
                }
            }
        }, cancellationToken);
    }

    /// <summary>Oublie la géométrie d'une instance : elle repartira de l'ancrage.</summary>
    public Task ClearWindowRectAsync(string key, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings =>
        {
            var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));
            if (instance is not null)
            {
                instance.Window = null;
            }
        }, cancellationToken);

    // Ordre

    /// <summary>Rang de chaque instance, par sa clé, pour trier des sessions.</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetInstanceRanksAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances.ToDictionary(i => i.Key, i => i.Order, StringComparer.Ordinal);
    }

    /// <summary>Décale un appareil. Rend faux s'il est déjà à l'extrémité.</summary>
    public async Task<bool> MoveDeviceAsync(
        string deviceId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var moved = false;

        await UpdateAsync(
            settings => moved = InstanceOrdering.MoveDevice(settings, deviceId, offset),
            cancellationToken).ConfigureAwait(false);

        return moved;
    }

    /// <summary>Décale une instance dans son appareil, dont elle ne sort pas.</summary>
    public async Task<bool> MoveInstanceAsync(
        string key,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var moved = false;

        await UpdateAsync(
            settings => moved = InstanceOrdering.MoveInstance(settings, key, offset),
            cancellationToken).ConfigureAwait(false);

        return moved;
    }

    /// <summary>Fixe l'ordre des appareils.</summary>
    public Task ReorderDevicesAsync(
        IReadOnlyList<string> orderedDeviceIds,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => InstanceOrdering.ReorderDevices(settings, orderedDeviceIds), cancellationToken);

    /// <summary>Fixe l'ordre des instances d'un appareil.</summary>
    public Task ReorderInstancesAsync(
        string deviceId,
        IReadOnlyList<string> orderedKeys,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings => InstanceOrdering.ReorderInstances(settings, deviceId, orderedKeys),
            cancellationToken);

    // Démarrage

    /// <summary>
    /// Remplace l'ensemble des instances ouvertes au démarrage par celles qui
    /// étaient ouvertes au moment de quitter. C'est un remplacement : toute
    /// instance absente de la liste en sort.
    /// </summary>
    public Task SaveStartupSetAsync(
        IReadOnlyCollection<string> openKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openKeys);

        var wanted = openKeys.ToHashSet(StringComparer.Ordinal);

        return UpdateAsync(settings =>
        {
            foreach (var instance in settings.Instances)
            {
                instance.IsEnabled = wanted.Contains(instance.Key);
            }
        }, cancellationToken);
    }

    /// <summary>Retient si le configurateur était affiché à la sortie.</summary>
    public Task SetConfiguratorVisibleAsync(bool visible, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.ConfiguratorVisible = visible, cancellationToken);

    public void Dispose() => _gate.Dispose();
}
