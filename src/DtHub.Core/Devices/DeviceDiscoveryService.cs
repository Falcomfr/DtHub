using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Builds the phone list: what ADB sees right now, enriched with the phone's
/// properties and with what had been remembered. Known devices that are absent
/// are still shown as offline, or they would disappear from the interface at
/// the slightest unplugging.
/// </summary>
public sealed class DeviceDiscoveryService : IDisposable
{
    /// <summary>
    /// System properties do not change from one scan to the next: rereading
    /// them every time would cost an ADB round trip per device for nothing.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _propertyCache = new(StringComparer.Ordinal);

    private readonly IAdbClient _adb;

    /// <summary>How long a link reading stays valid.</summary>
    private static readonly TimeSpan LinkFreshness = TimeSpan.FromMinutes(1);

    /// <summary>Last link reading per device, with its age.</summary>
    private readonly Dictionary<string, (WifiLink? Link, System.Diagnostics.Stopwatch Vu)> _link =
        new(StringComparer.Ordinal);
    private readonly IDeviceRegistry _registry;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DeviceDiscoveryService(IAdbClient adb, IDeviceRegistry registry)
    {
        _adb = adb;
        _registry = registry;
    }

    /// <summary>Number of property reads carried out in parallel.</summary>
    public int MaxParallelism { get; init; } = 4;

    /// <summary>
    /// Cuts dead wireless connections. The wireless debugging port changes on
    /// every phone restart, and the old connection stays listed as offline
    /// indefinitely. Without this cleanup, the same phone ends up occupying
    /// several entries, one of them dead, and it is sometimes that one that
    /// gets shown.
    /// </summary>
    /// <returns>Number of connections cut.</returns>
    public async Task<int> PruneStaleWirelessTransportsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AdbDeviceEntry> entries;

        try
        {
            entries = await _adb.ListDevicesAsync(detailed: true, cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException)
        {
            // Silence taken deliberately: without an answer from ADB we do not
            // know which devices have disappeared, and forgetting none of them
            // is the right default. ADB's refusal, for its part, is already
            // reported by the scan that follows.
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
                // The connection may have vanished on its own.
            }
        }

