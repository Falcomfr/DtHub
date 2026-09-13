namespace DtHub.Core.Papycha;

/// <summary>
/// Type of a progression link, as the site distinguishes it.
/// </summary>
public enum QuestLinkKind
{
    /// <summary>Another quest.</summary>
    Quest,

    /// <summary>A validated or unlocked achievement.</summary>
    Success,

    /// <summary>Something else: a milestone, a step along the way.</summary>
    Milestone,
}

/// <summary>
/// A link to a quest or an achievement, as the page offers it.
/// </summary>
/// <param name="Title">Displayed label.</param>
/// <param name="Url">Absolute address.</param>
/// <param name="Kind">What it leads to.</param>
public sealed record QuestLink(string Title, string Url, QuestLinkKind Kind)
{
    /// <summary>
    /// What we leave by following this link: the destination
    /// achievement, failing that its zone. Empty when we stay in the
    /// same chain, which is the ordinary case, and the button then
    /// announces nothing more than the title.
    /// </summary>
    public string? Series { get; init; }

}

/// <summary>
/// What precedes and what follows a quest, read from the progression
/// block that the site places at the bottom of the article.
///
/// Deliberately lists: a quest can open several chains, each one
/// filed under its objective.
/// </summary>
public sealed record QuestChain
{
    public IReadOnlyList<QuestLink> Previous { get; init; } = [];

    public IReadOnlyList<QuestLink> Next { get; init; } = [];

    /// <summary>Immediate previous quest, if there is one.</summary>
    public QuestLink? PreviousQuest =>
        Previous.FirstOrDefault(l => l.Kind == QuestLinkKind.Quest);

    /// <summary>Immediate next quest, if there is one.</summary>
    public QuestLink? NextQuest =>
        Next.FirstOrDefault(l => l.Kind == QuestLinkKind.Quest);

    /// <summary>
    /// The previous quest that the site names, if it names only one.
    ///
    /// The column can carry several: naming just one would
    /// misrepresent what the site publishes, and this is already the
    /// rule for the prerequisites graph. Noted across the 782 guides:
    /// 448 columns name a single quest, 39 name several.
    /// </summary>
    public QuestLink? OnlyPreviousQuest => Only(Previous);

    /// <summary>
    /// The next quest that the site names, if it names only one.
    /// Noted: 325 columns name a single one, 79 several, and 213
    /// only name the achievement that was just validated.
    /// </summary>
    public QuestLink? OnlyNextQuest => Only(Next);

    private static QuestLink? Only(IReadOnlyList<QuestLink> links)
    {
        QuestLink? seul = null;

        foreach (var link in links)
        {
            if (link.Kind != QuestLinkKind.Quest)
            {
                continue;
            }

            if (seul is not null)
            {
                return null;
            }

            seul = link;
        }

        return seul;
    }
}
