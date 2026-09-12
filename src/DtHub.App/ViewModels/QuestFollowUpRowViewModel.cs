using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>
/// Une suite proposée en fin de quête, prête à dessiner.
///
/// Le lien brut est conservé : c'est lui que le bouton porte en étiquette et
/// que la fenêtre suit, exactement comme pour les boutons de la barre du bas.
/// </summary>
public sealed class QuestFollowUpRowViewModel
{
    public QuestFollowUpRowViewModel(QuestLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        Link = link;
        Kind = KindLabel(link.Kind);
    }

    /// <summary>Le lien tel que le site le publie.</summary>
    public QuestLink Link { get; }

    public string Title => Link.Title;

    /// <summary>
    /// Ce qu'on écrit à droite du titre quand ce n'est pas une quête :
    /// « succès » ou « jalon ». Vide pour une quête, qui est le cas ordinaire
    /// et n'a pas besoin qu'on le lui dise.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Vrai quand la ligne mène quelque part qu'on veuille suivre.
    ///
    /// **Seule une quête en est une.** Le lien d'un succès pointe vers la page
    /// d'arbre du site, qui sort du guide pour montrer un tableau que la
    /// fenêtre présente déjà à sa façon : le clic ressemblait à une suite et
    /// emmenait ailleurs. La ligne reste, parce qu'elle dit ce que la quête
    /// valide, mais elle ne se clique plus.
    /// </summary>
    public bool IsFollowable => Link.Kind == QuestLinkKind.Quest;

    private static string KindLabel(QuestLinkKind kind) => kind switch
    {
        QuestLinkKind.Success => Strings.Get("QuestFollowUpSuccess"),
        QuestLinkKind.Milestone => Strings.Get("QuestFollowUpMilestone"),
        _ => string.Empty,
    };
}

/// <summary>
/// Des suites rangées sous un même objectif du site, par exemple « Dofus
/// Cawotte obtenu ».
///
/// Le groupe sans objectif existe et n'est pas une anomalie : le site pose hors
/// de tout groupe le succès que la quête vient de valider.
/// </summary>
public sealed class QuestFollowUpGroupViewModel
{
    public QuestFollowUpGroupViewModel(QuestFollowUpGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        Objective = group.Objective;
        Rows = [.. group.Links.Select(l => new QuestFollowUpRowViewModel(l))];
    }

    public string Objective { get; }

    /// <summary>L'objectif tel qu'il se lit, « Objectif : Dofus Cawotte obtenu ».</summary>
    public string ObjectiveText =>
        Objective.Length > 0 ? Strings.Format("QuestFollowUpObjective", Objective) : string.Empty;

    public bool HasObjective => Objective.Length > 0;

    public IReadOnlyList<QuestFollowUpRowViewModel> Rows { get; }
}
