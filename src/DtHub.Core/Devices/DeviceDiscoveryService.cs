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
    private readonly IDeviceRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceDiscoveryService(IAdbClient adb, IDeviceRegistry registry)
    {
        _adb = adb;
        _registry = registry;
    }

    /// <summary>Nombre de lectures de propriétés menées de front.</summary>
    public int MaxParallelism { get; init; } = 4;

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

        var known = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);

        // Les émulateurs ne sont pas la cible de l'outil et brouilleraient la
        // liste : ils sont écartés dès la découverte.
        var relevant = entries.Where(e => e.ConnectionKind != AdbConnectionKind.Emulator).ToList();

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
        var merged = Deduplicate(discovered);

        await _registry.UpsertRangeAsync(merged, cancellationToken).ConfigureAwait(false);

        var seen = merged.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var offline = known.Where(d => !seen.Contains(d.Id));

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
