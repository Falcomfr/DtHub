namespace DtHub.Core.Papycha;

/// <summary>
/// Carte « adresse de quête -> succès », relevée une fois sur le site et
/// livrée avec l'application.
///
/// Le succès d'une quête n'est lisible que dans le bloc d'intro de sa page :
/// le lire pour les sept cent quatre-vingt-deux coûte treize mégaoctets. Le
/// refaire chaque semaine sur chaque poste pèserait sur le site pour une
/// information qui ne bouge qu'aux mises à jour du jeu.
///
/// Elle complète les intertitres des pages de rubrique, que l'indexation lit
/// déjà. Mesuré : les intertitres seuls rattachent 380 quêtes, les blocs
/// d'intro 459, leur union 505 sur 782. Les 277 autres n'ont pas de succès, ce
/// que confirme la liste officielle du site, qui n'en annonce que 475 au total.
/// </summary>
public interface IQuestSuccessSeed
{
    /// <summary>
    /// Rend la carte, indexée sur l'adresse de la quête sans barre finale.
    /// Une carte vide est un cas normal : l'indexation retombe alors sur les
    /// seuls intertitres.
    /// </summary>
    IReadOnlyDictionary<string, string> Load();
}
