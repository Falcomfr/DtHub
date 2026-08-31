using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestZoneOrderTests
{
    [Theory]
    [InlineData("Quêtes du Port de Madrestam", "Port de Madrestam")]
    [InlineData("Quêtes des Bworks", "Bworks")]
    [InlineData("Quêtes de Sufokia", "Sufokia")]
    [InlineData("Quêtes de la Sain Ballotin", "Sain Ballotin")]
    [InlineData("Quêtes des Bulles Temporelles", "Bulles Temporelles")]
    [InlineData("Quêtes du Krosmoz", "Krosmoz")]
    public void Le_prefixe_tombe_quand_un_article_le_suit(string brut, string attendu)
    {
        Assert.Equal(attendu, QuestZoneOrder.DisplayName(brut));
    }

    [Theory]
    [InlineData("Quêtes principales")]
    [InlineData("Quêtes répétables")]
    [InlineData("Autres quêtes")]
    public void Sans_article_le_mot_fait_partie_du_nom(string nom)
    {
        // « Quêtes principales » ne désigne pas un endroit mais une sorte de
        // quête : en retirer le premier mot ne laisserait rien de sensé.
        Assert.Equal(nom, QuestZoneOrder.DisplayName(nom));
    }

    [Theory]
    [InlineData("Île de Frigost")]
    [InlineData("Château d'Amakna")]
    [InlineData("[Alignement] Bontarien")]
    [InlineData("Bonta & Cania")]
    public void Un_nom_de_categorie_est_laisse_tel_quel(string nom)
    {
        Assert.Equal(nom, QuestZoneOrder.DisplayName(nom));
    }

    [Fact]
    public void La_progression_va_d_albuera_a_frigost()
    {
        string[] attendu =
        [
            "Quêtes principales", "Albuera", "Astrub", "Amakna", "Château d'Amakna",
            "Quêtes du Port de Madrestam", "Quêtes des Bworks", "Île des Wabbits",
            "Bonta & Cania", "Île d'Otomaï", "Île de Pandala", "Quêtes de Sufokia",
            "Île d'Orado", "Archipel de Vulkania", "Îlot Rifique", "Île de Frigost",
        ];

        var rangs = attendu.Select(QuestZoneOrder.RankOf).ToList();

        Assert.Equal(rangs.OrderBy(r => r), rangs);
        Assert.All(rangs, r => Assert.True(r < QuestZoneOrder.UnknownRank));
    }

    [Theory]
    [InlineData("Quêtes du Krosmoz")]
    [InlineData("Île de Nowel")]
    [InlineData("Quêtes de la Sain Ballotin")]
    [InlineData("Quêtes des Bulles Temporelles")]
    [InlineData("Dédale")]
    [InlineData("[Alignement] Bontarien")]
    [InlineData("[Alignement] Brâkmarien")]
    [InlineData("Quêtes répétables")]
    [InlineData("Autres quêtes")]
    public void Le_supplement_vient_apres_la_progression(string nom)
    {
        Assert.True(QuestZoneOrder.IsExtra(nom), nom);
        Assert.True(QuestZoneOrder.RankOf(nom) > QuestZoneOrder.UnknownRank, nom);
    }

    [Fact]
    public void Une_zone_inconnue_se_range_en_fin_de_progression_pas_au_milieu()
    {
        // Le site peut en ajouter : une nouveauté doit se voir sans dérégler ce
        // qui la précède, et sans tomber dans le supplément.
        var rang = QuestZoneOrder.RankOf("Île de Nulle Part");

        Assert.Equal(QuestZoneOrder.UnknownRank, rang);
        Assert.False(QuestZoneOrder.IsExtra("Île de Nulle Part"));
        Assert.True(rang > QuestZoneOrder.RankOf("Île de Frigost"));
    }

    [Fact]
    public void Le_rang_se_moque_de_la_casse_et_des_accents()
    {
        Assert.Equal(QuestZoneOrder.RankOf("Île de Frigost"), QuestZoneOrder.RankOf("ile de frigost"));
        Assert.Equal(QuestZoneOrder.RankOf("Quêtes de Sufokia"), QuestZoneOrder.RankOf("SUFOKIA"));
    }
}
