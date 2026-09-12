namespace DtHub.Core.Papycha;

/// <summary>Nature d'un lien de progression, telle que le site la distingue.</summary>
public enum QuestLinkKind
{
    /// <summary>Une autre quête.</summary>
    Quest,

    /// <summary>Un succès validé ou débloqué.</summary>
    Success,

    /// <summary>Autre chose : un jalon, une étape de parcours.</summary>
    Milestone,
}

/// <summary>Un lien vers une quête ou un succès, tel que la page le propose.</summary>
/// <param name="Title">Libellé affiché.</param>
/// <param name="Url">Adresse absolue.</param>
/// <param name="Kind">Ce vers quoi il mène.</param>
public sealed record QuestLink(string Title, string Url, QuestLinkKind Kind)
{
    /// <summary>
    /// Ce qu'on quitte en suivant ce lien : le succès d'arrivée, à défaut sa
    /// zone. Vide quand on reste dans la même suite, ce qui est le cas
    /// ordinaire, et le bouton n'annonce alors rien de plus que le titre.
    /// </summary>
    public string? Series { get; init; }

}

/// <summary>
/// Ce qui précède et ce qui suit une quête, lu dans le bloc de progression que
/// le site place en pied d'article.
///
/// Volontairement des listes : une quête peut ouvrir plusieurs suites, chacune
/// rangée sous son objectif.
/// </summary>
public sealed record QuestChain
{
    public IReadOnlyList<QuestLink> Previous { get; init; } = [];

    public IReadOnlyList<QuestLink> Next { get; init; } = [];

    /// <summary>Quête précédente immédiate, s'il y en a une.</summary>
    public QuestLink? PreviousQuest =>
        Previous.FirstOrDefault(l => l.Kind == QuestLinkKind.Quest);

    /// <summary>Quête suivante immédiate, s'il y en a une.</summary>
    public QuestLink? NextQuest =>
        Next.FirstOrDefault(l => l.Kind == QuestLinkKind.Quest);

    /// <summary>
    /// La quête précédente que le site nomme, s'il n'en nomme qu'une.
    ///
    /// La colonne peut en porter plusieurs : en désigner une mentirait sur ce
    /// que le site publie, et c'est la règle qui vaut déjà pour le graphe des
    /// prérequis. Relevé sur les 782 guides : 448 colonnes nomment une seule
    /// quête, 39 en nomment plusieurs.
    /// </summary>
    public QuestLink? OnlyPreviousQuest => Only(Previous);

    /// <summary>
    /// La quête suivante que le site nomme, s'il n'en nomme qu'une. Relevé :
    /// 325 colonnes en nomment une seule, 79 plusieurs, et 213 ne nomment que
    /// le succès qui vient d'être validé.
    /// </summary>
    public QuestLink? OnlyNextQuest => Only(Next);

    private static QuestLink? Only(IReadOnlyList<QuestLink> links)
    {
        QuestLink? seul = null;

        foreach (var link in links)
        {
            if (link.Kind != QuestLinkKind.Quest)
            {
                continue;
            }

            if (seul is not null)
            {
                return null;
            }

            seul = link;
        }

        return seul;
    }
}
