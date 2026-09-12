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
        // **Le défaut que cette épreuve tient.** Une seule étape rapportait son
        // avancement, celle des quêtes, et c'est la plus courte : le compteur
        // atteignait « 782 / 782 » en quelques secondes puis restait figé
        // pendant les quatre cinquièmes du temps. Les autres étapes sont des
        // lectures d'un seul tenant : elles se nomment, elles ne se comptent
        // pas, et un « 0 / 0 » y serait pire que rien.
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
        // Quatre phrases distinctes, sans quoi nommer les étapes n'apprendrait
        // rien de plus que l'ancien compteur figé.
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
        // Le site annonce son total dans un en-tête de reponse : tant qu'il
        // n'est pas lu, afficher « 0 / 0 » ferait croire à un site vide.
        var texte = QuestIndexingLabel.For(new QuestIndexingProgress(0, 0));

        Assert.NotEmpty(texte);
        Assert.DoesNotContain("0", texte, StringComparison.Ordinal);
    }
}
