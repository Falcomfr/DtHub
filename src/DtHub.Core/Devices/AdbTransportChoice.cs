using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Chooses, among the transports ADB advertises, the ones worth
/// querying.
///
/// The same phone gets attached twice as soon as ADB reaches it on
/// its own over mDNS while it is already connected by its address:
/// one line "192.168.1.14:40187" and one line
/// "adb-XXXXXXXX-XXXXXX._adb-tls-connect._tcp", both "device", the
/// same address behind them. This was thought impossible: the
/// comment on <see cref="MdnsDeviceName"/> assumed the device
/// appears under its address when it is reachable and under this
/// name when it is not. It appears under both.
///
/// This is not harmless. The mDNS name is a poor target for
/// commands: the logs count twenty refusals in four days, "device
/// 'adb-...' not found" on getprop, on pm list users, on activity
/// resolution. And any command with no target fails with "more than
/// one device".
///
/// The name keeps its use: it identifies the device and serves to
/// connect to it. It is only discarded when a reachable address
/// designates the same phone within the same listing.
/// </summary>
public static class AdbTransportChoice
{
    /// <summary>
    /// The transports to query, in the order ADB returned them.
    ///
    /// A device we cannot recognize keeps both its lines: better an
    /// extra line than a phone that disappears from the list because
    /// it was mistaken for another.
    /// </summary>
    public static IReadOnlyList<AdbDeviceEntry> WithoutDoubles(
        IReadOnlyList<AdbDeviceEntry> entries,
        IEnumerable<AndroidDevice> known)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(known);

        var candidates = known as IReadOnlyCollection<AndroidDevice> ?? [.. known];

        HashSet<string> routable = new(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // A dead address line hides nothing: after a restart of wireless
            // debugging the old port lingers offline, and the mDNS name is
            // the line that works.
            if (MdnsDeviceName.IsMdnsName(entry.Serial) || !entry.IsReady)
            {
                continue;
            }

            if (Identity(entry, candidates) is { Length: > 0 } identity)
            {
                routable.Add(identity);
            }
        }

        if (routable.Count == 0)
        {
            return entries;
        }

        return
        [
            .. entries.Where(e =>
                !MdnsDeviceName.IsMdnsName(e.Serial)
                || Identity(e, candidates) is not { Length: > 0 } identity
                || !routable.Contains(identity)),
        ];
    }

    /// <summary>
    /// What already reaches each connected phone, for the announcement scan
    /// to leave alone.
    ///
    /// A phone held only under its mDNS name does not count by its serial
    /// number: its announcement then connects its address. Otherwise its
    /// remembered port was never refreshed, and after a Wi-Fi drop the
    /// reconnection knocked at one closed since. The phone only came back
    /// once it announced itself again, two minutes later on 2026-10-26.
    /// </summary>
    public static IReadOnlySet<string> ReachedByAddress(IEnumerable<AndroidDevice> connected)
    {
        ArgumentNullException.ThrowIfNull(connected);

        return connected
            .SelectMany(d => MdnsDeviceName.IsMdnsName(d.Serial) ? [d.Serial] : new[] { d.Serial, d.Id })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Which phone this transport belongs to, as far as it can be
    /// told without querying it: the serial number carried by an
    /// mDNS name, or the remembered device this line designates.
    /// </summary>
    private static string? Identity(AdbDeviceEntry entry, IReadOnlyCollection<AndroidDevice> known) =>
        MdnsDeviceName.HardwareSerialFrom(entry.Serial)
        ?? DeviceFactory.Match(known, entry)?.Id;
}
