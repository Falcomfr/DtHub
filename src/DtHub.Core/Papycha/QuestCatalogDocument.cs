namespace DtHub.Core.Papycha;

/// <summary>
/// Le catalogue tel qu'il est rangé sur le disque.
///
/// C'est un cache, pas un réglage : le perdre ne coûte qu'une nouvelle
/// indexation. Il porte tout de même une version de schéma, pour qu'un
/// changement de forme se solde par une reconstruction et non par une lecture
/// de travers.
/// </summary>
public sealed class QuestCatalogDocument
{
    /// <summary>
    /// Version 2 : chaque quête porte le nom de ses rubriques, pour que
    /// chercher « frigost » rende les quêtes de Frigost et pas seulement
    /// celles dont le titre porte le mot.
    ///
    /// Version 3 : la rubrique principale de chaque quête, et l'ordre dans
    /// lequel le site range ses rubriques.
    ///
    /// Version 4 : une quête est rangée sous une rubrique et une seule, et les
    /// rubriques que le site tient à la main viennent compléter ses catégories.
    ///
    /// Version 5 : le succès dont chaque quête fait partie.
    ///
    /// Version 6 : les quêtes d'un même succès sont réunies sous une seule
    /// rubrique.
    ///
    /// Version 7 : une quête appartient à toutes les rubriques qui la
    /// réclament, le site la rangeant lui-même à plusieurs endroits.
    ///
    /// Version 8 : l'ordre dans lequel le site présente ses succès.
    ///
    /// Version 9 : les succès viennent aussi de la carte embarquée, tirée du
    /// bloc d'intro de chaque quête.
    ///
    /// Version 10 : la place de chaque quête dans son succès.
    ///
    /// Version 11 : la position et le personnage de départ, et la clé de
    /// rubrique bâtie sur les rubriques affichées.
    ///
    /// Version 12 : les prérequis de chaque quête, et la clé de rubrique
    /// retirée, la recherche ne portant plus que sur les titres.
    ///
    /// Version 13 : l'adresse de la page rédigée de chaque rubrique, celle que
    /// le tableau de « Quêtes » désigne.
    ///
    /// Version 14 : les donjons, avec leur niveau, leur clef et leur pierre
    /// d'âme.
    ///
    /// Version 15 : les chemins, et le genre d'un lieu de combat, donjon, raid
    /// ou tanière.
    ///
    /// L'empreinte du site, ajoutée après la quinzième, n'a pas demandé de
    /// seizième : ses champs sont absents d'un catalogue plus ancien, ce qui
    /// vaut « on ne sait pas » et provoque une relecture, une seule fois. Une
    /// version n'est due que lorsque ce qui est déjà écrit changerait de sens.
    /// </summary>
    public const int CurrentSchemaVersion = 15;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Moment de la dernière indexation réussie.</summary>
    public DateTimeOffset? IndexedUtc { get; set; }

    /// <summary>
    /// Ce que le site annonçait au moment de cette lecture : date du dernier
    /// article modifié, et nombre total d'articles. Les comparer à ce qu'il
    /// annonce aujourd'hui dit s'il a bougé, pour le prix d'une demande.
    /// Absents d'un catalogue plus ancien, ce qui vaut « on ne sait pas » et
    /// provoque une relecture, une seule fois.
    /// </summary>
    public DateTimeOffset? SiteModifiedUtc { get; set; }

    /// <inheritdoc cref="SiteModifiedUtc" />
    public int SitePosts { get; set; }

    public List<QuestSummary> Quests { get; set; } = [];

    public List<QuestSection> Sections { get; set; } = [];

    /// <summary>
    /// Les donjons du site. Rangés à part des quêtes : ils n'ont ni succès ni
    /// prérequis, mais un niveau, une clef et une pierre d'âme.
    /// </summary>
    public List<DungeonSummary> Dungeons { get; set; } = [];

    /// <summary>
    /// Les chemins du site, chacun rangé du côté qu'il dessert. Ils n'ont ni
    /// niveau ni clef : un itinéraire ne se joue pas, il se suit.
    /// </summary>
    public List<PathSummary> Paths { get; set; } = [];

    /// <summary>
    /// Intitulés des rubriques dans l'ordre du site, réduits à une forme
    /// comparable. Vide si le menu n'a pas pu être lu : on retombe alors sur un
    /// classement par nombre de quêtes.
    /// </summary>
    public List<string> SectionOrder { get; set; } = [];

    /// <summary>
    /// Intitulés des succès dans l'ordre où les pages du site les présentent.
    ///
    /// Cet ordre est celui d'une progression, et il ne se retrouve nulle part
    /// ailleurs : les ranger par ordre alphabétique, comme on le faisait,
    /// mettait « Épilogue hivernal » avant « L'hiver arrive ».
    /// </summary>
    public List<string> SuccessOrder { get; set; } = [];

    /// <summary>
    /// Vrai si le catalogue est inutilisable en l'état et doit être reconstruit.
    /// Un catalogue vieilli reste employé : mieux vaut une liste d'hier que
    /// pas de liste du tout quand le réseau manque.
    /// </summary>
    public bool NeedsRebuild =>
        SchemaVersion != CurrentSchemaVersion || Quests.Count == 0;
}
