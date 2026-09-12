namespace DtHub.Core.Adb;

/// <summary>
/// Lit ce qu'ADB dit d'une connexion sans fil qui a échoué.
///
/// **Trois échecs très différents s'écrivent presque pareil**, et la
/// différence décide de ce qu'on conseille à l'utilisateur. Relevés au
/// caractère près sur un poste français, contre un vrai téléphone :
///
/// <code>
/// port fermé        cannot connect to 192.168.1.16:45573: … (10061)
/// machine absente   cannot connect to 192.168.1.99:40000: … (10060)
/// clé refusée       failed to connect to 192.168.1.16:37697
/// </code>
///
/// Les deux premiers portent un code d'erreur réseau : la connexion TCP n'a
/// même pas abouti, le téléphone est éteint, hors du réseau, ou son débogage
/// sans fil a changé de port. Le troisième n'en porte aucun, parce qu'il n'y a
/// pas eu de faute réseau : **le téléphone a accepté la connexion puis refusé
/// la poignée de main.** Il est là, il écoute, et il ne reconnaît plus la clé
/// de ce PC.
///
/// C'est la seule cause qu'une nouvelle association répare, et la seule qu'on
/// ne devine pas : rien n'a été désassocié à la main, et rallumer le débogage
/// sans fil n'y change rien.
/// </summary>
public static class AdbConnectFailure
{
    /// <summary>Ce qu'ADB écrit quand la connexion réseau elle-même a échoué.</summary>
    private const string NetworkFault = "cannot connect to";

    /// <summary>Ce qu'il écrit quand la connexion a abouti et la suite non.</summary>
    private const string Handshake = "failed to connect to";

    /// <summary>
    /// Vrai quand l'échec dit que l'appareil a refusé ce PC, et non que le
    /// réseau a manqué.
    ///
    /// Faux sur tout le reste, y compris sur une réponse qu'on ne comprend
    /// pas : conseiller une association là où le téléphone est simplement
    /// éteint enverrait l'utilisateur à la mauvaise page.
    /// </summary>
    public static bool MeansRefusedKey(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return false;
        }

        var said = failureReason.Trim();

        return said.StartsWith(Handshake, StringComparison.OrdinalIgnoreCase)
            && !said.Contains(NetworkFault, StringComparison.OrdinalIgnoreCase);
    }
}
