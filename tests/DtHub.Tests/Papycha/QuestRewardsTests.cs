using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public class QuestRewardsTests
{
    [Fact]
    public void Le_Cra_191_sur_Gare_au_Kailleu_gagne_ce_que_le_jeu_affiche()
    {
        var step = new TouchStepReward(160, 0.5f, 0.80000001f, 0.80000001f, false, []);
        Assert.Equal(1_976_445, QuestRewards.Experience(step, 191, 150));
        Assert.Equal(11_512, QuestRewards.Kamas(step, 191));
    }

    [Fact]
    public void L_Eni_184_sur_Distribution_de_Marrons_gagne_ce_que_le_jeu_affiche()
        => Assert.Equal(5_541_305, QuestRewards.Experience(new(200, 1f, 1.1f, 0f, true, []), 184, 150));

    [Fact]
    public void Le_cas_plafonne_suit_la_regle_de_DOFUS_PC_en_attendant_le_jeu()
    {
        // Game shows 1 162 075 for the Enu 190 on "Ça rend aimable." (step 2039); open, see Task 8.
        var step = new TouchStepReward(60, 1f, 0.80000001f, 1f, false, []);
        Assert.Equal(581_040, QuestRewards.Experience(step, 190, 150));
        Assert.Equal(4_780, QuestRewards.Kamas(step, 190));
    }

    [Fact]
    public void Les_kamas_suivent_le_niveau_du_joueur_quand_l_etape_le_dit()
        => Assert.Equal(43_980, QuestRewards.Kamas(new(100, 1f, 1f, 1f, true, []), 200)); // 200²+4000−20

    [Fact]
    public void Seules_les_lignes_dont_les_bornes_contiennent_le_niveau_comptent()
    {
        TouchRewardRow Row(int min, int max, int item) => new(min, max, [new(item, 1)], 0, 0, 0, 0, 0, 0);
        var step = new TouchStepReward(100, 1f, 1f, 0f, false, [Row(-1, -1, 1), Row(1, 99, 2), Row(100, 200, 3)]);
        var total = QuestRewards.For([new TouchStep("", "", [], step)], 150, 0)!;
        Assert.Equal([1, 3], total.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void Une_quete_additionne_ses_etapes_et_regroupe_les_objets()
    {
        var a = new TouchStepReward(50, 1f, 1f, 1f, false, [new(-1, -1, [new(7, 2)], 1, 0, 0, 0, 0, 0)]);
        var b = new TouchStepReward(50, 1f, 1f, 1f, false, [new(-1, -1, [new(7, 3)], 0, 1, 0, 0, 0, 0)]);
        var total = QuestRewards.For([new("", "", [], a), new("", "", [], b)], 40, 0)!;
        Assert.Equal(2 * QuestRewards.Experience(a, 40, 0), total.Experience);
        Assert.Equal([new TouchItemReward(7, 5)], total.Items);
        Assert.Equal((1, 1), (total.Emotes, total.Spells));
    }

    [Fact]
    public void Une_etape_sans_recompense_connue_ne_donne_aucun_total()
        => Assert.Null(QuestRewards.For([new TouchStep("", "", [], null)], 100, 0));

    [Fact]
    public void Le_cas_plafonne_masque_la_ligne_tant_qu_il_n_est_pas_verifie()
    {
        var step = new TouchStepReward(60, 1f, 1f, 1f, false, []);
        Assert.Null(QuestRewards.For([new TouchStep("", "", [], step)], 91, 0));
        Assert.NotNull(QuestRewards.For([new TouchStep("", "", [], step)], 90, 0));
    }

    [Fact]
    public void Aucun_total_sans_niveau_ou_sans_etape()
    {
        Assert.Null(QuestRewards.For([], 100, 0));
        Assert.Null(QuestRewards.For([new TouchStep("", "", [], new(50, 1f, 1f, 1f, false, []))], 0, 0));
    }
}
