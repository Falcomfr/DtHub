using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// The tag a quest row carries on its right when the quest can be done
/// again: a player looking for something to do today reads "Quotidienne"
/// without opening the guide.
/// </summary>
public static class QuestRecurrenceLabel
{
    /// <summary>
    /// The site's "Quêtes répétables" category. Measured on 2026-10-17: of
    /// its 128 quests, the API gives a rhythm for 95 only. The others are
    /// still repeatable, the site says so by filing them there.
    /// </summary>
    public const int RepeatableSection = 682;

    /// <summary>The tag, or <c>null</c> for a quest done once.</summary>
    public static string? Of(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        return quest.Recurrence switch
        {
            QuestRecurrence.Daily => Strings.Get("RecurrenceDaily"),
            QuestRecurrence.Weekly => Strings.Get("RecurrenceWeekly"),
            QuestRecurrence.Repeatable => Strings.Get("RecurrenceRepeatable"),
            QuestRecurrence.Seasonal => Strings.Get("RecurrenceSeasonal"),
            _ when quest.SectionIds.Contains(RepeatableSection) => Strings.Get("RecurrenceRepeatable"),
            _ => null,
        };
    }
}
