namespace DtHub.Core.Devices;

/// <summary>
/// Décide où poser un nom d'appareil dans une liste plate d'instances.
///
/// Le nom n'apparaît que là où l'appareil change : deux instances du même
/// téléphone qui se suivent n'en portent qu'un, et un téléphone coupé en deux
/// morceaux par une instance venue d'ailleurs en reçoit un par morceau.
/// </summary>
public static class DeviceHeaders
{
    /// <summary>
    /// Pour chaque position, vrai si la ligne ouvre une suite d'instances d'un
    /// même appareil et doit donc en porter le nom.
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
    /// Pour chaque position, vrai s'il s'agit du premier morceau de cet
    /// appareil dans la liste.
    ///
    /// Ce qui vaut pour l'appareil lui-même, et non pour la suite d'instances,
    /// ne s'affiche que là : le bouton qui rompt l'association n'a aucune
    /// raison de paraître deux fois pour le même téléphone.
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
}
