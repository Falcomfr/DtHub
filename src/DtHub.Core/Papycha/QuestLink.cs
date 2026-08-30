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
public sealed record QuestLink(string Title, string Url, QuestLinkKind Kind);

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
}
