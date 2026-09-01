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
    /// Le titre d'un groupe de résultats : zones, succès, quêtes.
    ///
    /// Distinct de l'intertitre ordinaire parce qu'il le domine : dans une
    /// recherche, ces trois-là coiffent des succès qui sont eux-mêmes des
    /// intertitres, et les deux niveaux se confondaient.
    /// </summary>
    Section,

    /// <summary>
    /// Un succès, qui coiffe ses quêtes sans se cliquer.
    ///
    /// Distinct de l'intertitre ordinaire pour porter son étoile : les
    /// intertitres « Zones » et « Quêtes » d'une recherche n'en veulent pas.
    /// </summary>
    Success,
}

/// <summary>
/// L'icône d'une ligne, qui dit sa nature avant qu'on l'ait lue.
///
/// Séparée de <see cref="QuestNodeKind"/>, qui dit ce que le clic fera : deux
/// branches se déplient de la même façon sans désigner la même chose, une zone
/// du monde et une famille de quêtes.
/// </summary>
public enum QuestNodeGlyph
{
    /// <summary>Aucune : les quêtes, les lignes les plus nombreuses.</summary>
    None,

    /// <summary>La racine des quêtes.</summary>
    Quests,

    /// <summary>La racine des donjons.</summary>
    Dungeons,

    /// <summary>Un endroit du monde.</summary>
    Place,

    /// <summary>Une famille de quêtes, qui ne se situe nulle part.</summary>
    Family,

    /// <summary>Un succès.</summary>
    Success,
}

/// <summary>
/// Une ligne de la liste déroulante : une branche, une quête, un intertitre.
/// </summary>
/// <param name="Kind">Ce que le clic déclenchera.</param>
/// <param name="Label">Texte affiché.</param>
/// <param name="Detail">Complément à droite : un nombre de quêtes, un niveau.</param>
/// <param name="Id">Identifiant de la rubrique, quand c'en est une.</param>
/// <param name="Glyph">L'icône qui annonce la nature de la ligne.</param>
/// <param name="Spaced">
/// Vrai quand un blanc doit suivre la ligne, parce que ce qui vient après
/// change de nature.
/// </param>
/// <param name="Needs">
/// Ce que le survol montre : les prérequis d'une quête, un par ligne. Vide
/// quand le site n'en donne pas, et la ligne n'affiche alors aucune icône.
/// </param>
/// <param name="Quest">La quête, quand c'en est une.</param>
public sealed record QuestNode(
    QuestNodeKind Kind,
    string Label,
    string? Detail = null,
    int Id = 0,
    QuestSummary? Quest = null,
    QuestNodeGlyph Glyph = QuestNodeGlyph.None,
    IReadOnlyList<QuestNeed>? Needs = null,
    bool Spaced = false)
{
    /// <summary>Ni les branches non faites, ni les intertitres ne se cliquent.</summary>
    public bool IsEnabled => Kind is QuestNodeKind.Branch or QuestNodeKind.Quest;

    /// <summary>Vrai quand la ligne a quelque chose à dire au survol.</summary>
    public bool HasNeeds => Needs is { Count: > 0 };
}

/// <summary>
/// Un prérequis d'une quête, et la quête qu'il nomme quand c'en est une.
///
/// Le site en écrit de toutes sortes : des quêtes, mais aussi des objets à
/// apporter, un alignement, un nombre de joueurs, un niveau, des créneaux
/// horaires. Seuls les premiers se cliquent.
/// </summary>
/// <param name="Text">Le prérequis tel que le site l'écrit.</param>
/// <param name="Quest">La quête qu'il désigne, ou <c>null</c>.</param>
public sealed record QuestNeed(string Text, QuestSummary? Quest)
{
    /// <summary>Vrai quand le prérequis mène quelque part.</summary>
    public bool IsQuest => Quest is not null;
}
