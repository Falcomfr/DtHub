namespace DtHub.Core.Devices;

/// <summary>Issue d'un appairage sans fil, du point de vue de l'utilisateur.</summary>
public enum WirelessPairingStatus
{
    /// <summary>Appairé et connecté : il n'y a plus rien à faire.</summary>
    Connected,

    /// <summary>Le téléphone a refusé le code, ou l'appairage n'a pas abouti.</summary>
    PairingFailed,

    /// <summary>
    /// Appairage réussi, mais le port de connexion n'a pas été découvert. Le
    /// réseau bloque probablement le mDNS ; l'utilisateur peut saisir le port
    /// affiché sur le téléphone.
    /// </summary>
    ConnectPortNotFound,

    /// <summary>Appairage réussi mais la connexion a échoué.</summary>
    ConnectFailed,
}

/// <summary>Résultat complet d'un appairage, prêt à être affiché.</summary>
public sealed record WirelessPairingResult(
    WirelessPairingStatus Status,
    string UserMessage,
    string? Address = null,
    string? DeviceGuid = null)
{
    /// <summary>
    /// Vrai quand le téléphone a accepté le code, quoi qu'il soit advenu de la
    /// connexion ensuite. Ne veut donc pas dire qu'il y a de quoi jouer : c'est
    /// <see cref="Connected"/> qui le dit.
    /// </summary>
    public bool Paired => Status != WirelessPairingStatus.PairingFailed;

    public bool Connected => Status == WirelessPairingStatus.Connected;

    /// <summary>
    /// Vrai quand il ne manque plus que le port. Le réseau n'a rien annoncé,
    /// mais le téléphone affiche ce port sous « Débogage sans fil », et
    /// l'appairage est acquis : il n'y a rien à refaire, rien qu'à le lire.
    /// </summary>
    public bool NeedsPort => Status == WirelessPairingStatus.ConnectPortNotFound;
}
