using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestListColumnsTests
{
    private static QuestNode Quete(int niveau = 0, QuestStatus statut = QuestStatus.NotStarted) =>
        new(QuestNodeKind.Quest, "Une quête", Quest: new QuestSummary { Level = niveau }, Status: statut);

    [Fact]
    public void Aucune_quete_avec_niveau_ni_suivi_ne_reserve_rien()
    {
        List<QuestNode> liste = [new(QuestNodeKind.Header, "Titre"), Quete(), Quete()];

        Assert.False(QuestListColumns.HasQuestLevels(liste));
        Assert.False(QuestListColumns.HasQuestMarks(liste));
    }

    [Fact]
    public void Une_seule_quete_avec_niveau_reserve_la_colonne()
    {
        Assert.True(QuestListColumns.HasQuestLevels([Quete(), Quete(42)]));
    }

    [Theory]
    [InlineData(QuestStatus.Completed)]
    [InlineData(QuestStatus.InProgress)]
    [InlineData(QuestStatus.Blocked)]
    [InlineData(QuestStatus.Ignored)]
    public void Une_seule_quete_suivie_reserve_la_marque(QuestStatus statut)
    {
        Assert.True(QuestListColumns.HasQuestMarks([Quete(), Quete(statut: statut)]));
    }
}
