using DtHub.Core.Papycha;

namespace DtHub.App.ViewModels;

/// <summary>Ce qu'une ligne de la liste déroulante propose.</summary>
public enum QuestNodeKind
{
    /// <summary>Remonter d'un cran.</summary>
    Back,

    /// <summary>Une branche à déplier.</summary>
    Branch,

    /// <summary>Une quête à ouvrir.</summary>
    Quest,

    /// <summary>Une branche annoncée mais pas encore faite.</summary>
    Pending,
}

/// <summary>
/// Une ligne de la liste déroulante : une branche, une quête, ou le retour.
/// </summary>
/// <param name="Kind">Ce que le clic déclenchera.</param>
/// <param name="Label">Texte affiché.</param>
/// <param name="Detail">Complément à droite : un nombre de quêtes, un niveau.</param>
/// <param name="Id">Identifiant de la rubrique, quand c'en est une.</param>
/// <param name="Quest">La quête, quand c'en est une.</param>
public sealed record QuestNode(
    QuestNodeKind Kind,
    string Label,
    string? Detail = null,
    int Id = 0,
    QuestSummary? Quest = null)
{
    /// <summary>Les branches non faites ne se cliquent pas.</summary>
    public bool IsEnabled => Kind != QuestNodeKind.Pending;
}
