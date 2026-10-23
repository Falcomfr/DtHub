using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestListColumnsTests
{
    private static QuestNode Quete(int niveau = 0) =>
        new(QuestNodeKind.Quest, "Une quête", Quest: new QuestSummary { Level = niveau });

    [Fact]
    public void Aucune_quete_avec_niveau_ne_reserve_rien()
    {
        List<QuestNode> liste = [new(QuestNodeKind.Header, "Titre"), Quete(), Quete()];

        Assert.False(QuestListColumns.HasQuestLevels(liste));
    }

    [Fact]
    public void Une_seule_quete_avec_niveau_reserve_la_colonne()
    {
        Assert.True(QuestListColumns.HasQuestLevels([Quete(), Quete(42)]));
    }

    [Fact]
    public void Un_seul_gain_reserve_ses_colonnes()
    {
        List<QuestNode> rien = [new(QuestNodeKind.Header, "Titre", GainXp: "1 k", Tier: 3), Quete()];

        Assert.False(QuestListColumns.HasQuestGains(rien));
        Assert.True(QuestListColumns.HasQuestGains([Quete(), Quete() with { GainXp = "86 k" }]));
    }

    [Fact]
    public void Un_donjon_qui_fait_avancer_des_quetes_reserve_aussi_les_colonnes_de_gain()
    {
        var donjon = new QuestNode(QuestNodeKind.Quest, "Donjon", Dungeon: new DungeonSummary(), GainXp: "1,2 M");

        Assert.True(QuestListColumns.HasQuestGains([donjon]));
    }
}
