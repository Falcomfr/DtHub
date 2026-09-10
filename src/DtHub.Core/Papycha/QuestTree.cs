using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// Bâtit les branches de la liste des guides à partir du catalogue.
///
/// Ces règles vivaient dans la vue-modèle de la fenêtre, mêlées à la pile de
/// navigation et à la recherche. Elles sont pourtant pures : elles ne touchent
/// ni à WPF, ni au réseau, ni au disque, et rendent des objets de données à
/// partir d'un catalogue et d'un décompte. Les laisser là revenait à les
/// laisser hors de portée des épreuves, dans un projet qu'aucune n'atteint.
///
/// La navigation reste où elle est : elle tient un état de fenêtre, et c'est
/// bien le métier d'une vue-modèle.
/// </summary>
public sealed class QuestTree
{
    /// <summary>Catégorie du site qui range toutes les quêtes.</summary>
    public const int RootSection = 7;

    /// <summary>
    /// Branches qui ne viennent pas des catégories du site. Leurs identifiants
    /// sont négatifs, hors de portée de celles-ci, qui sont positives.
    /// </summary>
    public const int DungeonSection = -100;
    public const int RaidSection = -101;
    public const int LairSection = -102;
    public const int QuestPathSection = -103;
    public const int DungeonPathSection = -104;

    /// <summary>
    /// Le chevron qui sépare les rangs d'un fil d'Ariane. Il vit ici parce que
    /// trois endroits l'écrivaient chacun de leur côté : la fenêtre, le repère
    /// d'un signalement, et le nom d'une rubrique à deux rangs. Ils doivent se
    /// lire pareil, le repère servant justement à retrouver la page dans la
    /// liste.
    /// </summary>
    public const string Separator = "  \u203a  ";

    /// <summary>
    /// Les trois sortes de lieux de combat, dans l'ordre où la racine les
    /// présente, avec leur titre et les mots du compte.
    /// </summary>
    public static readonly (DungeonKind Kind, string Title, string One, string Many)[] DungeonGroups =
    [
        (DungeonKind.Dungeon, "Dungeons", "WordDungeon", "WordDungeons"),
        (DungeonKind.Raid, "Raids", "WordRaid", "WordRaids"),
        (DungeonKind.Lair, "Lairs", "WordLair", "WordLairs"),
    ];

    private readonly QuestCatalogService _catalog;
    private readonly IReadOnlyDictionary<int, int> _sectionCounts;

    /// <summary>
    /// Ce que les prérequis relient, posé par l'appelant à chaque catalogue.
    /// Sert à rattacher un prérequis à la quête qu'il nomme.
    /// </summary>
    public QuestChainIndex? Chain { get; set; }

    /// <param name="catalog">Le catalogue lu sur le site.</param>
    /// <param name="sectionCounts">
    /// Le nombre de quêtes par rubrique. Le dictionnaire est celui de la
    /// vue-modèle, qui le recompte quand le catalogue change : le passer par
    /// référence évite de le recopier à chaque affichage.
    /// </param>
    public QuestTree(QuestCatalogService catalog, IReadOnlyDictionary<int, int> sectionCounts)
    {
        _catalog = catalog;
        _sectionCounts = sectionCounts;
    }

    /// <summary>
    /// Les quatre entrées de la racine : les quêtes, et les trois sortes de
    /// lieux de combat.
    /// </summary>
    public IReadOnlyList<QuestNode> Root() =>
    [
        new(QuestNodeKind.Branch, Strings.Get("Quests"),
            Nombre(_catalog.Catalog.Quests.Count),
            Id: RootSection, Glyph: QuestNodeGlyph.Quests),
        new(QuestNodeKind.Branch, Strings.Get("Dungeons"),
            Combien(Fighting(DungeonKind.Dungeon).Count, "WordDungeon", "WordDungeons"),
            Id: DungeonSection, Glyph: QuestNodeGlyph.Dungeons),
        new(QuestNodeKind.Branch, Strings.Get("Raids"),
            Combien(Fighting(DungeonKind.Raid).Count, "WordRaid", "WordRaids"),
            Id: RaidSection, Glyph: QuestNodeGlyph.Raids),
        new(QuestNodeKind.Branch, Strings.Get("Lairs"),
            Combien(Fighting(DungeonKind.Lair).Count, "WordLair", "WordLairs"),
            Id: LairSection, Glyph: QuestNodeGlyph.Lairs),
    ];

