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
    /// A quest row's lock for the chosen character: met when the quest is in progress, done, or startable
    /// (<see cref="QuestProfit.Startable"/>, as <see cref="QuestProfit.IsDoable"/> reads it). Each prerequisite naming
    /// another quest gets a check when done and, on an unmet lock only, a missing mark; the others (items, free text,
    /// the quest itself) get none. Without a tracker, or with no prerequisite naming another quest on a quest neither
    /// started nor done, the row is left neutral.
    /// </summary>
    public static QuestNode Locked(QuestNode node, TrackerProgress? progress, QuestChainIndex? chain)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (progress is null || chain is null || node.Quest is not { } quest || node.Needs is not { Count: > 0 } needs)
        {
            return node;
        }

        var touched = StatusOf(progress, quest.Id) is QuestStatus.InProgress or QuestStatus.Completed;
        if (!touched && QuestProfit.Previous(quest, chain).Count == 0)
        {
            return node;
        }

        var met = touched || QuestProfit.Startable(quest, progress, chain);

        return node with
        {
            NeedsMet = met,
            Needs = [.. needs.Select(n => chain.Resolve(n.Text) is { } p && p.Id != quest.Id
                ? n with { Met = StatusOf(progress, p.Id) == QuestStatus.Completed ? true : met ? null : false }
                : n)],
        };
    }

    /// <summary>
    /// A zone's progress in the quest areas: the quests done once and for
    /// all that are done, and how many quests are started. A quest done again
    /// (daily, weekly, repeatable, seasonal) stays out of the count, or a zone
    /// would never read as finished. Null without a linked tracker, the
    /// columns then stay empty. Under the list's filters, the quests they
    /// keep are counted instead (by the same <see cref="Passes"/>, repeatables
    /// included, as the label): none of them is done, so "done over total"
    /// would say nothing.
    /// </summary>
    public static ZoneTally? ZoneProgress(
        TrackerProgress? progress, IReadOnlyCollection<QuestSummary> quests, bool toDo = false, bool available = false,
        Func<QuestSummary, bool>? startable = null)
    {
        ArgumentNullException.ThrowIfNull(quests);

        if (progress is null)
        {
            return null;
        }

        var once = quests
            .Where(q => !QuestRecurrenceLabel.IsRepeatable(q))
            .ToList();
        var tally = new ZoneTally(
            once.Count(q => StatusOf(progress, q.Id) == QuestStatus.Completed),
            once.Count,
            quests.Count(q => StatusOf(progress, q.Id) == QuestStatus.InProgress));
        if (!toDo && !available)
        {
            return tally;
        }

        // All the zone's quests, repeatables included, as its label counts them under the same filters.
        var passing = quests.Count(q => Passes(progress, q.Id, toDo, available, startable?.Invoke(q) ?? false));
        var key = "ZonePassing" + (toDo && available ? "Both" : toDo ? "ToDo" : "Available") + (passing == 1 ? "One" : "");
        return tally with { Passing = passing, PassingTip = Strings.Format(key, passing) };
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
    /// <paramref name="startable"/> is DT Hub's own reading
    /// (<see cref="QuestProfit.Startable"/>): when the site lists available
    /// quests (it does for its active character only), a quest must be listed
    /// and startable; otherwise startable alone decides.
    /// </summary>
    public static bool Passes(TrackerProgress? progress, int questId, bool toDo, bool available, bool startable)
    {
        if (progress is null)
        {
            return true;
        }

        if (toDo && StatusOf(progress, questId) is QuestStatus.Completed or QuestStatus.Ignored)
        {
            return false;
        }

        // The site's list, when it gives one, bounds what its active character can start.
        return !available || ((progress.Available.Count == 0 || progress.Available.Contains(questId)) && startable);
    }

    /// <summary>
    /// The quests to suggest next. The site's own list, in its order, when
    /// it gives one, less what is not startable (<see cref="QuestProfit.Startable"/>);
    /// it does for its active character only, so otherwise
    /// the quests not done whose previous quests are all completed, in play
    /// order. A quest naming no previous quest is no one's follow-up and
    /// stays out, as do ignored, blocked and repeatable ones, and, as under
    /// "Accessibles", one whose first step is above <paramref name="level"/>
    /// (zero: not known, nothing left out on it).
    /// </summary>
    public static IReadOnlyList<QuestSummary> NextQuests(
        TrackerProgress? progress, IReadOnlyList<QuestSummary> catalog,
        IReadOnlyDictionary<int, TouchQuestFacts>? facts = null, int level = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (progress is null)
        {
            return [];
        }

        var chain = new QuestChainIndex(catalog);
        if (progress.Next.Count > 0)
        {
            var byId = catalog.ToLookup(q => q.Id);

            return [.. progress.Next.Distinct().SelectMany(id => byId[id].Take(1)).Where(q => QuestProfit.Startable(q, progress, chain))];
        }

        // Repeatable quests follow their previous quests forever: they made the list very long.
        return QuestPlayOrder.Sorted(catalog.Where(q =>
            !QuestRecurrenceLabel.IsRepeatable(q)
            && StatusOf(progress, q.Id) is not (QuestStatus.Completed or QuestStatus.Ignored or QuestStatus.Blocked)
            && !(level > 0 && facts?.GetValueOrDefault(q.Id)?.Steps.FirstOrDefault() is { } first && first.OptimalLevel > level)
            && QuestProfit.Previous(q, chain) is { Count: > 0 } previous
            && previous.All(p => StatusOf(progress, p.Id) == QuestStatus.Completed)));
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

/// <summary>
/// A zone's progress: done over total among the quests done once, and what is started; under the list's filters,
/// <c>Passing</c> of those quests pass them, and <c>PassingTip</c> says so.
/// </summary>
public sealed record ZoneTally(int Done, int Total, int Started, int? Passing = null, string? PassingTip = null)
{
    public string Count => Passing is { } passing
        ? passing.ToString(CultureInfo.CurrentCulture)
        : string.Create(CultureInfo.CurrentCulture, $"{Done}/{Total}");

    /// <summary>False for a zone of repeatables only, nothing there is ever finished, and under a filter.</summary>
    public bool IsComplete => Passing is null && Total > 0 && Done == Total;

    public string StartedTip => Strings.Format("ZoneInProgress", Started);
}
