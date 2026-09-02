namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qui appartient au site des guides, et ce qui n'en est pas.
///
/// La question se posait déjà pour lire les pages de rubrique, qui citent aussi
/// le wiki et les réseaux sociaux. Elle se pose surtout pour décider ce que nos
/// fenêtres acceptent de charger : elles n'ont pas de barre d'adresse, elles
/// portent notre cadre, et le pont y est posé sur tout document. Une page qui
/// nous tendait un lien y faisait donc ouvrir n'importe quelle adresse, de
/// n'importe quel hôte, jusqu'à « file:// », sous nos couleurs et sans que rien
/// ne dise où l'on était.
///
/// La comparaison porte sur l'hôte que rend l'analyseur d'adresses, et non sur
/// le début du texte : « https://papycha.fr@ailleurs.example/ » commence bien
/// par le nom du site sans lui appartenir, et l'analyseur rend « ailleurs ».
/// </summary>
public static class PapychaSite
{
    /// <summary>Nom d'hôte du site.</summary>
    public const string Host = "papycha.fr";

    /// <summary>Racine du site.</summary>
    public const string Root = "https://" + Host + "/";

    /// <summary>
    /// Vrai quand l'adresse est une page du site, en clair une adresse sûre de
    /// l'hôte du site ou d'un de ses sous-domaines.
    ///
    /// Les sous-domaines sont admis parce que le site en emploie au moins un :
    /// « www » redirige vers le nom nu, et la redirection est annoncée comme
    /// une navigation avant d'être suivie.
    ///
    /// Le clair est refusé, comme il l'est pour le navigateur : les neuf cent
    /// dix-huit adresses du catalogue sont toutes en « https ».
    /// </summary>
    public static bool Owns(string? url) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && (string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase));
}
