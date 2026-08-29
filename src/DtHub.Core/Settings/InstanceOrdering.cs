namespace DtHub.Core.Settings;

/// <summary>
/// Tient l'ordre des appareils et des instances dans les réglages.
///
/// L'ordre est porté par deux données complémentaires : la liste des appareils
/// dans <see cref="AppSettingsDocument.DeviceOrder"/>, et le rang global de
/// chaque instance dans <see cref="StoredInstance.Order"/>. La seconde est
/// dérivée de la première, ce qui permet à un simple tri sur le rang de rendre
/// l'ordre voulu sans avoir à consulter la liste des appareils.
///
/// Toutes les fonctions sont pures : elles ne touchent qu'au document reçu.
/// </summary>
public static class InstanceOrdering
{
    /// <summary>
    /// Ordre des appareils, complété par ceux que la liste mémorisée ignore.
    /// Un appareil découvert depuis le dernier enregistrement passe à la fin,
    /// dans l'ordre de ses instances.
    /// </summary>
    public static IReadOnlyList<string> ResolveDeviceOrder(AppSettingsDocument settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var present = settings.Instances
            .OrderBy(i => i.Order)
            .Select(i => i.DeviceId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var ordered = settings.DeviceOrder
            .Where(id => present.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        ordered.AddRange(present.Where(id => !ordered.Contains(id, StringComparer.Ordinal)));

        return ordered;
    }

    /// <summary>
    /// Resserre les rangs en 0 à n-1 et met la liste des appareils en accord
    /// avec ce qui existe réellement.
    ///
    /// Les rangs devenaient creux, et pouvaient entrer en collision : ils
    /// étaient attribués une fois pour toutes à la découverte, sans jamais
    /// être renumérotés après l'oubli d'un appareil. Deux instances de même
    /// rang laissaient l'ordre dépendre de l'ordre d'insertion.
    /// </summary>
    public static void Normalize(AppSettingsDocument settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var devices = ResolveDeviceOrder(settings);
        var rank = 0;

        foreach (var deviceId in devices)
        {
            foreach (var instance in InstancesOf(settings, deviceId))
            {
                instance.Order = rank++;
            }
        }

        settings.DeviceOrder = [.. devices];
    }

    /// <summary>
    /// Décale une instance à l'intérieur de son appareil. Elle n'en sort
    /// jamais : mélanger les instances de deux téléphones dans une même liste
    /// n'aurait pas de sens à l'écran, où elles sont groupées par appareil.
    /// Rend faux si rien n'a bougé.
    /// </summary>
    public static bool MoveInstance(AppSettingsDocument settings, string key, int offset)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var instance = settings.Instances.Find(i => string.Equals(i.Key, key, StringComparison.Ordinal));

        if (instance is null || offset == 0)
        {
            return false;
        }

        var siblings = InstancesOf(settings, instance.DeviceId).ToList();
        var from = siblings.IndexOf(instance);
        var to = from + offset;

        if (to < 0 || to >= siblings.Count)
        {
            return false;
        }

        siblings.RemoveAt(from);
        siblings.Insert(to, instance);

        Reseat(settings, instance.DeviceId, siblings);
        Normalize(settings);

        return true;
    }

    /// <summary>
    /// Décale un appareil, ses instances suivant en bloc. Rend faux s'il est
    /// déjà à l'extrémité.
    /// </summary>
    public static bool MoveDevice(AppSettingsDocument settings, string deviceId, int offset)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var devices = ResolveDeviceOrder(settings).ToList();
        var from = devices.FindIndex(id => string.Equals(id, deviceId, StringComparison.Ordinal));

        if (from < 0 || offset == 0)
        {
            return false;
        }

        var to = from + offset;

        if (to < 0 || to >= devices.Count)
        {
            return false;
        }

        devices.RemoveAt(from);
        devices.Insert(to, deviceId);

        settings.DeviceOrder = devices;
        Normalize(settings);

        return true;
    }

    /// <summary>Fixe l'ordre des instances d'un appareil, par leurs clés.</summary>
    public static void ReorderInstances(
        AppSettingsDocument settings,
        string deviceId,
        IReadOnlyList<string> orderedKeys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(orderedKeys);

        var siblings = InstancesOf(settings, deviceId).ToList();

        var reordered = orderedKeys
            .Select(k => siblings.Find(i => string.Equals(i.Key, k, StringComparison.Ordinal)))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        // Une clé oubliée par l'appelant ne doit pas faire disparaître son
        // instance : elle reprend sa place à la suite.
        reordered.AddRange(siblings.Where(i => !reordered.Contains(i)));

        Reseat(settings, deviceId, reordered);
        Normalize(settings);
    }

    /// <summary>Fixe l'ordre des appareils. Les inconnus sont ignorés.</summary>
    public static void ReorderDevices(AppSettingsDocument settings, IReadOnlyList<string> orderedDeviceIds)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(orderedDeviceIds);

        settings.DeviceOrder = [.. orderedDeviceIds];
        Normalize(settings);
    }

    private static IEnumerable<StoredInstance> InstancesOf(AppSettingsDocument settings, string deviceId) =>
        settings.Instances
            .Where(i => string.Equals(i.DeviceId, deviceId, StringComparison.Ordinal))
            .OrderBy(i => i.Order);

    /// <summary>
    /// Réécrit les rangs des instances d'un appareil dans l'ordre donné, en
    /// réutilisant les rangs qu'elles occupaient déjà. La renumérotation
    /// générale qui suit remet tout au propre.
    /// </summary>
    private static void Reseat(
        AppSettingsDocument settings,
        string deviceId,
        List<StoredInstance> ordered)
    {
        var seats = InstancesOf(settings, deviceId).Select(i => i.Order).ToList();

        for (var i = 0; i < ordered.Count && i < seats.Count; i++)
        {
            ordered[i].Order = seats[i];
        }
    }
}
