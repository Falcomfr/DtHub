using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestMenuParserTests
{
    /// <summary>
    /// Ranking captured on the site's "Quêtes" (Quests) page.
    /// </summary>
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
        // The site says "Quêtes d'Astrub" where the category says
        // "Astrub": an exact match would never happen.
        Assert.Equal(2, QuestMenuParser.RankOf(Classement, QuestSearch.Normalize("Astrub")));

        // "Île de Frigost" versus "Quêtes de Frigost": it is the
        // proper noun that brings them together, not the whole
        // phrase.
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
        // Without this count, "Quêtes du Château d'Amakna" would rank
        // just as well under "Amakna" as under "Château d'Amakna",
        // at the mercy of the list's order.
        var page = QuestSearch.Normalize("Quêtes du Château d'Amakna");

        var chateau = QuestMenuParser.Kinship(page, QuestSearch.Normalize("Château d'Amakna"));
        var amakna = QuestMenuParser.Kinship(page, QuestSearch.Normalize("Amakna"));

        Assert.True(chateau > amakna);
        Assert.True(amakna > 0);
    }

    [Fact]
    public void Deux_rubriques_sans_rapport_ne_se_rapprochent_pas()
    {
        // "Quêtes" and "Île" turn up everywhere: counting them would
        // bring absolutely anything close to absolutely anything.
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

    [Fact]
    public void Le_surplus_departage_deux_rubriques_que_le_meme_mot_rapproche()
    {
        // "Quêtes d'Amakna" shares "amakna" with "Amakna" just as it
        // does with "Château d'Amakna": the number of shared words
        // does not decide it. The one that adds the fewest extra
        // words is the closest.
        var page = QuestSearch.Normalize("Quêtes d'Amakna");

        Assert.Equal(
            QuestMenuParser.Kinship(page, QuestSearch.Normalize("Amakna")),
            QuestMenuParser.Kinship(page, QuestSearch.Normalize("Château d'Amakna")));

        Assert.Equal(0, QuestMenuParser.Surplus(page, QuestSearch.Normalize("Amakna")));
        Assert.Equal(1, QuestMenuParser.Surplus(page, QuestSearch.Normalize("Château d'Amakna")));
    }
}
