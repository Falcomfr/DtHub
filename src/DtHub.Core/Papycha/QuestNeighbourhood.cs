namespace DtHub.Core.Papycha;

/// <summary>Ce qui précède et ce qui suit une quête, et son rang dans son succès.</summary>
/// <param name="Previous">La quête d'avant, ou <c>null</c>.</param>
/// <param name="Next">La quête d'après, ou <c>null</c>.</param>
/// <param name="Rank">Rang dans le succès, à partir de 1. Zéro hors succès.</param>
/// <param name="Count">Nombre de quêtes du succès. Zéro hors succès.</param>
public readonly record struct QuestNeighbours(
    QuestSummary? Previous,
    QuestSummary? Next,
    int Rank,
    int Count)
{
    /// <summary>
    /// Vrai quand la liste du succès a désigné elle-même la suivante, parce que
    /// la quête ouverte n'est pas la dernière de la liste.
    ///
    /// Sert à savoir qui a le dernier mot. La colonne que le site publie en
    /// pied d'article s'intitule « Quêtes et jalons suivants » : elle dit ce que
    /// cette quête débloque, c'est-à-dire le graphe des prérequis, et non
    /// l'ordre dans lequel on lit un succès. Les deux se ressemblent souvent et
    /// diffèrent parfois : dans « Le théâtre des gobelins », la colonne de
    /// « Titi Gobelait le magobelin » ne nomme que « Manque de moule », qui
    /// l'exige, alors que la liste passe d'abord par « Un avenir de krotte de
    /// Trooll », qui n'exige rien. Suivre la colonne sautait une quête.
    /// </summary>
    public bool NextFromList => Rank > 0 && Rank < Count;

    /// <summary>
    /// Vrai quand la liste du succès a désigné elle-même la précédente, parce
    /// que la quête ouverte n'en est pas la première.
    /// </summary>
    public bool PreviousFromList => Rank > 1;
}

/// <summary>
/// Décide des voisines d'une quête.
///
/// Deux sources, dans cet ordre. La liste du succès d'abord : la précédente est
/// celle qu'on voit au-dessus, la suivante celle d'en dessous, et c'est ce
/// qu'on attend en parcourant une liste. Le graphe des prérequis ensuite, là où
/// la liste s'arrête : la première quête d'un succès n'a pas de précédente, la
/// dernière pas de suivante, et le site, lui, continue.
///
/// Le calcul est dans le noyau et non dans la vue : c'est la seule couche que
/// les épreuves atteignent, le projet d'épreuves visant net10.0 quand
/// l'application vise net10.0-windows. Il y vivait, et rien ne l'éprouvait.
/// </summary>
public static class QuestNeighbourhood
{
    /// <summary>Les voisines de cette quête, index des chaînes à l'appui s'il existe.</summary>
    public static QuestNeighbours Of(
        QuestSummary? quest,
        IEnumerable<QuestSummary>? quests,
        QuestChainIndex? chain)
    {
        if (quest is null)
        {
            return default;
        }

        QuestSummary? previous = null;
        QuestSummary? next = null;
        var rank = 0;
        var count = 0;

        if (quest.SuccessName.Length > 0 && quests is not null)
        {
            List<QuestSummary> group =
            [
                .. QuestPlayOrder.Sorted(quests.Where(q =>
                    string.Equals(q.SuccessName, quest.SuccessName, StringComparison.Ordinal))),
            ];

            var index = group.FindIndex(q =>
                string.Equals(q.Url, quest.Url, StringComparison.Ordinal));

            if (index >= 0)
            {
                rank = index + 1;
                count = group.Count;

                if (index > 0)
                {
                    previous = group[index - 1];
                }

                if (index < group.Count - 1)
                {
                    next = group[index + 1];
                }
            }
        }

        if (chain is not null)
        {
            previous ??= chain.PreviousOf(quest);
            next ??= chain.NextOf(quest);

            // Et si rien ne pend à cette quête, la série suivante, cherchée
            // dans tout le succès : elle ne part pas toujours de sa dernière.
            next ??= chain.NextSeriesOf(quest);
        }

        return new QuestNeighbours(previous, next, rank, count);
    }
}
