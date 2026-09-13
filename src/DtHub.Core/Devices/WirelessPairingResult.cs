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

    /// <summary>
    /// The pairing address does not answer, so the code was not spent. The
    /// mDNS announcement pointed at the wrong device; the user can type the
    /// address shown on the phone.
    /// </summary>
    AddressUnreachable,
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
    public bool Paired => Status
        is not WirelessPairingStatus.PairingFailed
        and not WirelessPairingStatus.AddressUnreachable;

    public bool Connected => Status == WirelessPairingStatus.Connected;

    /// <summary>
    /// Vrai quand il ne manque plus que le port. Le réseau n'a rien annoncé,
    /// mais le téléphone affiche ce port sous « Débogage sans fil », et
    /// l'appairage est acquis : il n'y a rien à refaire, rien qu'à le lire.
    /// </summary>
    public bool NeedsPort => Status == WirelessPairingStatus.ConnectPortNotFound;

    /// <summary>
    /// True when the address itself is what must be supplied. Nothing is
    /// gained in that case, but nothing is lost either: the code was not used,
    /// and the phone's screen shows the address, which does not lie.
    /// </summary>
    public bool NeedsAddress => Status == WirelessPairingStatus.AddressUnreachable;
}
