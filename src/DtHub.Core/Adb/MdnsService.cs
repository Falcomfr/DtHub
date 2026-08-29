namespace DtHub.Core.Adb;

/// <summary>
/// Un service annoncé par le débogage sans fil sur le réseau local. Le nom
/// contient le numéro de série du téléphone, ce qui permet de rattacher une
/// annonce à un appareil déjà connu.
/// </summary>
public sealed record MdnsService(string Name, string ServiceType, string Host, int Port)
{
    /// <summary>Service d'appairage, annoncé le temps que le code est affiché.</summary>
    public const string PairingType = "_adb-tls-pairing._tcp";

    /// <summary>Service de connexion, annoncé en permanence quand le débogage sans fil est actif.</summary>
    public const string ConnectType = "_adb-tls-connect._tcp";

    public string Address => $"{Host}:{Port}";

    public bool IsPairing => ServiceType.StartsWith(PairingType, StringComparison.Ordinal);

    public bool IsConnect => ServiceType.StartsWith(ConnectType, StringComparison.Ordinal);

    /// <summary>
    /// Vrai si l'annonce paraît provenir de l'appareil dont le numéro de série
    /// est donné. Le nom mDNS a la forme <c>adb-&lt;série&gt;-&lt;aléa&gt;</c>.
    /// </summary>
    public bool MatchesSerial(string? serial) =>
        !string.IsNullOrWhiteSpace(serial)
        && Name.Contains(serial, StringComparison.OrdinalIgnoreCase);
}
