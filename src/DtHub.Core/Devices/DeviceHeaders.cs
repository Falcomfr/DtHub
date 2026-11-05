namespace DtHub.Core.Devices;

/// <summary>
/// Decides where to place a device name in a flat list of
/// instances.
///
/// The name only appears where the device changes: two consecutive
/// instances of the same phone carry only one, and a phone split
/// into two pieces by an instance from elsewhere gets one per
/// piece.
/// </summary>
public static class DeviceHeaders
{
    /// <summary>
    /// For each position, true if the row opens a run of instances
    /// of the same device and must therefore carry its name.
    /// </summary>
    public static IReadOnlyList<bool> For(IReadOnlyList<string> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);

        var headers = new bool[deviceIds.Count];

        for (var i = 0; i < deviceIds.Count; i++)
        {
            headers[i] = i == 0
                || !string.Equals(deviceIds[i], deviceIds[i - 1], StringComparison.Ordinal);
        }

        return headers;
    }

    /// <summary>
    /// For each position, true if it is the first piece of that
    /// device in the list.
    ///
    /// What applies to the device itself, and not to the run of
    /// instances, is only shown there: the button that breaks the
    /// pairing has no reason to appear twice for the same phone.
    /// </summary>
    public static IReadOnlyList<bool> FirstOccurrences(IReadOnlyList<string> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var first = new bool[deviceIds.Count];

        for (var i = 0; i < deviceIds.Count; i++)
        {
            first[i] = seen.Add(deviceIds[i]);
        }

        return first;
    }

    /// <summary>
    /// For each position, true if the row opens a later run of a
    /// device already shown above. Those runs get a slim "continued"
    /// line: a second full header was read as a second phone
    /// (2026-11-05).
    /// </summary>
    public static IReadOnlyList<bool> Continuations(IReadOnlyList<string> deviceIds)
    {
        var headers = For(deviceIds);
        var firsts = FirstOccurrences(deviceIds);

        return [.. headers.Select((header, i) => header && !firsts[i])];
    }
}
