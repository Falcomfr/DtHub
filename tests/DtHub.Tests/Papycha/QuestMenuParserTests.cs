using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestMenuParserTests
{
    /// <summary>Classement relevé sur la page « Quêtes » du site.</summary>
    private static readonly string[] Classement =
    [
        QuestSearch.Normalize("Quêtes principales"),
        QuestSearch.Normalize("Quêtes du Krosmoz"),
        QuestSearch.Normalize("Quêtes d'Astrub"),
        QuestSearch.Normalize("Quêtes de Frigost"),
        QuestSearch.Normalize("Quêtes du Château d'Amakna"),
    ];

    [Fact]
    public void Une_rubrique_est_reconnue_dans_l_intitule_qui_la_contient()
    {
        // Le site dit « Quêtes d'Astrub » là où la catégorie dit « Astrub » :
        // une égalité ne se produirait jamais.
        Assert.Equal(2, QuestMenuParser.RankOf(Classement, QuestSearch.Normalize("Astrub")));

        // « Île de Frigost » face à « Quêtes de Frigost » : c'est le nom propre
        // qui rapproche, pas la phrase entière.
        Assert.Equal(3, QuestMenuParser.RankOf(Classement, QuestSearch.Normalize("Île de Frigost")));
    }

    [Fact]
    public void Une_rubrique_absente_du_classement_passe_a_la_fin()
    {
        Assert.Equal(int.MaxValue, QuestMenuParser.RankOf(Classement, "dedale"));
        Assert.Equal(int.MaxValue, QuestMenuParser.RankOf(Classement, string.Empty));
        Assert.Equal(int.MaxValue, QuestMenuParser.RankOf([], "astrub"));
    }

    [Fact]
    public void Le_nombre_de_mots_partages_departage_deux_rubriques_voisines()
    {
        // Sans ce compte, « Quêtes du Château d'Amakna » se rangerait aussi
        // bien sous « Amakna » que sous « Château d'Amakna », au hasard de
        // l'ordre de la liste.
        var page = QuestSearch.Normalize("Quêtes du Château d'Amakna");

        var chateau = QuestMenuParser.Kinship(page, QuestSearch.Normalize("Château d'Amakna"));
        var amakna = QuestMenuParser.Kinship(page, QuestSearch.Normalize("Amakna"));

        Assert.True(chateau > amakna);
        Assert.True(amakna > 0);
    }

    [Fact]
    public void Deux_rubriques_sans_rapport_ne_se_rapprochent_pas()
    {
        // « Quêtes » et « Île » se retrouvent partout : les compter
        // rapprocherait n'importe quoi de n'importe quoi.
        Assert.Equal(
            0,
            QuestMenuParser.Kinship(
                QuestSearch.Normalize("Quêtes de Sufokia"),
                QuestSearch.Normalize("Île de Frigost")));

        Assert.Equal(
            0,
            QuestMenuParser.Kinship(
                QuestSearch.Normalize("Quêtes des Îles"),
                QuestSearch.Normalize("Île de Pandala")));
    }

    [Theory]
    [InlineData(null, "astrub")]
    [InlineData("astrub", null)]
    [InlineData("", "")]
    public void Un_intitule_manquant_ne_rapproche_rien(string? first, string? second)
    {
        Assert.Equal(0, QuestMenuParser.Kinship(first, second));
    }
}
