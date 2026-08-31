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
    IReadOnlyDictionary<string, QuestSeedEntry> Load();
}

/// <summary>Ce que la carte retient d'une quête.</summary>
/// <param name="Success">Nom du succès dont elle fait partie.</param>
/// <param name="ChainStep">
/// Sa place dans sa chaîne de prérequis, zéro si le site ne la donne pas. Sert
/// à présenter les quêtes d'un succès dans l'ordre où l'on y joue.
/// </param>
/// <param name="PlayOrder">
/// Sa place dans son succès, calculée à l'extraction à partir des prérequis que
/// le site publie. Le site ne donne cet ordre nulle part ailleurs : sans lui,
/// « Les rescapés de Frigost » précédait « L'essentiel est dans le Lac gelé »
/// qu'elle exige pourtant.
/// </param>
public readonly record struct QuestSeedEntry(string Success, int ChainStep, int PlayOrder);
