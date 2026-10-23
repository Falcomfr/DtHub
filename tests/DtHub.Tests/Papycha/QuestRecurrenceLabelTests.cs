using DtHub.Core.Localization;
using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestRecurrenceLabelTests
{
    [Theory]
    [InlineData(QuestRecurrence.Daily, "RecurrenceDaily")]
    [InlineData(QuestRecurrence.Weekly, "RecurrenceWeekly")]
    [InlineData(QuestRecurrence.Repeatable, "RecurrenceRepeatable")]
    [InlineData(QuestRecurrence.Seasonal, "RecurrenceSeasonal")]
    public void Une_quete_repetable_dit_son_rythme(QuestRecurrence rythme, string clef)
    {
        var quest = new QuestSummary { Title = "Q", Recurrence = rythme };

        Assert.Equal(Strings.Get(clef), QuestRecurrenceLabel.Of(quest));
    }

    [Theory]
    [InlineData(QuestRecurrence.Unknown)]
    [InlineData(QuestRecurrence.Once)]
    public void Une_quete_de_la_rubrique_repetables_sans_rythme_est_dite_repetable(QuestRecurrence rythme)
    {
        // The site files it among its repeatable quests without telling the
        // rhythm, or its API says "once" against its own section.
        var quest = new QuestSummary { Title = "Q", Recurrence = rythme, SectionIds = [QuestRecurrenceLabel.RepeatableSection] };

        Assert.Equal(Strings.Get("RecurrenceRepeatable"), QuestRecurrenceLabel.Of(quest));
    }

    [Theory]
    [InlineData(QuestRecurrence.Unknown)]
    [InlineData(QuestRecurrence.Once)]
    public void Une_quete_qui_ne_se_refait_pas_ne_porte_rien(QuestRecurrence rythme)
    {
        var quest = new QuestSummary { Title = "Q", Recurrence = rythme, SectionIds = [18] };

        Assert.Null(QuestRecurrenceLabel.Of(quest));
    }

    [Theory]
    [InlineData(QuestRecurrence.Daily, new int[0], QuestRecurrence.Daily)]
    [InlineData(QuestRecurrence.Once, new[] { 18 }, QuestRecurrence.Once)]
    [InlineData(QuestRecurrence.Unknown, new[] { 18 }, QuestRecurrence.Unknown)]
    [InlineData(QuestRecurrence.Unknown, new[] { QuestRecurrenceLabel.RepeatableSection }, QuestRecurrence.Repeatable)]
    [InlineData(QuestRecurrence.Once, new[] { QuestRecurrenceLabel.RepeatableSection }, QuestRecurrence.Repeatable)]
    public void KindOf_suit_le_rythme_puis_la_rubrique(QuestRecurrence rythme, int[] rubriques, QuestRecurrence attendu)
    {
        var quest = new QuestSummary { Title = "Q", Recurrence = rythme, SectionIds = rubriques };

        Assert.Equal(attendu, QuestRecurrenceLabel.KindOf(quest));
    }
}
