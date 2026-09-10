using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Dresse la liste des téléphones : ce qu'ADB voit maintenant, enrichi des
/// propriétés du téléphone et de ce qui avait été mémorisé. Les appareils
/// connus mais absents restent présentés comme hors ligne, sans quoi ils
/// disparaîtraient de l'interface au moindre débranchement.
/// </summary>
public sealed class DeviceDiscoveryService : IDisposable
{
    /// <summary>
    /// Les propriétés système ne changent pas d'un balayage à l'autre : les
    /// relire à chaque fois coûterait un aller-retour ADB par appareil pour
    /// rien.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _propertyCache = new(StringComparer.Ordinal);

    private readonly IAdbClient _adb;

    /// <summary>Combien de temps une lecture de liaison reste valable.</summary>
    private static readonly TimeSpan LinkFreshness = TimeSpan.FromMinutes(1);

    /// <summary>Dernière liaison lue par appareil, avec son âge.</summary>
    private readonly Dictionary<string, (WifiLink? Link, System.Diagnostics.Stopwatch Vu)> _link =
        new(StringComparer.Ordinal);
    private readonly IDeviceRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceDiscoveryService(IAdbClient adb, IDeviceRegistry registry)
    {
        _adb = adb;
        _registry = registry;
    }

    /// <summary>Nombre de lectures de propriétés menées de front.</summary>
    public int MaxParallelism { get; init; } = 4;

    /// <summary>
    /// Coupe les connexions sans fil mortes. Le port du débogage sans fil
    /// change à chaque redémarrage du téléphone, et l'ancienne connexion reste
    /// indéfiniment listée comme hors ligne. Sans ce ménage, un même téléphone
    /// finit par occuper plusieurs entrées dont une éteinte, et c'est parfois
    /// elle qui s'affiche.
    /// </summary>
    /// <returns>Nombre de connexions coupées.</returns>
    public async Task<int> PruneStaleWirelessTransportsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdbDeviceEntry> entries;

        try
        {
            entries = await _adb.ListDevicesAsync(detailed: true, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException)
        {
            // Silence assumé : sans réponse d'ADB on ne sait pas quels
            // appareils ont disparu, et n'en oublier aucun est le bon défaut.
            // Le refus d'ADB, lui, est déjà dit par le balayage qui suit.
            return 0;
        }

        var stale = entries
            .Where(e => e.ConnectionKind == AdbConnectionKind.Wireless
                        && e.State == AdbDeviceState.Offline
                        && e.Host is { Length: > 0 }
                        && e.Port is > 0)
            .ToList();

        foreach (var entry in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _adb.DisconnectAsync(entry.Serial, cancellationToken).ConfigureAwait(false);
                InvalidatePropertyCache(entry.Serial);
            }
            catch (AdbException)
            {
                // La connexion a peut-être disparu d'elle-même.
            }
        }

