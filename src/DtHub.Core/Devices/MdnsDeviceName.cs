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
    /// Numéro de série matériel porté par un nom mDNS, ou <c>null</c> si le
    /// texte donné n'en est pas un.
    /// </summary>
    public static string? HardwareSerialFrom(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial)
            || !serial.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var service = serial.IndexOf(ServiceMarker, StringComparison.Ordinal);

        if (service <= Prefix.Length)
        {
            return null;
        }

        var body = serial[Prefix.Length..service];

        // Le jeton final est séparé par un tiret. Le numéro de série peut lui
        // aussi en contenir : c'est le dernier qui sépare, pas le premier.
        var token = body.LastIndexOf('-');

        var extracted = token > 0 ? body[..token] : body;

        return extracted.Length > 0 ? extracted : null;
    }

    /// <summary>Vrai si le texte donné est un nom mDNS de débogage sans fil.</summary>
    public static bool IsMdnsName(string? serial) => HardwareSerialFrom(serial) is not null;
}
