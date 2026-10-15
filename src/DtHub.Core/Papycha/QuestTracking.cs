using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

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

    /// <summary>
    /// A zone's progress in the quest areas, in the place of its levels:
    /// "12/57" done, and "3 in progress" only when something is started. Two
    /// parts, so the list lays them in two aligned columns. Null without a
    /// linked tracker, the columns then stay empty.
    /// </summary>
    public static ZoneTally? ZoneProgress(TrackerProgress? progress, IReadOnlyCollection<int> questIds)
    {
        ArgumentNullException.ThrowIfNull(questIds);

        if (progress is null)
        {
            return null;
        }

        var done = questIds.Count(id => StatusOf(progress, id) == QuestStatus.Completed);
        var started = questIds.Count(id => StatusOf(progress, id) == QuestStatus.InProgress);
        return new ZoneTally(
            string.Create(CultureInfo.CurrentCulture, $"{done}/{questIds.Count}"),
            started > 0 ? Strings.Format("ZoneInProgress", started) : null);
    }

    /// <summary>
    /// The account's other characters for whom a quest is not done yet, in
    /// the account's order: offered after it is marked done for one, since a
    /// player often does a quest with several characters at once.
    /// </summary>
    public static IReadOnlyList<TrackerCharacter> OthersToFinish(TrackerState state, string characterId, int questId)
    {
        ArgumentNullException.ThrowIfNull(state);

        return
        [
            .. state.Characters.Where(c =>
                c.Id != characterId
                && StatusOf(state.Progress.GetValueOrDefault(c.Id), questId) != QuestStatus.Completed),
        ];
    }

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
        ?? (characters.Count > 0 ? characters[0] : null);
}

/// <summary>A zone's progress: done over total, and what is started.</summary>
public sealed record ZoneTally(string Done, string? Started);
