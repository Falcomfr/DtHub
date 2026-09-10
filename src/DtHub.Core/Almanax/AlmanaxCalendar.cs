using System.Globalization;

namespace DtHub.Core.Almanax;

/// <summary>
/// L'Almanax de DOFUS Touch, chez Ankama.
///
/// **Touch n'a pas le même Almanax que DOFUS**, et c'est le seul vrai piège de
/// cette fonction : une offrande fausse coûte une journée de quête à qui la
/// suit. Mesuré sur l'officiel le 10 septembre 2026, la même page selon le
/// filtre : « 1 Aile de dragodinde » pour DOFUS, « 1 Dent de Dragodinde » pour
/// Touch. Le 11, « 2 Corne de Dragoeuf Guerrier » côté Touch.
///
/// **Aucune API ne sert Touch.** Sondé, « api.dofusdu.de » ne répond que pour
/// « dofus3 » ; « dofustouch », « touch » et « retro » sont des routes
/// inconnues. La seule application dédiée, Almafus, a quitté le Play Store en
/// 2024. Les bibliothèques du milieu grattent toutes le même portail, sans son
/// filtre, et rendent donc l'Almanax de DOFUS.
///
/// **Lire le calendrier depuis le téléphone a été essayé, et ne marche pas.**
/// Le client Touch est une enveloppe Cordova de quinze mégaoctets qui télécharge
/// ses actifs et les range dans son stockage interne. Sondé sur un appareil
/// réel : le stockage externe de l'application est vide, il n'y a pas d'OBB,
/// rien d'Ankama ailleurs sur la carte, « /data/data » refuse, et « run-as »
/// répond que le paquet n'est pas déboguable. C'est vrai de tout téléphone non
/// rooté, donc de celui de tout le monde.
///
/// Reste la page d'Ankama, qu'on affiche telle quelle. Rien n'est copié, rien
/// n'est hébergé, rien ne peut donc se périmer en silence : c'est la source qui
/// fait foi qui s'affiche, et le filtre est dans l'adresse.
/// </summary>
public static class AlmanaxCalendar
{
    /// <summary>Nom d'hôte du portail qui publie l'Almanax.</summary>
    public const string Host = "krosmoz.com";

    /// <summary>
    /// Le filtre qui bascule la page sur DOFUS Touch.
    ///
    /// Le portail retient le choix en session, mais l'adresse le porte aussi,
    /// et c'est cette forme qu'on emploie : une fenêtre qui dépendrait d'un
    /// témoin déjà posé montrerait l'Almanax de DOFUS à la première ouverture,
    /// c'est-à-dire l'offrande d'un autre jeu, sans que rien ne le dise.
    /// </summary>
    public const string TouchFilter = "?game=dofustouch";

    /// <summary>
    /// L'adresse de l'Almanax du jour, dans la langue demandée.
    ///
    /// Le portail publie en huit langues et l'application en parle trois : la
    /// correspondance est directe, et tout ce qu'on ne connaît pas retombe sur
    /// la langue neutre plutôt que d'inventer un chemin qui n'existe pas.
    /// </summary>
    public static string UrlFor(string? language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();

        var path = code switch
        {
            "fr" => "fr",
            "es" => "es",
            _ => "en",
        };

        return "https://www." + Host + "/" + path + "/almanax" + TouchFilter;
    }

    /// <summary>
    /// L'adresse d'un jour précis.
    ///
    /// Le portail accepte la date dans le chemin, au format ISO. On la pose
    /// toujours, même pour aujourd'hui : l'adresse nue rend bien le jour
    /// courant, mais elle le rend selon l'horloge du serveur, et la fenêtre
    /// annonce une date lue sur celle du poste. Les deux se croisent autour de
    /// minuit, et la date affichée ne serait alors plus celle des données.
    /// </summary>
    public static string UrlFor(string? language, DateOnly date)
    {
        var root = UrlFor(language);
        var filter = root.IndexOf('?', StringComparison.Ordinal);

        return string.Concat(
            root.AsSpan(0, filter),
            "/",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.AsSpan(filter));
    }

    /// <summary>
    /// Vrai quand l'adresse est une page de l'Almanax du portail.
    ///
    /// La fenêtre n'a pas de barre d'adresse : on y voit une page sans savoir
    /// d'où elle vient, sous notre titre et notre icône. Elle ne reçoit donc
    /// que l'Almanax, et le reste part au navigateur. C'est la même réserve que
    /// pour les pages de guides, et pour la même raison.
    ///
    /// La comparaison porte sur l'hôte que rend l'analyseur d'adresses et non
    /// sur le début du texte : « https://krosmoz.com@ailleurs.example/ »
    /// commence bien par le nom du portail sans lui appartenir.
    /// </summary>
    public static bool Owns(string? url)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        var known = string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase);

        if (!known)
        {
            return false;
        }

        // « /fr/almanax », « /fr/almanax/2026-09-10 », « /fr/almanax/aide » : la
        // langue d'abord, la rubrique ensuite. Le reste du portail, ses forums
        // et sa boutique, n'a rien à faire dans une fenêtre qui dit « Almanax ».
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 && string.Equals(parts[1], "almanax", StringComparison.OrdinalIgnoreCase);
    }
}
