namespace DtHub.Core.Settings;

/// <summary>
/// Tient l'ordre des instances dans les réglages.
///
/// L'ordre est global et libre : une instance peut se placer entre deux
/// instances d'un autre appareil. Il tient tout entier dans
/// <see cref="StoredInstance.Order"/>, rang dense de 0 à n-1, si bien qu'un
/// simple tri sur ce rang rend l'ordre voulu.
///
/// Les déplacements se disent par clés et non par décalage : la liste affichée
/// ne montre que les appareils joignables, alors que les réglages portent
/// toutes les instances. Un décalage compté sur les positions visibles
/// désignerait la mauvaise destination dès qu'une instance cachée s'intercale.
///
/// Toutes les fonctions sont pures : elles ne touchent qu'au document reçu.
/// </summary>
public static class InstanceOrdering
{
    /// <summary>
    /// Resserre les rangs en 0 à n-1, dans l'ordre courant.
    ///
    /// Les rangs devenaient creux et pouvaient entrer en collision : ils
    /// étaient attribués une fois pour toutes à la découverte, sans jamais
    /// être renumérotés après l'oubli d'un appareil. Deux instances de même
    /// rang laissaient l'ordre dépendre de l'ordre d'insertion.
    /// </summary>
    public static void Normalize(AppSettingsDocument settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Reseat([.. settings.Instances.OrderBy(i => i.Order)]);
    }

    /// <summary>
    /// Place une instance juste avant ou juste après une autre, quel que soit
    /// leur appareil.
    /// </summary>
    /// <returns>Faux si une clé est inconnue ou si rien ne bouge.</returns>
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

        // Retirer l'instance décale d'un rang tout ce qui la suivait.
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
    /// Ajoute une instance découverte : à la suite de celles de son appareil
    /// s'il en a déjà, sinon en fin de liste.
    ///
    /// Une instance neuve doit apparaître près de ses sœurs plutôt qu'au bout
    /// d'une longue liste, où on ne la verrait pas.
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

    /// <summary>
    /// Fixe l'ordre complet par les clés. Une clé oubliée par l'appelant garde
    /// son instance, qui reprend sa place à la suite.
    /// </summary>
    public static void ReorderInstances(AppSettingsDocument settings, IReadOnlyList<string> orderedKeys)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(orderedKeys);

        var remaining = settings.Instances.OrderBy(i => i.Order).ToList();

        var reordered = orderedKeys
            .Select(k => remaining.Find(i => string.Equals(i.Key, k, StringComparison.Ordinal)))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        reordered.AddRange(remaining.Where(i => !reordered.Contains(i)));

        Reseat(reordered);
    }

    private static void Reseat(List<StoredInstance> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
    }
}
