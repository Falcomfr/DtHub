namespace DtHub.Core.Papycha;

/// <summary>
/// Which optional columns a displayed list of rows reserves on its quest
/// rows: a column nobody fills in this list would only push the names to
/// the right.
/// </summary>
public static class QuestListColumns
{
    /// <summary>True when a quest row of the list has a level.</summary>
    public static bool HasQuestLevels(IEnumerable<QuestNode> nodes) =>
        nodes.Any(n => n.Quest is not null && n.Level.Length > 0);

    /// <summary>True when a quest row of the list carries a status, which every status but not started marks.</summary>
    public static bool HasQuestMarks(IEnumerable<QuestNode> nodes) =>
        nodes.Any(n => n.Quest is not null && n.Status != QuestStatus.NotStarted);
}
