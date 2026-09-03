namespace DtHub.Core.Papycha;

/// <summary>
/// Un lieu de combat du site - un donjon, un raid ou une tanière - tel qu'on en
/// a besoin pour le choisir.
///
/// Le site en publie quatre-vingt-trois, dans un format bien plus régulier que
/// ses quêtes : le niveau, la position et le personnage viennent de ses
/// métadonnées, la clef et la pierre d'âme d'un bloc dont les classes sont du
/// code et non des libellés.
///
/// Rien de ce qui vient des serveurs d'Ankama - les vignettes de boss et de
/// clefs - n'est repris ici : la liste reste du texte.
/// </summary>
public sealed record DungeonSummary
{
    public int Id { get; init; }

    /// <summary>Donjon, raid ou tanière, selon la catégorie du site.</summary>
    public DungeonKind Kind { get; init; }

    /// <summary>Nom du donjon, sans le préfixe « [Donjon] » du site.</summary>
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    /// <summary>Titre normalisé, pour la recherche.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>
    /// Niveau conseillé. Zéro quand le site ne le renseigne pas, ce qui arrive
    /// sur trois donjons : ceux-là se rangent à part plutôt qu'au niveau zéro.
    /// </summary>
    public int Level { get; init; }

    /// <summary>Coordonnées de l'entrée, « [9,-57] ».</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>Personnage à qui parler pour entrer.</summary>
    public string Person { get; init; } = string.Empty;

    /// <summary>
    /// Clef exigée à l'entrée. Vide sur neuf donjons, qui n'en demandent pas :
    /// l'absence est une information, pas un trou.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Taille de la pierre d'âme, telle que le site l'écrit : « petite »,
    /// « moyenne », « grande » ou « gigantesque ».
    /// </summary>
    public string SoulStone { get; init; } = string.Empty;

    /// <summary>Vrai si une clef est exigée.</summary>
    public bool NeedsKey => Key.Length > 0;
}
