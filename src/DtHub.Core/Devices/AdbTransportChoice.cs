using DtHub.Core.Adb;

namespace DtHub.Core.Devices;

/// <summary>
/// Choisit, parmi les transports qu'ADB annonce, ceux qui valent la peine
/// d'être interrogés.
///
/// Un même téléphone est attaché deux fois dès qu'ADB le rejoint tout seul par
/// mDNS alors qu'il est déjà connecté par son adresse : une ligne
/// « 192.168.1.14:40187 » et une ligne
/// « adb-XXXXXXXX-XXXXXX._adb-tls-connect._tcp », toutes deux « device », la
/// même adresse derrière. On le croyait impossible : le commentaire de
/// <see cref="MdnsDeviceName"/> posait que l'appareil paraît sous son adresse
/// quand il est joignable et sous ce nom quand il ne l'est pas. Il paraît sous
/// les deux.
///
/// Ce n'est pas anodin. Le nom mDNS est un mauvais destinataire de commandes :
/// les journaux comptent vingt refus en quatre jours, « device 'adb-...' not
/// found » sur getprop, sur pm list users, sur la résolution d'activité. Et
/// toute commande sans destinataire échoue en « more than one device ».
///
/// Le nom garde son emploi : il identifie l'appareil et sert à s'y connecter.
/// Il n'est écarté que lorsqu'une adresse joignable désigne le même téléphone
/// dans le même relevé.
/// </summary>
public static class AdbTransportChoice
{
    /// <summary>
    /// Les transports à interroger, dans l'ordre où ADB les a rendus.
    ///
    /// Un appareil qu'on ne sait pas reconnaître garde ses deux lignes : mieux
    /// vaut une ligne en trop qu'un téléphone qui disparaît de la liste parce
    /// qu'on l'a pris pour un autre.
    /// </summary>
    public static IReadOnlyList<AdbDeviceEntry> WithoutDoubles(
        IReadOnlyList<AdbDeviceEntry> entries,
        IEnumerable<AndroidDevice> known)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(known);

        var candidates = known as IReadOnlyCollection<AndroidDevice> ?? [.. known];

        HashSet<string> routable = new(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (MdnsDeviceName.IsMdnsName(entry.Serial))
            {
                continue;
            }

            if (Identity(entry, candidates) is { Length: > 0 } identity)
            {
                routable.Add(identity);
            }
        }

        if (routable.Count == 0)
        {
            return entries;
        }

        return
        [
            .. entries.Where(e =>
                !MdnsDeviceName.IsMdnsName(e.Serial)
                || Identity(e, candidates) is not { Length: > 0 } identity
                || !routable.Contains(identity)),
        ];
    }

    /// <summary>
    /// De quel téléphone ce transport est celui-ci, pour autant qu'on puisse le
    /// dire sans l'interroger : le numéro de série que porte un nom mDNS, ou
    /// l'appareil mémorisé que cette ligne désigne.
    /// </summary>
    private static string? Identity(AdbDeviceEntry entry, IReadOnlyCollection<AndroidDevice> known) =>
        MdnsDeviceName.HardwareSerialFrom(entry.Serial)
        ?? DeviceFactory.Match(known, entry)?.Id;
}
