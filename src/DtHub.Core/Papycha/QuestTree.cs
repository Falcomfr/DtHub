using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// Builds the branches of the guide list from the catalogue.
///
/// These rules used to live in the window's view model, mixed in with the
/// navigation stack and the search. Yet they are pure: they touch neither WPF,
/// nor the network, nor the disk, and they return data objects built from a
/// catalogue and a tally. Leaving them there amounted to keeping them out of
/// the reach of tests, in a project that none of them reaches.
///
/// Navigation stays where it is: it holds a window's state, and that is indeed
/// a view model's job.
/// </summary>
public sealed class QuestTree
{
    /// <summary>The site's category that holds all the quests.</summary>
    public const int RootSection = 7;

    /// <summary>
    /// Branches that do not come from the site's categories. Their identifiers
    /// are negative, out of reach of the site's, which are positive.
    /// </summary>
    public const int DungeonSection = -100;
    public const int RaidSection = -101;
    public const int LairSection = -102;
    public const int QuestPathSection = -103;
    public const int DungeonPathSection = -104;

    /// <summary>
    /// The chevron that separates the levels of a breadcrumb trail. It lives
    /// here because three places used to write it each on their own: the
    /// window, a report's landmark, and the name of a two-level section. They
    /// must read the same way, the landmark serving precisely to find the page
    /// again in the list.
    /// </summary>
    public const string Separator = "  \u203a  ";

    /// <summary>
    /// The three kinds of combat locations, in the order the root presents
    /// them, with their title and the count's words.
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
    /// What prerequisites link to, set by the caller for each catalogue. Used
    /// to attach a prerequisite to the quest it names.
    /// </summary>
    public QuestChainIndex? Chain { get; set; }

    /// <param name="catalog">The catalogue read from the site.</param>
    /// <param name="sectionCounts">
    /// The number of quests per section. The dictionary is the view model's
    /// own, which recounts it when the catalogue changes: passing it by
    /// reference avoids copying it on every display.
    /// </param>
    public QuestTree(QuestCatalogService catalog, IReadOnlyDictionary<int, int> sectionCounts)
    {
        _catalog = catalog;
        _sectionCounts = sectionCounts;
    }

    /// <summary>
    /// The root's four entries: the quests, and the three kinds of combat
    /// locations.
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

    /// <summary>The branch where a combat location belongs.</summary>
    public static int SectionOf(DungeonSummary place) => place.Kind switch
    {
        DungeonKind.Raid => RaidSection,
        DungeonKind.Lair => LairSection,
        _ => DungeonSection,
    };

    /// <summary>The combat locations of a kind, in the site's order.</summary>
    public IReadOnlyList<DungeonSummary> Fighting(DungeonKind kind) =>
        [.. _catalog.Catalog.Dungeons.Where(d => d.Kind == kind)];

    /// <summary>The paths on one side.</summary>
    public IReadOnlyList<PathSummary> Paths(PathSide side) =>
        [.. _catalog.Catalog.Paths.Where(p => p.Side == side)];

    /// <summary>
    /// The paths sub-branch, at the head of the branch it serves.
    ///
    /// A folder rather than a group tacked on after: a path compares neither
    /// to a zone nor to a dungeon, and mixing them would lengthen a list that
    /// is already long to go through.
    /// </summary>
    public QuestNode PathBranch(PathSide side, int section) => new(
        QuestNodeKind.Branch,
        Strings.Get("Paths"),
        Combien(Paths(side).Count, "WordPath", "WordPaths"),
        Id: section,
        Glyph: QuestNodeGlyph.Route);

    /// <summary>
    /// The dungeons, from the most approachable to the most demanding, cut
    /// into bands of fifty levels.
    ///
    /// Eighty-three lines cannot be scanned in one glance: one looks there for
    /// what is within reach, and the bands save counting. Those for which the
    /// site does not give a level bring up the rear under their own header,
    /// rather than passing for level zero.
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
    /// A dungeon line: its bare name, and its three facts for the columns on
    /// the right.
    ///
    /// **The level left the name.** It used to be formatted into it, which made
    /// the whole line one run of text that nothing could align or weight
    /// separately. It also made the list disagree with the banner above it,
    /// which has always shown the title alone.
    /// </summary>
    public static QuestNode NodeOf(DungeonSummary dungeon) => new(
        QuestNodeKind.Quest,
        (dungeon ?? throw new ArgumentNullException(nameof(dungeon))).Title,
        Dungeon: dungeon,
        Facts: DungeonFacts.Of(dungeon));

