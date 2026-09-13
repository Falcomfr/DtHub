using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>How a device was found again, or why it was not.</summary>
public enum ReconnectOutcome
{
    /// <summary>Already connected: nothing was attempted.</summary>
    AlreadyConnected,

    /// <summary>Reconnected to the last remembered address.</summary>
    ReconnectedToLastAddress,

    /// <summary>Reconnected to an address discovered by mDNS.</summary>
    ReconnectedByDiscovery,

    /// <summary>
    /// Not found: phone off, off the network, or debugging disabled.
    /// </summary>
    NotFound,

    /// <summary>
    /// Found, reachable, and it refuses this PC: it no longer
    /// recognizes its key. Only a new pairing fixes it.
    /// </summary>
    RefusedByDevice,
}

/// <summary>
/// Automatic reconnection at startup. The order follows increasing
/// cost: what is already connected, then the last known address, then
/// network discovery. The user has nothing to re-enter.
/// </summary>
public sealed class DeviceReconnectService
{
    private readonly IAdbClient _adb;
    private readonly TimeSpan _directAttempt;

    /// <param name="adb">The ADB client.</param>
    /// <param name="directAttempt">
    /// What is granted to the last known address before moving on to
    /// the scan. The production value is that of
    /// <see cref="DirectAttempt" />; tests shorten it so as not to
    /// wait for real.
    /// </param>
    public DeviceReconnectService(IAdbClient adb, TimeSpan? directAttempt = null) =>
        (_adb, _directAttempt) = (adb, directAttempt ?? DirectAttempt);

    /// <summary>
    /// What is granted to the last known address before moving on to
    /// the scan. Twenty-five times the measured time of a connection
    /// that succeeds.
    /// </summary>
    private static readonly TimeSpan DirectAttempt = TimeSpan.FromSeconds(5);

    /// <summary>Tries to find a remembered device again.</summary>
    public async Task<ReconnectOutcome> TryReconnectAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (await IsAlreadyConnectedAsync(device, cancellationToken).ConfigureAwait(false))
        {
            return ReconnectOutcome.AlreadyConnected;
        }

        var refused = false;

        // The wireless debugging port changes at every restart of
        // the phone: the last known address often fails, but it is
        // almost free to try and avoids an mDNS scan when it works.
        //
        // Almost, and that is the whole point of the deadline.
        // Measured: a device that answers connects in 190 ms, a
        // closed port gives control back in two seconds, but a phone
        // that is off leaves the system waiting twenty-two seconds
        // for the answer of a machine that will never answer. The
        // device list used to wait through all of that.
        if (device.LastKnownAddress is { Length: > 0 } address && device.LastKnownPort is > 0)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            deadline.CancelAfter(_directAttempt);

            try
            {
                var direct = await _adb
                    .ConnectAsync(address, device.LastKnownPort.Value, deadline.Token)
                    .ConfigureAwait(false);

                if (direct.Succeeded)
                {
                    return ReconnectOutcome.ReconnectedToLastAddress;
                }

                refused = AdbConnectFailure.MeansRefusedKey(direct.FailureReason);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The deadline has spoken, not the user: moving on to
                // the mDNS scan, which knows how to find a device
                // whose port has changed.
            }
        }

        var discovered = await FindByDiscoveryAsync(device, cancellationToken).ConfigureAwait(false);

        if (discovered is null)
        {
            // The device no longer announces itself, but it refused
            // this PC just before: it is the refusal that explains
            // it, not the silence.
            return refused ? ReconnectOutcome.RefusedByDevice : ReconnectOutcome.NotFound;
        }

        var connect = await _adb.ConnectAsync(discovered.Host, discovered.Port, cancellationToken)
            .ConfigureAwait(false);

        if (connect.Succeeded)
        {
            return ReconnectOutcome.ReconnectedByDiscovery;
        }

        return refused || AdbConnectFailure.MeansRefusedKey(connect.FailureReason)
            ? ReconnectOutcome.RefusedByDevice
            : ReconnectOutcome.NotFound;
    }

    /// <summary>
    /// Tries to find several devices again, all at the same time.
    ///
    /// **They used to be sequential, on the grounds that ADB
    /// serializes connections anyway. Measurement says otherwise**:
    /// two connections to absent devices take 19.3 s together,
    /// against 22 s for a single one. They do not get in each other's
    /// way. In sequence, every phone that was off added its wait to
    /// that of the others, and the device list waited for the sum.
    ///
    /// Every known device is tried, with no filter beforehand: safety
    /// comes from matching the serial number against the announced
    /// service name, not from a flag that may be missing on a device
    /// remembered by an earlier version.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, ReconnectOutcome>> TryReconnectAllAsync(
        IEnumerable<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        cancellationToken.ThrowIfCancellationRequested();

        var attempts = devices
            .Select(async device =>
            {
                try
                {
                    return (device.Id, Outcome: await TryReconnectAsync(device, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (AdbException)
                {
                    // An unreachable device must not interrupt the
                    // recovery of the others.
                    return (device.Id, Outcome: ReconnectOutcome.NotFound);
                }
            })
            .ToList();

        var results = await Task.WhenAll(attempts).ConfigureAwait(false);

        var outcomes = new Dictionary<string, ReconnectOutcome>(StringComparer.Ordinal);

        foreach (var (id, outcome) in results)
        {
            outcomes[id] = outcome;
        }

        return outcomes;
    }

    private async Task<bool> IsAlreadyConnectedAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        var entries = await _adb.ListDevicesAsync(detailed: false, cancellationToken).ConfigureAwait(false);

        return entries.Any(e => e.IsReady
            && (string.Equals(e.Serial, device.Serial, StringComparison.Ordinal)
                || string.Equals(e.Serial, device.ReconnectAddress, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Looks for the device's mDNS announcement. The service name
    /// contains the hardware serial number, which prevents connecting
    /// to the neighbor's phone.
    /// </summary>
    private async Task<MdnsService?> FindByDiscoveryAsync(AndroidDevice device, CancellationToken cancellationToken)
    {
        var services = await _adb.ListMdnsServicesAsync(cancellationToken).ConfigureAwait(false);
        var connectServices = services.Where(s => s.IsConnect).ToList();

        var byIdentity = connectServices.FirstOrDefault(s => s.MatchesSerial(device.Id));
        if (byIdentity is not null)
        {
            return byIdentity;
        }

        var bySerial = connectServices.FirstOrDefault(s => s.MatchesSerial(device.Serial));
        if (bySerial is not null)
        {
            return bySerial;
        }

        // Fallback: same host as last time, different port. This is
        // the common case after a phone restart on a network with
        // fixed leases.
        return device.LastKnownAddress is { Length: > 0 } address
            ? connectServices.FirstOrDefault(s => string.Equals(s.Host, address, StringComparison.Ordinal))
            : null;
    }
}
