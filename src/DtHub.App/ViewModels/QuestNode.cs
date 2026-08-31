using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>Ce qu'une ligne de la liste déroulante propose.</summary>
public enum QuestNodeKind
{
    /// <summary>Une branche à déplier : une zone de quêtes.</summary>
    Branch,

    /// <summary>Une quête à ouvrir.</summary>
    Quest,

    /// <summary>Une branche annoncée mais pas encore faite.</summary>
    Pending,

    /// <summary>Un intertitre de rubrique, qui ne se clique pas.</summary>
    Header,

    /// <summary>
    /// Un succès, qui coiffe ses quêtes sans se cliquer.
    ///
    /// Distinct de l'intertitre ordinaire pour porter son étoile : les
    /// intertitres « Zones » et « Quêtes » d'une recherche n'en veulent pas.
    /// </summary>
    Success,
}

/// <summary>
/// Une ligne de la liste déroulante : une branche, une quête, un intertitre.
/// </summary>
/// <param name="Kind">Ce que le clic déclenchera.</param>
/// <param name="Label">Texte affiché.</param>
/// <param name="Detail">Complément à droite : un nombre de quêtes, un niveau.</param>
/// <param name="Id">Identifiant de la rubrique, quand c'en est une.</param>
/// <param name="Tip">
/// Ce que le survol montre : les prérequis d'une quête, une ligne chacun. Vide
/// quand le site n'en donne pas, et la ligne n'affiche alors aucune icône.
/// </param>
/// <param name="Quest">La quête, quand c'en est une.</param>
public sealed record QuestNode(
    QuestNodeKind Kind,
    string Label,
    string? Detail = null,
    int Id = 0,
    QuestSummary? Quest = null,
    string? Tip = null)
{
    /// <summary>Ni les branches non faites, ni les intertitres ne se cliquent.</summary>
    public bool IsEnabled =>
        Kind is not (QuestNodeKind.Pending or QuestNodeKind.Header or QuestNodeKind.Success);

    /// <summary>Vrai quand la ligne a quelque chose à dire au survol.</summary>
    public bool HasTip => !string.IsNullOrEmpty(Tip);
}
