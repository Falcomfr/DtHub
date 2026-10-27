namespace DtHub.Core.Devices;

/// <summary>
/// The Wi-Fi latency spikes of one phone over the last minute, from pings
/// the PC sends it.
///
/// **Why it exists.** On 2026-09-29 and again on 2026-10-03 the game lagged
/// by fits while every reading was fine: pings to the phone answered in
/// 3 ms, and every ten to forty seconds in 205 to 210 ms, two Wi-Fi beacons,
/// the radio dozing. The retry share is only read at launch and cannot see
/// that. A ping is a few dozen bytes and needs no adb.
///
/// Fed from a pool thread and read from the sweep: guarded by a lock.
/// </summary>
public sealed class PingSpikes
{
    /// <summary>The answer time from which a ping counts as a spike.</summary>
    public const int SpikeMs = 100;

    /// <summary>
    /// Spikes in a minute from which they are named, counted once per
    /// <see cref="Bucket" />: three lost pings in a row are one hiccup, and
    /// only a radio that keeps dozing is felt in game.
    /// </summary>
    public const int Enough = 3;

    /// <summary>The span within which several spikes count as one.</summary>
    private const int Bucket = 10;

    /// <summary>How long a ping is waited for. A lost one counts as this long.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Silence after which the link counts as dead. A link that dies without
    /// a word left scrcpy waiting for frames, the window grey and nothing
    /// said, until some adb read happened to notice (13 to 20 s measured on
    /// 2026-10-26, and nothing bounded it).
    /// </summary>
    public static readonly TimeSpan LinkLostAfter = TimeSpan.FromSeconds(15);

    private readonly Queue<(DateTimeOffset At, long Ms)> _spikes = new();

    private readonly Lock _gate = new();

    private DateTimeOffset? _lastAnswer;

    /// <summary>
    /// True once the phone answered and then stayed silent for
    /// <see cref="LinkLostAfter" />. Never for one that never answered: a
    /// network that drops pings must not close a window that works.
    /// </summary>
    public bool LinkLost(DateTimeOffset now) => SilentFor(now, LinkLostAfter);

    /// <summary>
    /// Silence after which the window shows the player it waits for the
    /// phone, before the link counts as lost: a hiccup comes back within it.
    /// </summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// True once the phone answered and then stayed silent for
    /// <see cref="SilentAfter" />.
    /// </summary>
    public bool Silent(DateTimeOffset now) => SilentFor(now, SilentAfter);

    private bool SilentFor(DateTimeOffset now, TimeSpan span)
    {
        lock (_gate)
        {
            return _lastAnswer is { } last && now - last >= span;
        }
    }

    /// <summary>
    /// The address to ping for an adb serial of the form "a.b.c.d:port", or
    /// <c>null</c>: a USB serial has none, and an mDNS name would need
    /// resolving first, which the phones of the field do not need.
    /// </summary>
    public static string? HostOf(string? serial) =>
        serial?.Split(':') is [var host, var port]
            && host.Count(c => c == '.') == 3
            && System.Net.IPAddress.TryParse(host, out _)
            && int.TryParse(port, out _)
            ? host
            : null;

    /// <summary>Records one ping: its answer time, or <c>null</c> if lost.</summary>
    public void Add(DateTimeOffset at, long? milliseconds)
    {
        var ms = milliseconds ?? (long)Timeout.TotalMilliseconds;

        if (milliseconds is not null)
        {
            lock (_gate)
            {
                // Pings answer out of order: a late one must not set it back.
                if (_lastAnswer is null || at > _lastAnswer)
                {
                    _lastAnswer = at;
                }
            }
        }

        if (ms < SpikeMs)
        {
            return;
        }

        lock (_gate)
        {
            // Pruned here too: with the panel hidden nothing reads them.
            Prune(at);
            _spikes.Enqueue((at, ms));
        }
    }

    /// <summary>
    /// The spikes of the minute before <paramref name="now" />, one per ten
    /// seconds at most, and the worst.
    /// </summary>
    public (int Count, long Worst) Since(DateTimeOffset now)
    {
        lock (_gate)
        {
            Prune(now);

            return (
                _spikes.Select(s => s.At.ToUnixTimeSeconds() / Bucket).Distinct().Count(),
                _spikes.Count == 0 ? 0 : _spikes.Max(s => s.Ms));
        }
    }

    private void Prune(DateTimeOffset now)
    {
        while (_spikes.Count > 0 && now - _spikes.Peek().At > Window)
        {
            _ = _spikes.Dequeue();
        }
    }
}