    /// <summary>La branche où un lieu de combat se trouve.</summary>
    public static int SectionOf(DungeonSummary place) => place.Kind switch
    {
        DungeonKind.Raid => RaidSection,
        DungeonKind.Lair => LairSection,
        _ => DungeonSection,
    };

    /// <summary>Les lieux de combat d'un genre, dans l'ordre du site.</summary>
    public IReadOnlyList<DungeonSummary> Fighting(DungeonKind kind) =>
        [.. _catalog.Catalog.Dungeons.Where(d => d.Kind == kind)];

    /// <summary>Les chemins d'un côté.</summary>
    public IReadOnlyList<PathSummary> Paths(PathSide side) =>
        [.. _catalog.Catalog.Paths.Where(p => p.Side == side)];

    /// <summary>
    /// La sous-branche des chemins, en tête de la branche qu'elle sert.
    ///
    /// Un dossier plutôt qu'un groupe à la suite : un chemin ne se compare ni à
    /// une zone ni à un donjon, et les mêler allongerait une liste qu'on
    /// parcourt déjà longuement.
    /// </summary>
    public QuestNode PathBranch(PathSide side, int section) => new(
        QuestNodeKind.Branch,
        Strings.Get("Paths"),
        Combien(Paths(side).Count, "WordPath", "WordPaths"),
        Id: section,
        Glyph: QuestNodeGlyph.Route);

