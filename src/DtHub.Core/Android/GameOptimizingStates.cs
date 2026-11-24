using System.Collections.Concurrent;

namespace DtHub.Core.Android;

/// <summary>
/// Samsung's game optimizing service per connected phone, by device id (D237).
/// A pending or failed read holds no state; a failed one is let go so the next
/// sweep asks again, and a phone forgotten meanwhile is not written back.
/// </summary>
public sealed class GameOptimizingStates
{
    private readonly ConcurrentDictionary<string, GameOptimizingState?> _states = new(StringComparer.Ordinal);

    public GameOptimizingState? Of(string deviceId) => _states.GetValueOrDefault(deviceId);

    /// <summary>True when this phone should be read now: not known, not being read.</summary>
    public bool Begin(string deviceId) => _states.TryAdd(deviceId, null);

    /// <summary>Ends a read begun by <see cref="Begin" />.</summary>
    public void Finish(string deviceId, GameOptimizingState? state)
    {
        if (state is { } known)
        {
            _ = _states.TryUpdate(deviceId, known, null);
        }
        else
        {
            _ = _states.TryRemove(KeyValuePair.Create(deviceId, (GameOptimizingState?)null));
        }
    }

    /// <summary>The state read back after a change made from DT Hub.</summary>
    public void Set(string deviceId, GameOptimizingState state) => _states[deviceId] = state;

    public void Forget(string deviceId) => _ = _states.TryRemove(deviceId, out _);

    public IReadOnlyList<string> Known => [.. _states.Keys];
}
