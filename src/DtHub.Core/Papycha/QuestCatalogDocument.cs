namespace DtHub.Core.Papycha;

/// <summary>
/// The catalog as it is stored on disk.
///
/// This is a cache, not a setting: losing it only costs a new
/// indexing run. It still carries a schema version, so that a change
/// in shape results in a rebuild rather than a skewed read.
/// </summary>
public sealed class QuestCatalogDocument
{
    /// <summary>
    /// Version 2: each quest carries the name of its sections, so that
    /// searching "frigost" returns Frigost's quests and not only those
    /// whose title carries the word.
    ///
    /// Version 3: the main section of each quest, and the order in
    /// which the site arranges its sections.
    ///
    /// Version 4: a quest is filed under one section and only one, and
    /// the sections the site keeps by hand come to complete its
    /// categories.
    ///
    /// Version 5: the achievement each quest is part of.
    ///
    /// Version 6: the quests of the same achievement are gathered
    /// under a single section.
    ///
    /// Version 7: a quest belongs to every section that claims it, the
    /// site itself filing it in several places.
    ///
    /// Version 8: the order in which the site presents its
    /// achievements.
    ///
    /// Version 9: achievements also come from the embedded map, drawn
    /// from each quest's intro block.
    ///
    /// Version 10: the place of each quest within its achievement.
    ///
    /// Version 11: the starting position and character, and the
    /// section key built on the displayed sections.
    ///
    /// Version 12: the prerequisites of each quest, and the section
    /// key removed, search now bearing only on titles.
    ///
    /// Version 13: the address of each section's written page, the
    /// one the "Quêtes" table designates.
    ///
    /// Version 14: the dungeons, with their level, their key and their
    /// soul stone.
    ///
    /// Version 15: the paths, and the kind of a combat location,
    /// dungeon, raid or lair.
    ///
    /// The site's fingerprint, added after the fifteenth, did not
    /// call for a sixteenth: its fields are absent from an older
    /// catalog, which counts as "unknown" and triggers a reread, just
    /// once. A version is only owed when what is already written
    /// would change meaning.
    ///
    /// Version 16: the order of achievements is now that of the
    /// achievements the quests carry, and not that of the site's
    /// headings. What is already written therefore changes meaning:
    /// thirty of the ninety-six entries in a fifteenth-schema catalog
    /// name no achievement at all. An unchanged site fingerprint would
    /// trigger no reread, and the old ordering would stay in place.
    /// </summary>
    public const int CurrentSchemaVersion = 16;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Time of the last successful indexing run.</summary>
    public DateTimeOffset? IndexedUtc { get; set; }

    /// <summary>
    /// What the site announced at the time of this read: date of the
    /// last modified article, and total number of articles. Comparing
    /// them to what it announces today says whether it has changed,
    /// for the price of one request. Absent from an older catalog,
    /// which counts as "unknown" and triggers a reread, just once.
    /// </summary>
    public DateTimeOffset? SiteModifiedUtc { get; set; }

    /// <inheritdoc cref="SiteModifiedUtc" />
    public int SitePosts { get; set; }

    /// <summary>
    /// What each category read announced at that time. Empty in an
    /// older catalog, which counts as "unknown" and triggers a reread,
    /// just once.
    /// </summary>
    public List<CategoryStamp> SiteCategories { get; set; } = [];

    public List<QuestSummary> Quests { get; set; } = [];

    public List<QuestSection> Sections { get; set; } = [];

    /// <summary>
    /// The site's dungeons. Kept apart from quests: they have neither
    /// achievement nor prerequisites, but a level, a key and a soul
    /// stone.
    /// </summary>
    public List<DungeonSummary> Dungeons { get; set; } = [];

    /// <summary>
    /// The site's paths, each filed on the side it serves. They have
    /// neither level nor key: a route is not played, it is followed.
    /// </summary>
    public List<PathSummary> Paths { get; set; } = [];

    /// <summary>
    /// Section headings in the site's order, reduced to a comparable
    /// form. Empty if the menu could not be read: it then falls back
    /// to ordering by quest count.
    /// </summary>
    public List<string> SectionOrder { get; set; } = [];

    /// <summary>
    /// Achievement headings in the order the site's pages present them.
    ///
    /// This order is one of progression, and it is found nowhere else:
    /// sorting them alphabetically, as used to be done, put "Épilogue
    /// hivernal" before "L'hiver arrive".
    /// </summary>
    public List<string> SuccessOrder { get; set; } = [];

    /// <summary>
    /// True if the catalog is unusable as is and must be rebuilt. An
    /// aged catalog is still used: a list from yesterday is better
    /// than no list at all when the network is missing.
    /// </summary>
    public bool NeedsRebuild =>
        SchemaVersion != CurrentSchemaVersion || Quests.Count == 0;
}
