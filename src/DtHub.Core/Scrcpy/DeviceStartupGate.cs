using System.Collections.Concurrent;

namespace DtHub.Core.Scrcpy;

/// <summary>Un appareil vient de changer d'état d'occupation.</summary>
public sealed class DeviceBusyChangedEventArgs(string deviceId, bool isBusy) : EventArgs
{
    /// <summary>Appareil dont l'état vient de changer.</summary>
    public string DeviceId { get; } = deviceId;

    /// <summary>Vrai tant qu'une ouverture est en cours sur cet appareil.</summary>
    public bool IsBusy { get; } = isBusy;
}

/// <summary>
/// Sérialise les ouvertures de session appareil par appareil, et annonce
/// lesquels sont occupés.
///
/// Deux ouvertures qui se chevauchent sur un même téléphone se cassent : la
/// première meurt sur « Server connection failed » alors qu'elle avait déjà
/// poussé son serveur. Deux téléphones différents, eux, n'ont aucune raison de
/// s'attendre : le verrou est donc par appareil, jamais global.
/// </summary>
public sealed class DeviceStartupGate : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _busy = new(StringComparer.Ordinal);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DeviceStartupGate(Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));

    /// <summary>
    /// Repos laissé après une ouverture, avant d'en autoriser une autre sur le
    /// même appareil. Nul par défaut : le verrou impose déjà l'espacement d'une
    /// ouverture complète, et un chiffre inventé ne vaudrait rien.
    /// </summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.Zero;

    /// <summary>Signalé à chaque prise et à chaque libération.</summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? BusyChanged;

    /// <summary>Vrai si une ouverture est en cours sur cet appareil.</summary>
    public bool IsBusy(string deviceId)
    {
        lock (_busy)
        {
            return _busy.Contains(deviceId);
        }
    }

    /// <summary>
    /// Attend son tour sur cet appareil. Le jeton rendu libère la place, après
    /// le repos, quand il est disposé.
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
    /// Place réservée sur un appareil. Le repos est appliqué avant de rendre la
    /// place, et non après : le suivant attend donc réellement, et l'indicateur
    /// d'activité reste allumé pendant ce temps.
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
