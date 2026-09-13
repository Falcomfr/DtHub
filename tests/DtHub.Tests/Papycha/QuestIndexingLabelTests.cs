using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestIndexingLabelTests
{
    [Fact]
    public void Les_quetes_se_comptent()
    {
        var texte = QuestIndexingLabel.For(new QuestIndexingProgress(300, 782));

        Assert.Contains("300", texte, StringComparison.Ordinal);
        Assert.Contains("782", texte, StringComparison.Ordinal);
    }

    [Fact]
    public void Les_autres_etapes_ne_comptent_rien()
    {
        // **The flaw that this test pins down.** Only one phase
        // used to report its progress, the quests one, and it is
        // the shortest: the counter would reach "782 / 782" within
        // a few seconds and then stay frozen for four-fifths of the
        // time. The other phases are single, uninterrupted reads:
        // they are named, they are not counted, and a "0 / 0" there
        // would be worse than nothing.
        foreach (var phase in new[]
        {
            QuestIndexingPhase.Sections,
            QuestIndexingPhase.Dungeons,
            QuestIndexingPhase.Paths,
            QuestIndexingPhase.Arranging,
        })
        {
            var texte = QuestIndexingLabel.For(new QuestIndexingProgress(0, 0, phase));

            Assert.NotEmpty(texte);
            Assert.DoesNotContain("0 / 0", texte, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Chaque_etape_se_dit_autrement()
    {
        // Four distinct sentences, without which naming the phases
        // would teach nothing more than the old frozen counter.
        var phases = new[]
        {
            QuestIndexingPhase.Quests,
            QuestIndexingPhase.Sections,
            QuestIndexingPhase.Dungeons,
            QuestIndexingPhase.Paths,
            QuestIndexingPhase.Arranging,
        };

        var dits = phases
            .Select(p => QuestIndexingLabel.For(new QuestIndexingProgress(0, 0, p)))
            .ToList();

        Assert.Equal(phases.Length, dits.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Sans_total_les_quetes_se_taisent_sur_le_compte()
    {
        // The site announces its total in a response header: as
        // long as it has not been read, showing "0 / 0" would make
        // it look like an empty site.
        var texte = QuestIndexingLabel.For(new QuestIndexingProgress(0, 0));

        Assert.NotEmpty(texte);
        Assert.DoesNotContain("0", texte, StringComparison.Ordinal);
    }
}
