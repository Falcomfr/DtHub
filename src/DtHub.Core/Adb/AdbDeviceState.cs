namespace DtHub.Core.Adb;

/// <summary>
/// État rapporté par <c>adb devices</c>. La valeur brute est conservée à côté
/// pour ne rien perdre lorsqu'ADB introduit un état que nous ne connaissons
/// pas encore.
/// </summary>
public enum AdbDeviceState
{
    /// <summary>État non reconnu. La chaîne d'origine reste disponible.</summary>
    Unknown = 0,

    /// <summary>Prêt à recevoir des commandes.</summary>
    Device,

    /// <summary>Connu mais injoignable : câble débranché, Wi-Fi coupé, veille.</summary>
    Offline,

    /// <summary>La clé RSA n'a pas encore été acceptée sur le téléphone.</summary>
    Unauthorized,

    /// <summary>Autorisation en cours de négociation.</summary>
    Authorizing,

    /// <summary>Connexion TCP en cours.</summary>
    Connecting,

    /// <summary>Le pilote USB refuse l'accès à l'appareil.</summary>
    NoPermissions,

    Bootloader,
    Recovery,
    Sideload,
    Rescue,
    Host,

    /// <summary>Le port existe mais aucun appareil ne répond.</summary>
    Detached,
}
