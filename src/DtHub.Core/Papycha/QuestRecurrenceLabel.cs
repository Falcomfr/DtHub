using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// The rhythm a quest row shows on its right when the quest can be done
/// again, as a glyph whose hover says it: a player looking for something to
/// do today spots the daily ones without opening the guide.
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
    public static string? Of(QuestSummary quest) => KindOf(quest) switch
    {
        QuestRecurrence.Daily => Strings.Get("RecurrenceDaily"),
        QuestRecurrence.Weekly => Strings.Get("RecurrenceWeekly"),
        QuestRecurrence.Repeatable => Strings.Get("RecurrenceRepeatable"),
        QuestRecurrence.Seasonal => Strings.Get("RecurrenceSeasonal"),
        _ => null,
    };

    /// <summary>
    /// The rhythm the row shows: the API's, except that the site's
    /// repeatable section wins over "once" or nothing.
    /// </summary>
    public static QuestRecurrence KindOf(QuestSummary quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        return quest.Recurrence switch
        {
            QuestRecurrence.Daily or QuestRecurrence.Weekly or QuestRecurrence.Repeatable or QuestRecurrence.Seasonal
                => quest.Recurrence,
            _ when quest.SectionIds.Contains(RepeatableSection) => QuestRecurrence.Repeatable,
            _ => quest.Recurrence,
        };
    }
}
