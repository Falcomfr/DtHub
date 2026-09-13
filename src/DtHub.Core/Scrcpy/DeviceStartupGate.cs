using System.Collections.Concurrent;

namespace DtHub.Core.Scrcpy;

/// <summary>A device has just changed its busy state.</summary>
public sealed class DeviceBusyChangedEventArgs(string deviceId, bool isBusy) : EventArgs
{
    /// <summary>Device whose state has just changed.</summary>
    public string DeviceId { get; } = deviceId;

    /// <summary>True while an opening is in progress on this device.</summary>
    public bool IsBusy { get; } = isBusy;
}

/// <summary>
/// Serializes session openings device by device, and announces which
/// ones are busy.
///
/// Two openings that overlap on the same phone break: the first one
/// dies on "Server connection failed" even though it had already
/// pushed its server. Two different phones, on the other hand, have
/// no reason to wait for each other: the lock is therefore per
/// device, never global.
/// </summary>
public sealed class DeviceStartupGate : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DeviceStartupGate(Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    /// <summary>
    /// Rest left after an opening, before allowing another one on
    /// the same device. Zero by default: the lock already imposes
    /// the spacing of a full opening, and a made up figure would be
    /// worthless.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.Zero;

    /// <summary>Raised on every acquisition and every release.</summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? BusyChanged;

    /// <summary>True if an opening is in progress on this device.</summary>
    public bool IsBusy(string deviceId)
    {
        lock (_busy)
        {
            return _busy.Contains(deviceId);
        }
    }

    /// <summary>
    /// Waits its turn on this device. The returned token releases the
    /// slot, after the rest period, when it is disposed.
    /// </summary>
    public async Task<IAsyncDisposable> EnterAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(deviceId) ? "?" : deviceId;
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        Mark(key, busy: true);

        return new Lease(this, key, gate);
    }

    public void Dispose()
    {
        foreach (var gate in _gates.Values)
        {
            gate.Dispose();
        }

        _gates.Clear();
    }

    private void Mark(string deviceId, bool busy)
    {
        lock (_busy)
        {
            if (busy ? !_busy.Add(deviceId) : !_busy.Remove(deviceId))
            {
                return;
            }
        }

        BusyChanged?.Invoke(this, new DeviceBusyChangedEventArgs(deviceId, busy));
    }

    /// <summary>
    /// Slot reserved on a device. The rest period is applied before
    /// releasing the slot, not after: the next one therefore really
    /// waits, and the activity indicator stays lit during that time.
    /// </summary>
    private sealed class Lease(DeviceStartupGate gate, string deviceId, SemaphoreSlim semaphore)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (gate.Cooldown > TimeSpan.Zero)
                {
                    await gate._delay(gate.Cooldown, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Mark(deviceId, busy: false);
                semaphore.Release();
            }
        }
    }
}
