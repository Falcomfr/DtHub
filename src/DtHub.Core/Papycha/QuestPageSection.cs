namespace DtHub.Core.Papycha;

/// <summary>
/// A section as the site presents it on its "Quêtes" (Quests) page.
///
/// These are not WordPress categories but hand-maintained pages. The
/// distinction matters: measured across the 782 quests, the
/// categories file 632 and leave the other 150 without a section,
/// reachable only through search. These pages claim 120 more and
/// name groupings that no category carries, such as the Krosmoz,
/// Sufokia, or the Bulles Temporelles (Temporal Bubbles).
/// </summary>
public sealed record QuestPageSection
{
    /// <summary>
    /// Title given by the site, "Quêtes du Krosmoz" (Krosmoz Quests)
    /// for example.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Page address.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Addresses of the quests it lists.</summary>
    public IReadOnlyList<string> QuestUrls { get; init; } = [];

    /// <summary>Groups on the page, in its order.</summary>
    public IReadOnlyList<QuestPageGroup> Groups { get; init; } = [];
}

/// <summary>
/// A subheading on a section page and the quests it heads.
///
/// The site groups its quests by achievement, in the form
/// <c>&lt;strong&gt;[Succès] Nom :&lt;/strong&gt;</c> (French for
/// "[Achievement] Name:") followed by a list. This is the only place
/// where a quest's link to its achievement is readable without
/// opening the quest's page: the intro block carries it too, but it
/// would take seven hundred and eighty two requests to read it
/// everywhere.
///
/// Not every subheading is an achievement: a page also says "Divers"
/// (Miscellaneous) or "Quêtes des Calanques d'Astrub" (Astrub Coves
/// Quests). Only achievements are treated as such, the rest does not
/// claim to be one.
/// </summary>
public sealed record QuestPageGroup
{
    /// <summary>
    /// Title, without the "[Succès]" (Achievement) marker or the
    /// trailing colon.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>True if the subheading announces an achievement.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>Addresses of the quests it heads.</summary>
    public IReadOnlyList<string> QuestUrls { get; init; } = [];
}
