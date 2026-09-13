namespace DtHub.Core.Papycha;

/// <summary>
/// A quest, as the catalog keeps it.
///
/// Deliberately without the article body: indexing it would cost
/// twenty-two megabytes and thirty times more transfer, for
/// information obtained for free by opening the page. What is here
/// is enough to search and to file.
/// </summary>
public sealed record QuestSummary
{
    /// <summary>Id of the article on the site.</summary>
    public int Id { get; init; }

    /// <summary>Displayed title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Address of the page.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Recommended level, or zero when the site does not give it.
    /// </summary>
    public int Level { get; init; }

    /// <summary>Categories from the site, including the zone.</summary>
    public IReadOnlyList<int> Categories { get; init; } = [];

    /// <summary>Quest types, in the sense of the site's taxonomy.</summary>
    public IReadOnlyList<int> Types { get; init; } = [];

    /// <summary>Title reduced to a comparable form, computed once.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>
    /// Section chosen to place the quest in a search list.
    ///
    /// The smallest of those it belongs to: it is the most precise,
    /// and therefore the one that situates. "Astrub" situates better
    /// than "Quêtes".
    /// </summary>
    public int SectionId { get; init; }

    /// <summary>
    /// All the sections the quest belongs to.
    ///
    /// One alone was not enough. The site files "Le dragon d'Astrub"
    /// both under its main quests and under those of Astrub: a quest
    /// is both a place and a path, and forcing a single choice emptied
    /// the cross-cutting sections. Measured: the main quests page
    /// lists seventy-three, of which only twelve had no zone and were
    /// therefore the only ones left to stay there.
    /// </summary>
    public IReadOnlyList<int> SectionIds { get; init; } = [];

    /// <summary>
    /// Achievement the quest is part of, empty when the site does not
    /// say so.
    ///
    /// The attachment is read on the section pages, which group their
    /// quests under subheadings. Measured: three hundred seventy-three
    /// quests out of seven hundred eighty-two carry one. The others
    /// carry none, and nothing should invent one for them.
    /// </summary>
    public string SuccessName { get; init; } = string.Empty;

    /// <summary>
    /// Place of the quest in its prerequisite chain, zero if the site
    /// does not give it.
    ///
    /// This is not its place within its achievement: the three quests
    /// of "De la caillasse plein les poches" are worth 1, 6 and 6
    /// there, and the chain, which counts seven steps, crosses several
    /// achievements. It is nonetheless the only play order the site
    /// publishes, and it is better than alphabetical order for
    /// presenting the quests of an achievement.
    /// </summary>
    public int ChainStep { get; init; }

    /// <summary>
    /// Place of the quest within its achievement, zero if it is not
    /// known.
    ///
    /// Computed from the prerequisites the site publishes, which give
    /// a partial order: "Les rescapés de Frigost" requires "[FIN]
    /// L'essentiel est dans le Lac gelé", so the latter comes first.
    /// The site publishes this order nowhere else for most
    /// achievements.
    /// </summary>
    public int PlayOrder { get; init; }

    /// <summary>
    /// In-game position where the quest starts, "[4,-6]", empty if the
    /// site does not give it. Filled in on 687 quests out of 782.
    /// </summary>
    public string StartPosition { get; init; } = string.Empty;

    /// <summary>
    /// Character the quest starts with, empty if the site does not
    /// give it. Filled in on 693 quests out of 782.
    ///
    /// With the position, enough to compose the first step without
    /// reading the prose: "Rendez-vous en [4,-6], parlez à Yse
    /// Vewibad".
    /// </summary>
    public string StartPerson { get; init; } = string.Empty;

    /// <summary>
    /// What must have been done before this quest, as the site
    /// displays it.
    ///
    /// From two sources combined: the site's metadata, which gives a
    /// short free text for 130 quests, and the page's "précédents"
    /// column, which names the quests and milestones for 527.
    /// Together, 613 quests out of 782, where the level alone informs
    /// only 117.
    /// </summary>
    public IReadOnlyList<string> Prerequisites { get; init; } = [];
}

/// <summary>
/// A section of the tree: a category or a type from the site.
/// </summary>
public sealed record QuestSection
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Parent section, or zero at the root.</summary>
    public int Parent { get; init; }

    /// <summary>Number of quests filed directly under it.</summary>
    public int Count { get; init; }

    /// <summary>
    /// The site's page presenting this section, when it has one.
    ///
    /// This is not the WordPress category archive, which is only a
    /// list of articles: it is the written page, the one the "Quêtes"
    /// table designates. Empty for a section this table does not name.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    public string SearchKey { get; init; } = string.Empty;
}
