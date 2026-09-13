namespace DtHub.Core.Settings;

/// <summary>
/// Holds the order of instances in the settings.
///
/// The order is global and free: an instance can place itself between
/// two instances of another device. It fits entirely in
/// <see cref="StoredInstance.Order"/>, a dense rank from 0 to n-1, so
/// much so that a simple sort on this rank yields the wanted order.
///
/// Moves are expressed by keys and not by offset: the displayed list
/// only shows reachable devices, whereas the settings carry all
/// instances. An offset counted on the visible positions would point
/// to the wrong destination as soon as a hidden instance slips in
/// between.
///
/// All functions are pure: they only touch the document received.
/// </summary>
public static class InstanceOrdering
{
    /// <summary>
    /// Tightens the ranks to 0 through n-1, in the current order.
    ///
    /// Ranks were becoming sparse and could collide: they were
    /// assigned once and for all at discovery, without ever being
    /// renumbered after a device was forgotten. Two instances with
    /// the same rank left the order depending on insertion order.
    /// </summary>
    public static void Normalize(AppSettingsDocument settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Reseat([.. settings.Instances.OrderBy(i => i.Order)]);
    }

    /// <summary>
    /// Places an instance just before or just after another, whatever
    /// their device.
    /// </summary>
    /// <returns>False if a key is unknown or if nothing moves.</returns>
    public static bool MoveInstance(
        AppSettingsDocument settings,
        string key,
        string targetKey,
        bool above)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var ordered = settings.Instances.OrderBy(i => i.Order).ToList();

        var from = ordered.FindIndex(i => string.Equals(i.Key, key, StringComparison.Ordinal));
        var onto = ordered.FindIndex(i => string.Equals(i.Key, targetKey, StringComparison.Ordinal));

        if (from < 0 || onto < 0 || from == onto)
        {
            return false;
        }

        var destination = above ? onto : onto + 1;

        // Removing the instance shifts everything that followed it
        // by one rank.
        if (from < destination)
        {
            destination--;
        }

        if (destination == from)
        {
            return false;
        }

        var moved = ordered[from];
        ordered.RemoveAt(from);
        ordered.Insert(destination, moved);

        Reseat(ordered);

        return true;
    }

    /// <summary>
    /// Adds a discovered instance: after those of its device if it
    /// already has some, otherwise at the end of the list.
    ///
    /// A new instance must appear near its siblings rather than at
    /// the end of a long list, where it would go unseen.
    /// </summary>
    public static void Add(AppSettingsDocument settings, StoredInstance instance)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(instance);

        var ordered = settings.Instances.OrderBy(i => i.Order).ToList();

        var last = ordered.FindLastIndex(
            i => string.Equals(i.DeviceId, instance.DeviceId, StringComparison.Ordinal));

        ordered.Insert(last < 0 ? ordered.Count : last + 1, instance);
        settings.Instances.Add(instance);

        Reseat(ordered);
    }

    private static void Reseat(List<StoredInstance> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
    }
}
