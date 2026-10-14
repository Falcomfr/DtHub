namespace DtHub.Core.Papycha;

/// <summary>Progress of one quest for one character on the tracker.</summary>
public enum QuestStatus
{
    NotStarted,
    InProgress,
    Completed,
    Blocked,
    Ignored,
}

/// <summary>
/// The codes the site uses for <see cref="QuestStatus"/>. The tracker
/// stores a quest with no status as an absent entry, hence the empty
/// code for <see cref="QuestStatus.NotStarted"/>.
/// </summary>
public static class QuestStatusCodes
{
    /// <summary>Code the site expects for this status.</summary>
    public static string ToSite(QuestStatus status) => status switch
    {
        QuestStatus.InProgress => "in_progress",
        QuestStatus.Completed => "completed",
        QuestStatus.Blocked => "blocked",
        QuestStatus.Ignored => "ignored",
        _ => string.Empty,
    };

    /// <summary>
    /// Status for a code from the site. An unknown or empty code is
    /// "not started": a code added by the site must not break reading.
    /// </summary>
    public static QuestStatus FromSite(string? code) => code switch
    {
        "in_progress" => QuestStatus.InProgress,
        "completed" => QuestStatus.Completed,
        "blocked" => QuestStatus.Blocked,
        "ignored" => QuestStatus.Ignored,
        _ => QuestStatus.NotStarted,
    };
}
