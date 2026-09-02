namespace DtHub.Core.Papycha;

/// <summary>
/// L'ordre dans lequel on joue les quêtes d'un succès.
///
/// La place dans le succès d'abord, calculée à l'indexation à partir des
/// prérequis du site ; le rang de chaîne ensuite, pour les quêtes que la carte
/// ne connaît pas ; le titre en dernier, pour que l'ordre soit total et
/// toujours le même.
///
/// Un zéro ne dit pas « premier » mais « on ne sait pas », et passe donc en
/// queue. La chaîne de quêtes le triait pourtant à l'endroit, si bien qu'une
/// quête de rang inconnu passait pour la première de son succès et se donnait
/// pour la suite de la série précédente. La liste et la chaîne lisent
/// maintenant la même règle, ce que le commentaire de la liste affirmait déjà
/// sans que ce fût vrai.
/// </summary>
public static class QuestPlayOrder
{
    /// <summary>Les quêtes rangées dans l'ordre où l'on y joue.</summary>
    public static IReadOnlyList<QuestSummary> Sorted(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        return
        [
            .. quests
                .OrderBy(q => q.PlayOrder == 0 ? int.MaxValue : q.PlayOrder)
                .ThenBy(q => q.ChainStep == 0 ? int.MaxValue : q.ChainStep)
                .ThenBy(q => q.Title, StringComparer.CurrentCulture),
        ];
    }
}