        return stale.Count;
    }

    /// <summary>Balaye les appareils et met le registre à jour.</summary>
    public async Task<DeviceDiscoveryResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Coupe tous les transports ADB qui mènent à cet appareil.
    ///
    /// Un même téléphone en occupe couramment deux : celui de son adresse, et
    /// celui de son nom mDNS, qu'ADB ouvre de lui-même en découvrant l'annonce.
    /// Mesuré sur l'appareil de développement, qui en portait bien deux.
    /// N'en couper qu'un laisserait l'autre ouvert.
    ///
    /// Le client ADB vit ici et nulle part ailleurs dans cette couche : rompre
    /// une association a besoin de cette coupure, et lui donner son propre
    /// client reviendrait à disperser l'accès à ADB pour une seule commande.
    /// </summary>
    /// <returns>Les adresses effectivement coupées, dans l'ordre.</returns>
    public async Task<IReadOnlyList<string>> DisconnectDeviceAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        IReadOnlyList<AdbDeviceEntry> entries;

        try
        {
            entries = await _adb.ListDevicesAsync(detailed: false, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException)
        {
            // Sans réponse d'ADB il ne reste que l'adresse mémorisée, et la
            // tenter vaut mieux que renoncer. Le refus d'ADB est déjà dit
            // ailleurs, par le balayage.
            entries = [];
        }

        var addresses = entries
            .Where(e => e.ConnectionKind == AdbConnectionKind.Wireless
                && DeviceFactory.Match([device], e) is not null)
            .Select(e => e.Serial)
            .Concat(device.ReconnectAddress is { Length: > 0 } remembered ? [remembered] : [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<string> cut = [];

        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _adb.DisconnectAsync(address, cancellationToken).ConfigureAwait(false);
            InvalidatePropertyCache(address);
            cut.Add(address);
        }

        return cut;
    }

    /// <summary>Dernier état thermique lu par appareil, avec son âge.</summary>
    private readonly Dictionary<string, (ThermalReading? Reading, System.Diagnostics.Stopwatch Vu)> _heat =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Combien de temps une lecture de chaleur reste valable.
    ///
    /// Plus longue que celle de la liaison : un appareil ne passe pas d'un
    /// palier thermique à l'autre en quelques secondes, et la question coûte un
    /// aller-retour de shell.
    /// </summary>
    private static readonly TimeSpan HeatFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Ce que l'appareil dit de sa chaleur, ou <c>null</c> s'il n'en dit rien.
    ///
    /// La question n'est posée qu'une fois par minute et par appareil : c'est
    /// le rythme auquel la chaleur bouge, et l'appelant la pose à chaque
    /// balayage sans que cela se paie.
    /// </summary>
    public async Task<ThermalReading?> GetThermalAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_heat.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < HeatFreshness)
        {
            return garde.Reading;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(serial, ["dumpsys", "thermalservice"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var reading = ThermalReading.Parse(dump);

            _heat[serial] = (reading, System.Diagnostics.Stopwatch.StartNew());

            return reading;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Même silence assumé que pour la liaison : ne pas connaître la
            // chaleur est un résultat valable, que l'appelant traite comme une
            // absence de contrainte. Un appareil qui répond mal à une question
            // accessoire ne doit pas faire échouer le balayage.
            return null;
        }
    }

    /// <summary>
    /// L'afficheur virtuel ouvert par scrcpy sur cet appareil est-il
    /// déverrouillé, ou <c>null</c> si on ne le trouve pas.
    ///
    /// Sans cache : la question n'a de sens qu'une fois l'afficheur créé, et
    /// elle n'est posée qu'une fois par lancement.
    /// </summary>
    public async Task<bool?> IsVirtualDisplayUnlockedAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_trusted.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < TrustFreshness)
        {
            return garde.Unlocked;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(serial, ["dumpsys", "display"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var unlocked = VirtualDisplayTrust.IsUnlocked(dump);

            // Seule une réponse est gardée : tant qu'aucun afficheur n'existe,
            // la question n'a pas de réponse et il faudra la reposer.
            if (unlocked is not null)
            {
                _trusted[serial] = (unlocked, System.Diagnostics.Stopwatch.StartNew());
            }

            return unlocked;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Même silence assumé que pour les autres sondages accessoires.
            return null;
        }
    }

    /// <summary>
    /// L'appareil est-il verrouillé en ce moment, ou <c>null</c> si sa
    /// réponse ne le dit pas.
    ///
    /// Sans cache, et pour la même raison que le drapeau de l'afficheur : la
    /// question n'est posée qu'au lancement, et un état qui change en une
    /// seconde ne se garde pas.
    /// </summary>
    public async Task<bool?> IsDeviceLockedAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_locked.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < LockFreshness)
        {
            return garde.Locked;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(serial, ["dumpsys", "trust"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var locked = DeviceLock.IsLocked(dump);

            _locked[serial] = (locked, System.Diagnostics.Stopwatch.StartNew());

            return locked;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Même silence assumé que pour les autres sondages accessoires.
            return null;
        }
    }

    /// <summary>
    /// Dernier verdict de confiance de l'afficheur, par appareil.
    ///
    /// Gardé longtemps : ce drapeau dit ce dont l'appareil est capable, et
    /// cela ne change pas d'une session à l'autre. Une minute suffit à ce que
    /// le panneau puisse poser la question toutes les deux secondes.
    /// </summary>
    private readonly Dictionary<string, (bool? Unlocked, System.Diagnostics.Stopwatch Vu)> _trusted =
        new(StringComparer.Ordinal);

    private static readonly TimeSpan TrustFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Dernier état de verrouillage, par appareil.
    ///
    /// Gardé peu : c'est un état que l'utilisateur change d'un geste, et
    /// l'avertissement doit disparaître dans la foulée du déverrouillage.
    /// </summary>
    private readonly Dictionary<string, (bool? Locked, System.Diagnostics.Stopwatch Vu)> _locked =
        new(StringComparer.Ordinal);

    private static readonly TimeSpan LockFreshness = TimeSpan.FromSeconds(4);

    /// <summary>Dernier niveau de batterie lu par appareil, avec son âge.</summary>
    private readonly Dictionary<string, (BatteryReading? Reading, System.Diagnostics.Stopwatch Vu)> _battery =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Combien de temps une lecture de batterie reste valable.
    ///
    /// La même minute que la chaleur, et pour la même raison : une batterie ne
    /// perd pas dix pour cent en dix secondes, et la question coûte un
    /// aller-retour de shell à chaque balayage.
    /// </summary>
    private static readonly TimeSpan BatteryFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Ce que l'appareil dit de sa batterie, ou <c>null</c> s'il n'en dit rien.
    /// </summary>
    public async Task<BatteryReading?> GetBatteryAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_battery.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < BatteryFreshness)
        {
            return garde.Reading;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(serial, ["dumpsys", "battery"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var reading = BatteryReading.Parse(dump);

            _battery[serial] = (reading, System.Diagnostics.Stopwatch.StartNew());

            return reading;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Même silence assumé que pour la chaleur : ne pas connaître la
            // batterie est un résultat valable, et un appareil qui répond mal
            // à une question accessoire ne doit pas faire échouer le balayage.
            return null;
        }
    }

    /// <summary>Dernière place libre lue par appareil, avec son âge.</summary>
    private readonly Dictionary<string, (StorageReading? Reading, System.Diagnostics.Stopwatch Vu)> _storage =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Combien de temps une lecture de place libre reste valable.
    ///
    /// Bien plus longue que les autres : la place ne bouge pas en séance, et
    /// c'est une assurance, pas une surveillance.
    /// </summary>
    private static readonly TimeSpan StorageFreshness = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Ce que l'appareil dit de sa place libre, ou <c>null</c> s'il n'en dit
    /// rien.
    /// </summary>
    public async Task<StorageReading?> GetStorageAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_storage.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < StorageFreshness)
        {
            return garde.Reading;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(serial, ["df", "/data"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var reading = StorageReading.Parse(dump);

            _storage[serial] = (reading, System.Diagnostics.Stopwatch.StartNew());

            return reading;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Même silence assumé que pour les autres sondages accessoires.
            return null;
        }
    }

    /// <summary>
    /// Demande à l'appareil s'il accepte la simulation d'entrée.
    ///
    /// Le symptôme qu'elle éclaire est silencieux par nature : l'image passe,
    /// la fenêtre s'ouvre, et le clic ne fait rien sans qu'aucune erreur ne
    /// paraisse. La sonde envoie la touche « inconnue », qui ne déclenche rien,
    /// et lit ce que le système répond.
    ///
    /// Elle n'est lancée que sur demande de l'utilisateur. Le délai est court :
    /// une question à laquelle l'appareil ne répond pas vite ne répondra pas.
    /// </summary>
    public async Task<InputInjection> CheckInputInjectionAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return InputInjection.Unknown;
        }

        try
        {
            var probe = await _adb
                .ExecuteAsync(
                    serial,
                    InputInjectionCheck.ProbeCommand,
                    ProbeTimeout,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return probe.TimedOut
                ? InputInjection.Unknown
                : InputInjectionCheck.Read(probe.ExitCode, probe.StandardOutput, probe.StandardError);
        }
        catch (AdbException)
        {
            // Un appareil qui ne répond plus n'apprend rien sur la souris, et
            // le dire relèverait du hasard. Son absence est déjà signalée
            // ailleurs, par le balayage.
            return InputInjection.Unknown;
        }
    }

    /// <summary>Délai laissé à la sonde d'entrée.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ce que l'appareil dit de sa liaison Wi-Fi, ou <c>null</c> s'il n'en a
    /// pas à dire : liaison USB, Wi-Fi éteint, ou Android qui répond autrement.
    ///
    /// Gardé une minute, pas davantage : un réseau change, et c'est justement
    /// quand il change qu'il faut le relire. Mais l'appel coûte 0,46 s mesuré,
    /// il est sur le chemin de l'ouverture, et ouvrir deux comptes coup sur
    /// coup le payait deux fois pour la même réponse.
    ///
    /// Aucune faute n'est propagée. Ne pas connaître la liaison ne doit jamais
    /// empêcher une session de s'ouvrir : l'appelant traite l'absence comme
    /// une absence de contrainte.
    /// </summary>
    public async Task<WifiLink?> GetWifiLinkAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (_link.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < LinkFreshness)
        {
            return garde.Link;
        }

        try
        {
            var status = await _adb
                .ShellAsync(serial, ["cmd", "wifi", "status"], cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var link = WifiLink.Parse(status);

            _link[serial] = (link, System.Diagnostics.Stopwatch.StartNew());

            return link;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Silence assumé, et c'est le seul endroit où il se justifie ici :
            // ne pas connaître la liaison est déjà un résultat valable, que
            // l'appelant traite comme une absence de contrainte. Faire échouer
            // un lancement parce qu'un appareil répond mal à une question
            // accessoire serait hors de proportion.
            return null;
        }
    }

    /// <summary>
    /// Force la relecture des propriétés au prochain balayage, par exemple
    /// après une mise à jour d'Android ou un changement de nom d'appareil.
    /// </summary>
    public void InvalidatePropertyCache(string? serial = null)
    {
        lock (_propertyCache)
        {
            if (serial is null)
            {
                _propertyCache.Clear();
            }
            else
            {
                _propertyCache.Remove(serial);
            }
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<DeviceDiscoveryResult> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        IReadOnlyList<AdbDeviceEntry> entries;
        try
        {
            entries = await _adb.ListDevicesAsync(detailed: true, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException exception)
        {
            // Sans ADB, on montre au moins ce qui était mémorisé plutôt qu'un
            // écran vide.
            warnings.Add(exception.UserMessage);
            var stored = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceDiscoveryResult(stored, warnings);
        }

        // Les mémorisés et les écartés viennent ensemble : le balayage a besoin
        // des deux, et le registre relit son fichier à chaque demande.
        var (known, discarded) = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        // Les émulateurs ne sont pas la cible de l'outil et brouilleraient la
        // liste : ils sont écartés dès la découverte.
        //
        // Les doubles aussi, et avant toute interrogation : ADB rejoint un
        // téléphone tout seul par mDNS alors qu'il est déjà connecté par son
        // adresse, et le nom mDNS refuse ensuite les commandes qu'on lui
        // adresse.
        var relevant = AdbTransportChoice.WithoutDoubles(
            [.. entries.Where(e => e.ConnectionKind != AdbConnectionKind.Emulator)],
            known);

        var properties = await ReadPropertiesAsync(relevant, warnings, cancellationToken).ConfigureAwait(false);

        var observedAt = DateTimeOffset.UtcNow;
        var discovered = new List<AndroidDevice>(relevant.Count);

        foreach (var entry in relevant)
        {
            properties.TryGetValue(entry.Serial, out var entryProperties);

            var hardwareSerial = DeviceProperties.ReadHardwareSerial(entryProperties);
            var match = DeviceFactory.Match(known, entry, hardwareSerial);

            discovered.Add(DeviceFactory.Create(entry, entryProperties, match, observedAt));
        }

        // Un même téléphone peut apparaître deux fois, branché en USB et
        // toujours connecté en Wi-Fi. On garde la meilleure des deux lignes.
        var deduplicated = Deduplicate(discovered);

        // Un appareil écarté sort du balayage entier, et pas seulement de
        // l'écriture au registre. La coupure ADB ne tient pas éternellement :
        // le serveur rejoint de lui-même un téléphone qui s'annonce et dont il
        // garde la clé, au redémarrage du serveur ou à la réactivation du
        // débogage sans fil. Filtrer le seul registre laissait alors la ligne
        // revenir à l'écran, ce qui est le défaut rapporté.
        var merged = discarded.Count == 0
            ? deduplicated
            : deduplicated.Where(d => !discarded.Contains(d.Id)).ToList();

        await _registry.UpsertRangeAsync(merged, cancellationToken).ConfigureAwait(false);

        var seen = merged.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        // Le souvenir est filtré comme la découverte : un appareil écarté n'a
        // pas à revenir par la liste des hors ligne.
        var offline = known.Where(d => !seen.Contains(d.Id) && !discarded.Contains(d.Id));

        var all = merged
            .Concat(offline)
            .OrderByDescending(d => d.IsPrimary)
            .ThenByDescending(d => d.IsConnected)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        PruneCache(relevant);

        return new DeviceDiscoveryResult(all, warnings);
    }

    private async Task<Dictionary<string, IReadOnlyDictionary<string, string>>> ReadPropertiesAsync(
        IReadOnlyList<AdbDeviceEntry> entries,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        List<AdbDeviceEntry> toRead = [];

        foreach (var entry in entries.Where(e => e.IsReady))
        {
            lock (_propertyCache)
            {
                if (_propertyCache.TryGetValue(entry.Serial, out var cached))
                {
                    results[entry.Serial] = cached;
                    continue;
                }
            }

            toRead.Add(entry);
        }

        if (toRead.Count == 0)
        {
            return results;
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, MaxParallelism),
            CancellationToken = cancellationToken,
        };

        var collected = new System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Parallel.ForEachAsync(toRead, options, async (entry, token) =>
        {
            try
            {
                var properties = await _adb.GetPropertiesAsync(entry.Serial, token).ConfigureAwait(false);
                if (properties.Count > 0)
                {
                    collected[entry.Serial] = properties;
                }
            }
            catch (AdbException exception)
            {
                // Un téléphone qui refuse getprop reste listé : il faut
                // justement pouvoir l'afficher pour expliquer quoi faire.
                failures.Add($"{entry.DisplayName} : {exception.UserMessage}");
            }
        }).ConfigureAwait(false);

        foreach (var (serial, properties) in collected)
        {
            results[serial] = properties;

            lock (_propertyCache)
            {
                _propertyCache[serial] = properties;
            }
        }

        warnings.AddRange(failures.Distinct(StringComparer.Ordinal));

        return results;
    }

    /// <summary>
    /// Fusionne les doublons d'identité en gardant la ligne la plus utile :
    /// un transport prêt prime sur un transport hors ligne, et l'USB prime sur
    /// le Wi-Fi parce qu'il est plus stable.
    /// </summary>
    private static List<AndroidDevice> Deduplicate(IEnumerable<AndroidDevice> devices) =>
        [.. devices
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(d => d.IsConnected)
                .ThenByDescending(d => d.ConnectionKind == AdbConnectionKind.Usb)

                // Une adresse joignable avant un nom mDNS. Sans ce départage,
                // deux lignes sans fil également connectées se départageaient
                // par l'ordre où ADB les rend, c'est-à-dire par leur numéro de
                // transport : la ligne retenue changeait d'une reconnexion à
                // l'autre, et c'était parfois celle qui refuse les commandes.
                .ThenBy(d => MdnsDeviceName.IsMdnsName(d.Serial))
                .First())];

    private void PruneCache(IEnumerable<AdbDeviceEntry> entries)
    {
        var live = entries.Select(e => e.Serial).ToHashSet(StringComparer.Ordinal);

        lock (_propertyCache)
        {
            foreach (var serial in _propertyCache.Keys.Where(s => !live.Contains(s)).ToList())
            {
                _propertyCache.Remove(serial);
            }
        }
    }
}
