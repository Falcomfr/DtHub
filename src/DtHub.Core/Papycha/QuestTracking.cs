namespace DtHub.Core.Papycha;

/// <summary>How the guides list shows a quest the player tracks.</summary>
public enum QuestMark
{
    None,
    InProgress,
    Done,
    Dimmed,
}

/// <summary>
/// The rules that lay the player's tracker over the guides list. Pure, so
/// the view models stay thin and the rules are tested without a window.
/// </summary>
public static class QuestTracking
{
    /// <summary>
    /// Completed wins over the explicit status: the site infers some quests
    /// as done without storing a status for them.
    /// </summary>
    public static QuestStatus StatusOf(TrackerProgress? progress, int questId)
    {
        if (progress is null)
        {
            return QuestStatus.NotStarted;
        }

        if (progress.Completed.Contains(questId))
        {
            return QuestStatus.Completed;
        }

        return progress.Statuses.TryGetValue(questId, out var status) ? status : QuestStatus.NotStarted;
    }

    public static QuestMark MarkOf(TrackerProgress? progress, int questId) => StatusOf(progress, questId) switch
    {
        QuestStatus.Completed => QuestMark.Done,
        QuestStatus.InProgress => QuestMark.InProgress,
        QuestStatus.Blocked or QuestStatus.Ignored => QuestMark.Dimmed,
        _ => QuestMark.None,
    };

    /// <summary>
    /// Whether a quest stays in the list under the two filters. Without a
    /// linked tracker nothing is filtered: the list must read as before.
    /// </summary>
    public static bool Passes(TrackerProgress? progress, int questId, bool toDo, bool available)
    {
        if (progress is null)
        {
            return true;
        }

        if (toDo && StatusOf(progress, questId) is QuestStatus.Completed or QuestStatus.Ignored)
        {
            return false;
        }

        return !available || progress.Available.Contains(questId);
    }

    /// <summary>
    /// Moving past the first step of an untouched quest starts it. The step
    /// restored when the guide reopens does not count, or merely opening a
    /// guide would mark it started.
    /// </summary>
    public static bool ShouldStart(QuestStatus status, int stepCount, int index, int resumedIndex) =>
        status == QuestStatus.NotStarted && stepCount >= 2 && index >= 1 && index != resumedIndex;

    public static bool IsLastStep(int stepCount, int index) => stepCount >= 1 && index == stepCount - 1;

    /// <summary>
    /// The remembered character may have been archived on the site, so fall
    /// back to the site's active one, then to the first.
    /// </summary>
    public static TrackerCharacter? PickCharacter(
        IReadOnlyList<TrackerCharacter> characters, string? rememberedId, string? activeId) =>
        characters.FirstOrDefault(c => c.Id == rememberedId)
        ?? characters.FirstOrDefault(c => c.Id == activeId)
        ?? characters.FirstOrDefault();
}