    /// <summary>
    /// Sections that contain at least one quest, in the order the site files
    /// them.
    ///
    /// The catalogue already returns them ordered; resorting them by quest
    /// count, as used to be done, amounted to ignoring the site's order after
    /// having gone to fetch it.
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

            // Whatever is not part of progression comes after a header, so
            // that the list does not mix a place with a track.
            if (!separated && QuestZoneOrder.IsExtra(zone.Name))
            {
                separated = true;

                yield return new QuestNode(
                    QuestNodeKind.Header,
                    QuestZoneOrder.ExtrasHeader,
                    Glyph: QuestNodeGlyph.Family);
            }

            // A blank line where one goes from a family of quests to places:
            // "Quêtes principales" ("Main quests") opens the list without
            // being a place, and without this breathing space it reads as the
            // world's first zone. The blank belongs to the row that precedes
            // the break, and not to the one that follows it, so as not to
            // double the one from the header further below.
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
    /// Adds a section's quests in the order they are played.
    ///
    /// This is how the site presents them, and how they are played: an
    /// isolated quest rarely says what it is for. The sorting is that of
    /// <see cref="QuestZonePlan"/>, which follows the prerequisites.
    /// </summary>
    public IEnumerable<QuestNode> BySuccess(IReadOnlyList<QuestSummary> quests)
    {
        var plan = QuestZonePlan.Of(quests, _catalog.Catalog.SuccessOrder);

        // The count announced is that of the whole achievement, not that of
        // the fragment: a series cut by a lone quest remains a single series,
        // and its first header must say how many quests it carries in total.
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
                // The indentation says the belonging: a quest flush with the
                // margin is not claimed by any achievement.
                yield return NodeOf(quest) with { InSuccess = block.IsSuccess };
            }
        }
    }


    /// <summary>
    /// A quest line.
    ///
    /// The right-hand column no longer carries the level: the site only gives
    /// it for one hundred and seventeen quests out of seven hundred and
    /// eighty-two, and a column empty nine times out of ten does not earn its
    /// place. It carries the prerequisites instead, which cover six hundred
    /// and thirteen of them, and which say something useful before setting
    /// off: what needs to have been done.
    /// </summary>
    public QuestNode NodeOf(QuestSummary quest) => new(
        QuestNodeKind.Quest,
        quest.Title,
        Quest: quest,
        Needs: NeedsOf(quest));

    /// <summary>
    /// A quest's prerequisites, each attached to the quest it names when it is
    /// one. Out of five hundred and sixty-seven distinct prerequisites, many
    /// are items, an alignment or a time slot: those stay as text, and only
    /// the others become links.
    /// </summary>
    public IReadOnlyList<QuestNeed> NeedsOf(QuestSummary quest) =>
        quest.Prerequisites.Count == 0
            ? []
            : [.. quest.Prerequisites.Select(need => new QuestNeed(need, Chain?.Find(need)))];


    /// <summary>
    /// A section's icon, depending on whether it locates a place or files a
    /// family.
    /// </summary>
    public static QuestNodeGlyph GlyphOf(string? zone) =>
        QuestZoneOrder.IsPlace(zone) ? QuestNodeGlyph.Place : QuestNodeGlyph.Family;

    /// <summary>
    /// A combat location's icon, which says which of the three is being looked
    /// at.
    ///
    /// The three are similar enough to share a type; they are not similar
    /// enough to share an icon, since a search can return all three at once.
    /// </summary>
    public static QuestNodeGlyph GlyphOf(DungeonKind kind) => kind switch
    {
        DungeonKind.Raid => QuestNodeGlyph.Raids,
        DungeonKind.Lair => QuestNodeGlyph.Lairs,
        _ => QuestNodeGlyph.Dungeons,
    };

    /// <summary>
    /// A section's name, as the window displays it above itself.
    ///
    /// The four branches that do not come from the site have no name to look
    /// up there, and the fallback used to name them all "Section": a report
    /// about the Minotoror carried "Section › Minotoror" instead of "Dungeons
    /// › Minotoror", and therefore did not say where to look. Paths keep their
    /// two levels, as the breadcrumb trail shows them.
    /// </summary>
    public string NameOf(int section) => section switch
    {
        // RootSection is not part of this: it is a real category from the
        // site, and its name is read from the catalogue like the others.
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
    /// Count for a search header. The word follows the kind: announcing "1
    /// quest" above a zone would make the header lie right above what it caps.
    /// </summary>
    public static string Combien(int count, string singulier, string pluriel) =>
        Strings.Format("Count", count, Strings.Get(count == 1 ? singulier : pluriel));
}