        return stale.Count;
    }

    /// <summary>Scans devices and updates the registry.</summary>
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
    /// Cuts every ADB transport that leads to this device.
    ///
    /// The same phone commonly occupies two: the one for its address, and the
    /// one for its mDNS name, which ADB opens on its own when it discovers the
    /// announcement. Measured on the development device, which indeed carried
    /// two. Cutting only one would leave the other open.
    ///
    /// The ADB client lives here and nowhere else in this layer: severing a
    /// pairing needs this cut, and giving it its own client would amount to
    /// scattering ADB access for a single command.
    /// </summary>
    /// <returns>The addresses actually cut, in order.</returns>
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
            // Without an answer from ADB, only the remembered address is left,
            // and trying it beats giving up. ADB's refusal is already reported
            // elsewhere, by the scan.
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

    /// <summary>Last thermal reading per device, with its age.</summary>
    private readonly Dictionary<string, (ThermalReading? Reading, System.Diagnostics.Stopwatch Vu)> _heat =
        new(StringComparer.Ordinal);

    /// <summary>
    /// How long a heat reading stays valid.
    ///
    /// Longer than the link's: a device does not move from one thermal tier to
    /// another in a few seconds, and the question costs a shell round trip.
    /// </summary>
    private static readonly TimeSpan HeatFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What the device says about its heat, or <c>null</c> if it says nothing.
    ///
    /// The question is asked only once a minute per device: that is the pace
    /// at which heat moves, and the caller asks it on every scan without
    /// paying for it.
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
            // Same silence taken deliberately as for the link: not knowing the
            // heat is a valid result, which the caller treats as no
            // constraint. A device that answers a side question poorly must
            // not make the scan fail.
            return null;
        }
    }

    /// <summary>
    /// Is the virtual display scrcpy opened on this device unlocked, or
    /// <c>null</c> if it cannot be found.
    ///
    /// No cache: the question only makes sense once the display has been
    /// created, and it is asked only once per launch.
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

            // Only an actual answer is kept: as long as no display exists, the
            // question has no answer and will need to be asked again. The
            // display group is read from the same dump: asking for it
            // separately would cost a second "dumpsys display", which does
            // not come cheap.
            if (unlocked is not null)
            {
                var now = System.Diagnostics.Stopwatch.StartNew();

                _trusted[serial] = (unlocked, now);
                _grouped[serial] = (VirtualDisplayTrust.HasOwnGroup(dump), now);
            }

            return unlocked;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Same silence taken deliberately as for the other side probes.
            return null;
        }
    }

    /// <summary>
    /// Is the device locked right now, or <c>null</c> if its answer does not
    /// say.
    ///
    /// No cache, and for the same reason as the display's flag: the question
    /// is asked only at launch, and a state that changes within a second is
    /// not worth keeping.
    /// </summary>
    public async Task<bool?> IsDeviceLockedAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        // **Freshness follows the previous answer.** Locked, we ask the
        // question often: the warning must go out right on the heels of
        // unlocking. Unlocked, there is nothing left to turn off, and fifteen
        // questions a minute during a play session would wake up a phone that
        // is already encoding two video streams.
        if (_locked.TryGetValue(serial, out var garde)
            && garde.Vu.Elapsed < (garde.Locked == false ? OpenFreshness : LockFreshness))
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
            // Same silence taken deliberately as for the other side probes.
            return null;
        }
    }

    /// <summary>
    /// Last trust verdict for the display, per device.
    ///
    /// Kept for a long time: this flag says what the device is capable of, and
    /// that does not change from one session to another. One minute is enough
    /// for the panel to be able to ask the question every two seconds.
    /// </summary>
    private readonly Dictionary<string, (bool? Unlocked, System.Diagnostics.Stopwatch Vu)> _trusted =
        new(StringComparer.Ordinal);

    private static readonly TimeSpan TrustFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Does the virtual display have its own group, per device. Read from the
    /// same dump as the trust flag, and kept just as long: it is a capability
    /// of the device, not a state.
    /// </summary>
    private readonly Dictionary<string, (bool? Grouped, System.Diagnostics.Stopwatch Vu)> _grouped =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Can this device keep several accounts active at once, or <c>null</c> as
    /// long as no virtual display has been seen.
    ///
    /// The answer comes from the reading
    /// <see cref="IsVirtualDisplayUnlockedAsync" /> has already taken:
    /// calling that one first is therefore the rule.
    /// </summary>
    public async Task<bool?> HasOwnDisplayGroupAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (_grouped.TryGetValue(serial ?? string.Empty, out var garde)
            && garde.Vu.Elapsed < TrustFreshness)
        {
            return garde.Grouped;
        }

        _ = await IsVirtualDisplayUnlockedAsync(serial!, cancellationToken).ConfigureAwait(false);

        return _grouped.TryGetValue(serial ?? string.Empty, out var frais) ? frais.Grouped : null;
    }

    /// <summary>
    /// Last lock state, per device.
    ///
    /// Kept briefly: this is a state the user changes with one gesture, and
    /// the warning must disappear right on the heels of unlocking.
    /// </summary>
    private readonly Dictionary<string, (bool? Locked, System.Diagnostics.Stopwatch Vu)> _locked =
        new(StringComparer.Ordinal);

    private static readonly TimeSpan LockFreshness = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Once the device is unlocked, the question can wait.
    /// </summary>
    private static readonly TimeSpan OpenFreshness = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Is the game exempt from battery saving, per device.
    ///
    /// Kept for two minutes: this is a setting that gets applied by hand on
    /// the phone, and the warning must go out shortly after, without asking
    /// the question every two seconds for all that.
    /// </summary>
    private readonly Dictionary<string, (bool? Exempt, System.Diagnostics.Stopwatch Vu)> _exempt =
        new(StringComparer.Ordinal);

    private static readonly TimeSpan ExemptFreshness = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Is the game shielded from battery saving on this device, or <c>null</c>
    /// when the device does not say.
    /// </summary>
    public async Task<bool?> IsBatteryExemptAsync(
        string serial,
        string package,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial) || string.IsNullOrWhiteSpace(package))
        {
            return null;
        }

        if (_exempt.TryGetValue(serial, out var garde) && garde.Vu.Elapsed < ExemptFreshness)
        {
            return garde.Exempt;
        }

        try
        {
            var dump = await _adb
                .ShellAsync(
                    serial,
                    ["dumpsys", "deviceidle", "whitelist"],
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var exempt = BatteryExemption.Covers(dump, package);

            _exempt[serial] = (exempt, System.Diagnostics.Stopwatch.StartNew());

            return exempt;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Same silence taken deliberately as for the other side probes.
            return null;
        }
    }

    /// <summary>Last battery level read per device, with its age.</summary>
    private readonly Dictionary<string, (BatteryReading? Reading, System.Diagnostics.Stopwatch Vu)> _battery =
        new(StringComparer.Ordinal);

    /// <summary>
    /// How long a battery reading stays valid.
    ///
    /// The same one minute as heat, and for the same reason: a battery does
    /// not lose ten percent in ten seconds, and the question costs a shell
    /// round trip on every scan.
    /// </summary>
    private static readonly TimeSpan BatteryFreshness = TimeSpan.FromMinutes(1);

    /// <summary>
    /// What the device says about its battery, or <c>null</c> if it says
    /// nothing.
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
            // Same silence taken deliberately as for heat: not knowing the
            // battery is a valid result, and a device that answers a side
            // question poorly must not make the scan fail.
            return null;
        }
    }

    /// <summary>Last free space reading per device, with its age.</summary>
    private readonly Dictionary<string, (StorageReading? Reading, System.Diagnostics.Stopwatch Vu)> _storage =
        new(StringComparer.Ordinal);

    /// <summary>
    /// How long a free space reading stays valid.
    ///
    /// Much longer than the others: free space does not move during a session,
    /// and this is an insurance check, not a monitor.
    /// </summary>
    private static readonly TimeSpan StorageFreshness = TimeSpan.FromMinutes(15);

    /// <summary>
    /// What the device says about its free space, or <c>null</c> if it says
    /// nothing.
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
            // Same silence taken deliberately as for the other side probes.
            return null;
        }
    }

    /// <summary>
    /// Asks the device whether it accepts input simulation.
    ///
    /// The symptom it sheds light on is silent by nature: the picture shows,
    /// the window opens, and the click does nothing without any error
    /// appearing. The probe sends the "unknown" key, which triggers nothing,
    /// and reads what the system answers.
    ///
    /// It is only run at the user's request. The timeout is short: a question
    /// the device does not answer quickly will not answer at all.
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
            // A device that no longer answers teaches nothing about the mouse,
            // and saying so would come down to guesswork. Its absence is
            // already reported elsewhere, by the scan.
            return InputInjection.Unknown;
        }
    }

    /// <summary>Timeout given to the input probe.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What the device says about its Wi-Fi link, or <c>null</c> if it has
    /// nothing to say: a USB link, Wi-Fi turned off, or Android answering
    /// differently.
    ///
    /// Kept for one minute, no longer: a network changes, and it is precisely
    /// when it changes that it needs rereading. But the call costs 0.46 s
    /// measured, it sits on the path to opening a session, and opening two
    /// accounts back to back used to pay for it twice for the same answer.
    ///
    /// No fault is propagated. Not knowing the link must never prevent a
    /// session from opening: the caller treats the absence as no constraint.
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
            // Silence taken deliberately, and this is the one place here where
            // it is justified: not knowing the link is already a valid result,
            // which the caller treats as no constraint. Failing a launch
            // because a device answers a side question poorly would be out of
            // all proportion.
            return null;
        }
    }

    /// <summary>
    /// Forces properties to be reread on the next scan, for instance after an
    /// Android update or a device name change.
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
            // Without ADB, we at least show what had been remembered rather
            // than an empty screen.
            warnings.Add(exception.UserMessage);
            var stored = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);
            return new DeviceDiscoveryResult(stored, warnings);
        }

        // Remembered and discarded devices come together: the scan needs both,
        // and the registry rereads its file on every request.
        var (known, discarded) = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        // Emulators are not this tool's target and would clutter the list:
        // they are discarded as soon as they are discovered.
        //
        // Duplicates too, and before any querying: ADB joins a phone on its
        // own by mDNS when it is already connected through its address, and
        // the mDNS name then refuses the commands sent to it.
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

        // The same phone can appear twice, plugged in over USB and still
        // connected over Wi-Fi. We keep the better of the two entries.
        var deduplicated = Deduplicate(discovered);

        // A discarded device drops out of the entire scan, not just out of the
        // write to the registry. The ADB disconnect does not hold forever: the
        // server rejoins on its own a phone that announces itself and whose
        // key it has kept, whether the server restarts or wireless debugging
        // gets turned back on. Filtering only the registry used to let the
        // entry come back on screen, which is the reported bug.
        var merged = discarded.Count == 0
            ? deduplicated
            : deduplicated.Where(d => !discarded.Contains(d.Id)).ToList();

        await _registry.UpsertRangeAsync(merged, cancellationToken).ConfigureAwait(false);

        var seen = merged.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        // What is remembered is filtered like what is discovered: a discarded
        // device has no business coming back through the offline list.
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
                // A phone that refuses getprop stays listed: it is precisely
                // so it can be shown that we can explain what to do.
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
    /// Merges identity duplicates, keeping the most useful entry: a ready
    /// transport outranks an offline one, and USB outranks Wi-Fi because it is
    /// more stable.
    /// </summary>
    private static List<AndroidDevice> Deduplicate(IEnumerable<AndroidDevice> devices) =>
        [.. devices
            .GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(d => d.IsConnected)
                .ThenByDescending(d => d.ConnectionKind == AdbConnectionKind.Usb)

                // A reachable address before an mDNS name. Without this
                // tie-breaker, two equally connected wireless entries used to
                // be decided by the order ADB returns them in, that is, by
                // their transport number: the entry kept would change from one
                // reconnection to the next, and it was sometimes the one that
                // refuses commands.
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
