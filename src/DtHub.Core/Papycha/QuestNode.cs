namespace DtHub.Core.Papycha;

/// <summary>What a row of the dropdown list offers.</summary>
public enum QuestNodeKind
{
    /// <summary>A branch to unfold: a zone of quests.</summary>
    Branch,

    /// <summary>A quest to open.</summary>
    Quest,

    /// <summary>A branch announced but not yet made.</summary>
    Pending,

    /// <summary>A section subheading, which is not clickable.</summary>
    Header,

    /// <summary>
    /// The title of a group of results: zones, achievements, quests.
    ///
    /// Distinct from the ordinary subheading because it sits above
    /// it: in a search, these three top achievements that are
    /// themselves subheadings, and the two levels used to blend
    /// together.
    /// </summary>
    Section,

    /// <summary>
    /// An achievement, which tops its quests without being clickable.
    ///
    /// Distinct from the ordinary subheading in order to carry its
    /// star: the "Areas" and "Quests" subheadings of a search do not
    /// want one.
    /// </summary>
    Success,
}

/// <summary>
/// The icon of a row, which tells its nature before it has been read.
///
/// Separate from <see cref="QuestNodeKind"/>, which says what the
/// click will do: two branches unfold the same way without
/// designating the same thing, a zone of the world and a family of
/// quests.
/// </summary>
public enum QuestNodeGlyph
{
    /// <summary>None: quests, the most numerous rows.</summary>
    None,

    /// <summary>The root of quests.</summary>
    Quests,

    /// <summary>The root of dungeons.</summary>
    Dungeons,

    /// <summary>The root of raids.</summary>
    Raids,

    /// <summary>The root of lairs.</summary>
    Lairs,

    /// <summary>A place in the world.</summary>
    Place,

    /// <summary>A path, which joins two places.</summary>
    Route,

    /// <summary>A family of quests, which is not located anywhere.</summary>
    Family,

    /// <summary>An achievement.</summary>
    Success,
}

/// <summary>
/// A row of the dropdown list: a branch, a quest, a subheading.
/// </summary>
/// <param name="Kind">What the click will trigger.</param>
/// <param name="Label">Displayed text.</param>
/// <param name="Detail">
/// Detail on the right: a number of quests, a level.
/// </param>
/// <param name="Id">Identifier of the section, when it is one.</param>
/// <param name="Glyph">The icon that announces the row's nature.</param>
/// <param name="InSuccess">
/// True when the row is a quest of the achievement that tops it. It
/// is then indented, and a thread links it to the others: this
/// indent is what says that a quest without it belongs to no
/// achievement.
/// </param>
/// <param name="Spaced">
/// True when a blank must follow the row, because what comes after
/// changes in nature.
/// </param>
/// <param name="Needs">
/// What the hover shows: a quest's prerequisites, one per line. Empty
/// when the site gives none, and the row then shows no icon.
/// </param>
/// <param name="Quest">The quest, when it is one.</param>
/// <param name="Dungeon">The combat location, when it is one.</param>
/// <param name="Path">The path, when it is one.</param>
/// <param name="Facts">
/// The three facts a dungeon row states in its own columns, or
/// <c>null</c> for every other kind of row. One nullable member rather
/// than three, since six of the seven kinds have none: the template
/// then has a single question to ask before laying the columns out.
/// </param>
public sealed record QuestNode(
    QuestNodeKind Kind,
    string Label,
    string? Detail = null,
    int Id = 0,
    QuestSummary? Quest = null,
    DungeonSummary? Dungeon = null,
    PathSummary? Path = null,
    QuestNodeGlyph Glyph = QuestNodeGlyph.None,
    IReadOnlyList<QuestNeed>? Needs = null,
    bool InSuccess = false,
    bool Spaced = false,
    DungeonFacts? Facts = null)
{
    /// <summary>
    /// Neither unmade branches nor subheadings are clickable.
    /// </summary>
    public bool IsEnabled => Kind is QuestNodeKind.Branch or QuestNodeKind.Quest;

    /// <summary>
    /// The address the row opens, whether it leads to a quest or a
    /// dungeon.
    /// </summary>
    public string? Url => Quest?.Url ?? Dungeon?.Url ?? Path?.Url;

    /// <summary>True when the row has something to say on hover.</summary>
    public bool HasNeeds => Needs is { Count: > 0 };
}

/// <summary>
/// A quest's prerequisite, and the quest it names when it is one.
///
/// The site writes all sorts of them: quests, but also items to
/// bring, an alignment, a number of players, a level, time slots.
/// Only the first kind is clickable.
/// </summary>
/// <param name="Text">The prerequisite as the site writes it.</param>
/// <param name="Quest">The quest it designates, or <c>null</c>.</param>
public sealed record QuestNeed(string Text, QuestSummary? Quest)
{
    /// <summary>True when the prerequisite leads somewhere.</summary>
    public bool IsQuest => Quest is not null;
}
