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

        if (settings.SchemaVersion < 5)
        {
            // Le mode « largeur libre » est retiré. Il ne pouvait pas tenir sa
            // promesse : le jeu fige la hauteur de sa mise en page à son
            // initialisation, donc changer la hauteur d'une fenêtre rognait
            // l'image ou laissait une bande. Le réglage disparaît du fichier
            // à la réécriture, sans que rien ne soit à décider.
            changed = true;
        }

        if (settings.SchemaVersion < 6)
        {
            // Les appareils ne se trient plus : l'ordre est global et libre,
            // porté par le seul rang de chaque instance. La liste des appareils
            // disparaît du fichier à la réécriture, sans rien à décider, les
            // rangs portant déjà l'ordre voulu.
            InstanceOrdering.Normalize(settings);
            changed = true;
        }

        if (settings.SchemaVersion < 7)
        {
            // La densité de l'afficheur devient un réglage de zoom, calculé à
            // partir de la définition retenue. La valeur fixe du fichier n'a
            // plus d'effet et disparaît à la réécriture ; le zoom démarre au
            // réglage d'origine, qui donne la même chose qu'avant.
            settings.GameZoom = GameZoom.Normal;
            changed = true;
        }

        // Les paliers retirés, huitième et neuvième versions : la qualité
        // « Haute » fondue dans la maximale, le zoom « très proche » fondu dans
        // « proche ». Il n'y a rien à faire ici, et il ne le faut pas : le
        // convertisseur tolérant a déjà remplacé la valeur inconnue à la
        // lecture, par le repli déclaré sur l'énumération, si bien qu'un
        // Enum.IsDefined placé ici est toujours vrai et que la branche ne tirait
        // jamais. Deux migrations mortes, dont une qui mentait : un fichier
        // portant « Closest » retombait sur le réglage d'origine et non sur
        // « proche ». Les deux replis portent maintenant la décision.

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

    /// <summary>
    /// Modifie les réglages et ne les écrit que si quelque chose a changé.
    ///
    /// Réaffirmer un état déjà en place, ce que fait chaque lancement,
    /// réécrivait le fichier et prévenait tout le monde pour rien.
    /// </summary>
    /// <returns>Vrai si le fichier a été réécrit.</returns>
    public async Task<bool> UpdateIfChangedAsync(
        Func<AppSettingsDocument, bool> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsDocument document;

        try
        {
            document = await LoadOrMigrateAsync(cancellationToken).ConfigureAwait(false);

            if (!mutate(document))
            {
                return false;
            }

            await _store.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, document);

        return true;
    }

    /// <summary>Force la relecture depuis le disque au prochain accès.</summary>
    public void Invalidate() => _current = null;

    /// <summary>Réglages de mirroring dérivés des préférences.</summary>
    public async Task<ScrcpyOptions> GetScrcpyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);
        var profile = QualityProfile.For(settings.Quality, settings.CustomQuality);

        return new ScrcpyOptions
        {
            AudioEnabled = settings.AudioEnabled,
            ClipboardSyncEnabled = settings.ClipboardSyncEnabled,
            VirtualDisplayWidth = settings.VirtualDisplayWidth,
            VirtualDisplayHeight = settings.VirtualDisplayHeight,
            VirtualDisplayDpi = settings.VirtualDisplayDpi,

            // Le codec ne se choisit qu'au palier personnalisé. Ailleurs il
            // reste nul, et scrcpy décide : c'est lui qui sait ce que
            // l'appareil encode en matériel.
            VideoCodec = settings.Quality == StreamQuality.Custom
                ? settings.CustomQuality.Sanitized().VideoCodec
                : null,
            // Les images par seconde et le débit ne viennent que de la
            // qualité choisie : deux sources pour un même réglage auraient
            // fini par diverger.
            //
            // Le débit est ici celui de la définition mémorisée. Il est
            // recalculé au lancement sur la définition réellement retenue, qui
            // suit la taille de la fenêtre : c'est là qu'il prend son sens.
            MaxFps = profile.MaxFps,
            VideoBitrateKbps = profile.BitrateFor(
                settings.VirtualDisplayWidth,
                Math.Min(settings.VirtualDisplayHeight, profile.MaximumDisplayHeight)),
        }.Sanitized();
    }

    /// <summary>Profil de qualité en vigueur.</summary>
    public async Task<QualityProfile> GetQualityAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return QualityProfile.For(settings.Quality, settings.CustomQuality);
    }

    /// <summary>Retient la qualité choisie.</summary>
    public Task SetQualityAsync(StreamQuality quality, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.Quality = quality, cancellationToken);

    /// <summary>Valeurs du palier personnalisé, corrigées si le fichier déraille.</summary>
    public async Task<CustomQuality> GetCustomQualityAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(cancellationToken).ConfigureAwait(false)).CustomQuality.Sanitized();

    /// <summary>Retient les valeurs du palier personnalisé.</summary>
    public Task SetCustomQualityAsync(
        CustomQuality custom,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(custom);

        return UpdateAsync(settings => settings.CustomQuality = custom.Sanitized(), cancellationToken);
    }

    /// <summary>Retient si le son du téléphone doit sortir sur le PC.</summary>
    public Task SetAudioEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.AudioEnabled = enabled, cancellationToken);

    /// <summary>Distance apparente en vigueur.</summary>
    public async Task<GameZoom> GetZoomAsync(CancellationToken cancellationToken = default) =>
        (await GetAsync(cancellationToken).ConfigureAwait(false)).GameZoom;

    /// <summary>Retient la distance apparente choisie.</summary>
    public Task SetZoomAsync(GameZoom zoom, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.GameZoom = zoom, cancellationToken);

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
    /// <summary>
    /// Oublie les instances dont le profil Android n'existe plus.
    ///
    /// Une instance mémorisée survit à une déconnexion, et c'est voulu : un
    /// téléphone débranché doit garder ses lignes. Elle survivait aussi à la
    /// suppression du profil, ce qui laissait dans la liste un compte qui
    /// n'existe nulle part, qu'aucun bouton ne pouvait retirer.
    ///
    /// On ne se fie pas à l'absence du jeu, qui peut n'être qu'un échec de
    /// commande passager : on se fie à la disparition du profil. Seuls les
    /// téléphones dont la liste de profils a été lue pour de bon sont
    /// concernés, les autres ne prouvent rien.
    /// </summary>
    /// <param name="profiles">Profils relevés, par identifiant d'appareil.</param>
    /// <returns>Le nombre d'instances oubliées.</returns>
    public async Task<int> ForgetMissingProfilesAsync(
        IReadOnlyDictionary<string, IReadOnlyList<int>> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return 0;
        }

        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        var gone = settings.Instances
            .Where(i => profiles.TryGetValue(i.DeviceId, out var live) && !live.Contains(i.UserId))
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Rien à retirer : on n'écrit pas. Une écriture sans changement à
        // chaque balayage userait le fichier pour rien.
        if (gone.Count == 0)
        {
            return 0;
        }

        await UpdateAsync(
            document =>
            {
                // La géométrie de la fenêtre part avec l'instance : elle est
                // portée par l'entrée elle-même, et non par le dictionnaire des
                // places, qui ne connaît que nos propres fenêtres.
                _ = document.Instances.RemoveAll(i => gone.Contains(i.Key));

                InstanceOrdering.Normalize(document);
            },
            cancellationToken).ConfigureAwait(false);

        return gone.Count;
    }

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
                };

                // À la suite de celles de son appareil : une instance neuve
                // doit apparaître près de ses sœurs, pas au bout d'une longue
                // liste où on ne la verrait pas.
                InstanceOrdering.Add(settings, entry);
                stored[entry.Key] = entry;
            }

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
                    IsManaged = i.IsManaged,
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

    /// <summary>
    /// Place une instance juste avant ou juste après une autre, quel que soit
    /// leur appareil. Rend faux si rien ne bouge.
    /// </summary>
    public async Task<bool> MoveInstanceAsync(
        string key,
        string targetKey,
        bool above,
        CancellationToken cancellationToken = default)
    {
        var moved = false;

        await UpdateAsync(
            settings => moved = InstanceOrdering.MoveInstance(settings, key, targetKey, above),
            cancellationToken).ConfigureAwait(false);

        return moved;
    }

    /// <summary>Fixe l'ordre complet des instances, par leurs clés.</summary>
    public Task ReorderInstancesAsync(
        IReadOnlyList<string> orderedKeys,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings => InstanceOrdering.ReorderInstances(settings, orderedKeys),
            cancellationToken);

    // Démarrage

    /// <summary>
    /// Marque des instances comme faisant partie du lancement suivant, ou les
    /// en retire.
    ///
    /// Lancer une instance l'y met, la fermer par le bouton l'en retire, et
    /// rien d'autre n'y touche : fermer une fenêtre de jeu à la main, quitter
    /// l'application ou perdre le téléphone laissent l'ensemble intact.
    /// </summary>
    public async Task SetInstancesEnabledAsync(
        IReadOnlyCollection<string> keys,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return;
        }

        var wanted = keys.ToHashSet(StringComparer.Ordinal);

        await UpdateIfChangedAsync(
            settings =>
            {
                var changed = false;

                foreach (var instance in settings.Instances.Where(i => wanted.Contains(i.Key)))
                {
                    if (instance.IsEnabled != enabled)
                    {
                        instance.IsEnabled = enabled;
                        changed = true;
                    }
                }

                return changed;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retient si une fenêtre suit les placements automatiques.
    /// </summary>
    public Task SetInstanceManagedAsync(
        string key,
        bool managed,
        CancellationToken cancellationToken = default) =>
        UpdateIfChangedAsync(
            settings =>
            {
                var instance = settings.Instances.Find(
                    i => string.Equals(i.Key, key, StringComparison.Ordinal));

                if (instance is null || instance.IsManaged == managed)
                {
                    return false;
                }

                instance.IsManaged = managed;

                return true;
            },
            cancellationToken);

    /// <summary>Instances laissées de côté par les placements automatiques.</summary>
    public async Task<IReadOnlySet<string>> GetUnmanagedKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.Instances
            .Where(i => !i.IsManaged)
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Retient la taille posée au curseur.</summary>
    public Task SaveCustomSizePercentAsync(int percent, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.CustomSizePercent = Math.Clamp(percent, 0, 100), cancellationToken);

    /// <summary>Retient si le configurateur était affiché à la sortie.</summary>
    public Task SetConfiguratorVisibleAsync(bool visible, CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.ConfiguratorVisible = visible, cancellationToken);

    /// <summary>
    /// Retient si le suivi de quêtes était ouvert et sur quelle quête, pour le
    /// rouvrir tel quel au lancement suivant.
    /// </summary>
    /// <summary>Retient si l'application doit se mettre à jour toute seule.</summary>
    public Task SetUpdatesAutomaticAsync(
        bool automatic,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(settings => settings.UpdatesAutomatic = automatic, cancellationToken);

    public Task SetQuestsStateAsync(
        bool visible,
        string? lastQuestUrl,
        int lastQuestStep = 0,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings =>
            {
                settings.QuestsVisible = visible;

                // L'étape suit l'adresse : retenir un rang sans le guide auquel
                // il appartient ferait rouvrir une autre quête à une étape qui
                // n'est pas la sienne.
                if (!string.IsNullOrWhiteSpace(lastQuestUrl))
                {
                    settings.LastQuestStep = lastQuestStep < 0 ? 0 : lastQuestStep;
                }

                // Une adresse vide n'efface pas la précédente : fermer la
                // fenêtre sur sa liste ne doit pas faire oublier la quête qu'on
                // y lisait avant.
                if (!string.IsNullOrWhiteSpace(lastQuestUrl))
                {
                    settings.LastQuestUrl = lastQuestUrl;
                }
            },
            cancellationToken);

    /// <summary>
    /// Retient où était une fenêtre. Une place sans surface n'est pas
    /// enregistrée : c'est ce que rend une fenêtre jamais affichée.
    /// </summary>
    public Task SetWindowPlacementAsync(
        string key,
        WindowPlacement? placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (placement is not { IsSized: true })
        {
            return Task.CompletedTask;
        }

        return UpdateAsync(settings => settings.WindowPlacements[key] = placement, cancellationToken);
    }

    public void Dispose() => _gate.Dispose();
}
