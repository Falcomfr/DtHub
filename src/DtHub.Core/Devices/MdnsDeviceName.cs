namespace DtHub.Core.Devices;

/// <summary>
/// Nom mDNS annoncé par le débogage sans fil d'Android, de la forme
/// <c>adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp</c>.
///
/// Il porte le numéro de série matériel de l'appareil, suivi d'un jeton tiré au
/// hasard. Le lire évite de créer une seconde identité pour un téléphone déjà
/// connu : un même appareil apparaît sous son adresse IP quand il est joignable,
/// et sous ce nom quand il ne l'est pas, ADB ne pouvant alors pas être interrogé
/// pour obtenir son numéro de série.
/// </summary>
public static class MdnsDeviceName
{
    private const string Prefix = "adb-";
    private const string ServiceMarker = "._adb";

    /// <summary>
    /// Numéro de série matériel porté par un nom mDNS complet, ou <c>null</c>
    /// si le texte donné n'en est pas un. Le suffixe de service est exigé :
    /// c'est lui qui distingue un nom d'annonce d'un numéro de série ordinaire,
    /// et cette méthode sert justement à faire le tri dans ce que rapporte
    /// <c>adb devices</c>.
    /// </summary>
    public static string? HardwareSerialFrom(string? serial)
    {
        if (!LooksAnnounced(serial))
        {
            return null;
        }

        var service = serial!.IndexOf(ServiceMarker, StringComparison.Ordinal);

        return service > Prefix.Length ? SerialIn(serial[Prefix.Length..service]) : null;
    }

    /// <summary>
    /// Numéro de série matériel porté par le nom d'instance d'une annonce,
    /// c'est-à-dire la première colonne de <c>adb mdns services</c>.
    ///
    /// Le type de service y vit dans une colonne à part : le nom n'en porte
    /// donc pas le suffixe, contrairement au numéro de série que rapporte
    /// <c>adb devices</c> pour un appareil injoignable. Le tri, lui, est déjà
    /// fait par ADB : ce qui figure dans cette colonne est une annonce.
    /// </summary>
    public static string? HardwareSerialFromInstance(string? instance)
    {
        if (!LooksAnnounced(instance))
        {
            return null;
        }

        var service = instance!.IndexOf(ServiceMarker, StringComparison.Ordinal);

        return SerialIn(service > 0 ? instance[Prefix.Length..service] : instance[Prefix.Length..]);
    }

    private static bool LooksAnnounced(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.StartsWith(Prefix, StringComparison.Ordinal);

    private static string? SerialIn(string body)
    {
        // Le jeton final est séparé par un tiret. Le numéro de série peut lui
        // aussi en contenir : c'est le dernier qui sépare, pas le premier.
        var token = body.LastIndexOf('-');

        var extracted = token > 0 ? body[..token] : body;

        return extracted.Length > 0 ? extracted : null;
    }

    /// <summary>Vrai si le texte donné est un nom mDNS de débogage sans fil.</summary>
    public static bool IsMdnsName(string? serial) => HardwareSerialFrom(serial) is not null;
}