    /// <summary>
    /// Les donjons, du plus abordable au plus exigeant, coupés par paliers de
    /// cinquante niveaux.
    ///
    /// Quatre-vingt-trois lignes ne se parcourent pas d'un œil : on y cherche
    /// ce qui est à sa portée, et les paliers évitent de compter. Ceux dont le
    /// site ne donne pas le niveau ferment la marche sous leur propre
    /// intertitre, plutôt que de passer pour du niveau zéro.
    /// </summary>
    public IEnumerable<QuestNode> DungeonNodes()
    {
        var ordered = Fighting(DungeonKind.Dungeon)
            .OrderBy(d => DungeonLevelBand.RankOf(d.Level))
            .ThenBy(d => d.Level)
            .ThenBy(d => d.Title, StringComparer.CurrentCulture);

        var band = int.MinValue;

        foreach (var dungeon in ordered)
        {
            var rank = DungeonLevelBand.RankOf(dungeon.Level);

            if (rank != band)
            {
                band = rank;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    DungeonLevelBand.NameOf(dungeon.Level));
            }

            yield return NodeOf(dungeon);
        }
    }

    /// <summary>
    /// Une ligne de donjon : son nom avec son niveau, et à droite ce qu'il faut
    /// savoir avant d'y aller.
    /// </summary>
    public static QuestNode NodeOf(DungeonSummary dungeon) => new(
        QuestNodeKind.Quest,
        dungeon.Level > 0 ? $"{dungeon.Title} (niv. {dungeon.Level})" : dungeon.Title,
        Detail(dungeon),
        Dungeon: dungeon);

    /// <summary>
    /// Ce que la colonne de droite dit d'un donjon : la pierre d'âme et la
    /// position, précédées d'une clef quand il en faut une. Le nom de la clef
    /// vient au survol : il est trop long pour la colonne.
    /// </summary>
    public static string Detail(DungeonSummary dungeon)
    {
        List<string> parts = [];

        if (dungeon.SoulStone.Length > 0)
        {
            // « gigantesque pierre d'âme » dit deux fois « pierre d'âme » dans
            // une colonne où toutes les lignes en portent une : la taille suffit.
            parts.Add(dungeon.SoulStone.Replace(" pierre d’âme", string.Empty, StringComparison.Ordinal));
        }

        if (dungeon.Position.Length > 0)
        {
            parts.Add(dungeon.Position);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Rubriques qui contiennent au moins une quête, dans l'ordre où le site
    /// les range.
    ///
    /// Le catalogue les rend déjà ordonnées ; les reclasser par nombre de
    /// quêtes, comme on le faisait, revenait à ignorer l'ordre du site après
    /// être allé le chercher.
    /// </summary>
    public IEnumerable<QuestNode> Branches()
    {
        var zones = _catalog.Catalog.Sections
            .Where(s => s.Id != RootSection && _sectionCounts.GetValueOrDefault(s.Id) > 0)
            .OrderBy(s => QuestZoneOrder.RankOf(s.Name))
            .ThenBy(s => QuestZoneOrder.DisplayName(s.Name), StringComparer.CurrentCulture);

        List<QuestSection> ordered = [.. zones];
        var separated = false;

        for (var i = 0; i < ordered.Count; i++)
        {
            var zone = ordered[i];

            // Ce qui ne relève pas de la progression vient après un intertitre,
            // pour que la liste ne mélange pas un lieu et un cheminement.
            if (!separated && QuestZoneOrder.IsExtra(zone.Name))
            {
                separated = true;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    QuestZoneOrder.ExtrasHeader,
                    Glyph: QuestNodeGlyph.Family);
            }

            // Un blanc là où l'on passe d'une famille de quêtes aux lieux :
            // « Quêtes principales » ouvre la liste sans être un endroit, et
            // sans cette respiration elle se lit comme la première zone du
            // monde. Le blanc appartient à la ligne qui précède la rupture, et
            // non à celle qui la suit, pour ne pas doubler celui de
            // l'intertitre plus bas.
            var next = i + 1 < ordered.Count ? ordered[i + 1] : null;

            yield return new QuestNode(
                QuestNodeKind.Branch,
                $"{QuestZoneOrder.DisplayName(zone.Name)} ({_sectionCounts[zone.Id]})",
                QuestLevelRange.Of(_catalog.InSection(zone.Id)),
                Id: zone.Id,
                Glyph: GlyphOf(zone.Name),
                Spaced: next is not null
                    && !QuestZoneOrder.IsPlace(zone.Name)
                    && QuestZoneOrder.IsPlace(next.Name));
        }
    }

    public static string Text(int value) =>
        value.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// Ajoute les quêtes d'une rubrique dans l'ordre où l'on y joue.
    ///
    /// C'est ainsi que le site les présente, et c'est ainsi qu'on les joue :
    /// une quête isolée dit rarement à quoi elle sert. Le rangement est celui
    /// de <see cref="QuestZonePlan"/>, qui suit les prérequis.
    /// </summary>
    public IEnumerable<QuestNode> BySuccess(IReadOnlyList<QuestSummary> quests)
    {
        var plan = QuestZonePlan.Of(quests, _catalog.Catalog.SuccessOrder);

        // Le compte annoncé est celui du succès entier, non celui du morceau :
        // une série coupée par une quête seule reste une seule série, et son
        // premier intertitre doit dire combien de quêtes elle porte en tout.
        var total = plan
            .Where(b => b.IsSuccess)
            .GroupBy(b => b.SuccessName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.Quests.Count), StringComparer.Ordinal);

        foreach (var block in plan)
        {
            if (block.IsSuccess)
            {
                yield return new QuestNode(
                    QuestNodeKind.Success,
                    block.IsContinuation
                        ? $"{block.SuccessName} ({Strings.Get("SeriesContinued")})"
                        : $"{block.SuccessName} ({Text(total[block.SuccessName])})",
                    QuestLevelRange.Of(block.Quests),
                    Glyph: QuestNodeGlyph.Success);
            }

            foreach (var quest in block.Quests)
            {
                // Le décalage dit l'appartenance : une quête au ras de la marge
                // n'est réclamée par aucun succès.
                yield return NodeOf(quest) with { InSuccess = block.IsSuccess };
            }
        }
    }


    /// <summary>
    /// Une ligne de quête.
    ///
    /// La colonne de droite ne porte plus le niveau : le site ne le renseigne
    /// que sur cent dix-sept quêtes sur sept cent quatre-vingt-deux, et une
    /// colonne vide neuf fois sur dix ne mérite pas sa place. Elle porte les
    /// prérequis, qui en couvrent six cent treize, et qui disent quelque chose
    /// d'utile avant de partir : ce qu'il faut avoir fait.
    /// </summary>
    public QuestNode NodeOf(QuestSummary quest) => new(
        QuestNodeKind.Quest,
        quest.Title,
        Quest: quest,
        Needs: NeedsOf(quest));

    /// <summary>
    /// Les prérequis d'une quête, chacun rattaché à la quête qu'il nomme quand
    /// c'en est une. Sur cinq cent soixante-sept prérequis distincts, beaucoup
    /// sont des objets, un alignement ou un créneau horaire : ceux-là restent
    /// du texte, et seuls les autres deviendront des liens.
    /// </summary>
    public IReadOnlyList<QuestNeed> NeedsOf(QuestSummary quest) =>
        quest.Prerequisites.Count == 0
            ? []
            : [.. quest.Prerequisites.Select(need => new QuestNeed(need, Chain?.Find(need)))];


    /// <summary>L'icône d'une rubrique, selon qu'elle situe ou qu'elle range.</summary>
    public static QuestNodeGlyph GlyphOf(string? zone) =>
        QuestZoneOrder.IsPlace(zone) ? QuestNodeGlyph.Place : QuestNodeGlyph.Family;

    /// <summary>
    /// L'icône d'un lieu de combat, qui dit lequel des trois on regarde.
    ///
    /// Les trois se ressemblent assez pour partager un type ; ils ne se
    /// ressemblent pas assez pour partager une icône, la recherche pouvant
    /// rendre les trois d'un coup.
    /// </summary>
    public static QuestNodeGlyph GlyphOf(DungeonKind kind) => kind switch
    {
        DungeonKind.Raid => QuestNodeGlyph.Raids,
        DungeonKind.Lair => QuestNodeGlyph.Lairs,
        _ => QuestNodeGlyph.Dungeons,
    };

    /// <summary>
    /// Le nom d'une rubrique, tel que la fenêtre l'affiche au-dessus d'elle.
    ///
    /// Les quatre branches qui ne viennent pas du site n'ont pas de nom à y
    /// chercher, et le repli les nommait toutes « Rubrique » : un signalement
    /// sur le Minotoror portait « Rubrique › Minotoror » au lieu de
    /// « Donjons › Minotoror », et ne disait donc pas où regarder. Les chemins
    /// gardent leurs deux rangs, comme le fil d'Ariane les montre.
    /// </summary>
    public string NameOf(int section) => section switch
    {
        // RootSection n'est pas de la partie : c'est une vraie catégorie du
        // site, et son nom se lit dans le catalogue comme les autres.
        DungeonSection => Strings.Get("Dungeons"),
        RaidSection => Strings.Get("Raids"),
        LairSection => Strings.Get("Lairs"),
        DungeonPathSection => Strings.Get("Dungeons") + Separator + Strings.Get("Paths"),
        QuestPathSection => Strings.Get("QuestAreaCrumb") + Separator + Strings.Get("Paths"),
        _ => QuestZoneOrder.DisplayName(
                 _catalog.Catalog.Sections.FirstOrDefault(s => s.Id == section)?.Name)
             is { Length: > 0 } name
                 ? name
                 : Strings.Get("Section"),
    };

    public static string Nombre(int count) => Combien(count, "WordQuest", "WordQuests");

    /// <summary>
    /// Compte d'un intertitre de recherche. Le mot suit la nature : annoncer
    /// « 1 quête » au-dessus d'une zone ferait mentir l'intertitre juste
    /// au-dessus de ce qu'il coiffe.
    /// </summary>
    public static string Combien(int count, string singulier, string pluriel) =>
        Strings.Format("Count", count, Strings.Get(count == 1 ? singulier : pluriel));
}
