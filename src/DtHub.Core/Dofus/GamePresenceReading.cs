using DtHub.Core.Devices;

namespace DtHub.Core.Dofus;

/// <summary>
/// What each phone is known to carry, after one pass of the account
/// search.
///
/// **The defect this type exists to catch: an empty list read as an
/// answer.** The accounts list invalidates its cache on ten ordinary
/// gestures, Launch and Restart and Stop among them. On the next tick
/// the display was handed <c>[]</c>, and the code that decides asked it
/// "which phones carry the game?" The answer was "none", so both phones
/// were stamped "Game not installed" in orange for the 2.9 seconds the
/// real search takes, then snapped back. That is what the user saw
/// several times a minute.
///
/// **The rule, in one sentence: a pass that does not know writes no
/// verdict.** A null list means the search has not answered; the
/// verdict already rendered simply stays. Making that state a value of
/// its own is what makes "I do not know" impossible to confuse with
/// "there is no game", which no pair of booleans ever managed.
///
/// **An offline phone is not questioned, so nothing is read into its
/// silence.** <see cref="DofusInstanceService" /> only asks connected
/// devices. Concluding from the absence of an answer nobody asked for
/// is the same fault in a second disguise.
/// </summary>
public static class GamePresenceReading
{
    /// <summary>
    /// The verdict for every device now on screen, given what was known
    /// before and what this pass found.
    ///
    /// The result holds exactly one entry per device in
    /// <paramref name="devices" />: a phone that is gone leaves no
    /// verdict behind, which is the pruning the previous arrangement
    /// never did.
    /// </summary>
    /// <param name="known">The verdicts rendered so far, by device id.</param>
    /// <param name="devices">The devices discovery just saw.</param>
    /// <param name="instances">
    /// The accounts the search found, or <c>null</c> when it has not
    /// answered. An empty list is an answer; <c>null</c> is not.
    /// </param>
    public static Dictionary<string, GamePresence> After(
        IReadOnlyDictionary<string, GamePresence> known,
        IReadOnlyList<AndroidDevice> devices,
        IReadOnlyList<DofusInstance>? instances)
    {
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(devices);

        var read = new Dictionary<string, GamePresence>(StringComparer.Ordinal);

        var withGame = instances is null
            ? null
            : instances.Select(i => i.DeviceId).ToHashSet(StringComparer.Ordinal);

        foreach (var device in devices)
        {
            read[device.Id] = withGame is null || !device.IsConnected
                ? Kept(known, device.Id)
                : withGame.Contains(device.Id) ? GamePresence.Present : GamePresence.Absent;
        }

        return read;
    }

    private static GamePresence Kept(IReadOnlyDictionary<string, GamePresence> known, string id) =>
        known.TryGetValue(id, out var verdict) ? verdict : GamePresence.Unknown;
}
