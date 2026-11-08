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

    // Read in the Cra 191's journal on 2026-10-23, Bonus Pack +150 %: level capped at 1.5 x the quest's.
    [Theory]
    [InlineData(60, 1f, 0.80000001f, 581_037)]    // Ça rend aimable.
    [InlineData(50, 1f, 0.80000001f, 388_122)]    // La chasse de Lily
    [InlineData(85, 0.5f, 1.1f, 893_715)]         // Redolphe est un vilain grincheux: cap 127.5 counts as 127
    [InlineData(180, 2f, 1.2f, 12_746_430)]       // Mission Solution
    [InlineData(170, 0.5f, 0.80000001f, 2_046_765)] // Le ver de trop
    public void Le_Cra_191_gagne_ce_que_le_jeu_affiche(int opt, float duration, float xpRatio, long xp)
        => Assert.Equal(xp, QuestRewards.Experience(new(opt, duration, xpRatio, 1f, false, []), 191, 150));

    [Fact]
    public void Ca_rend_aimable_donne_les_kamas_du_jeu()
        => Assert.Equal(4_780, QuestRewards.Kamas(new(60, 1f, 0.80000001f, 1f, false, []), 191));

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
    public void Un_personnage_bien_au_dessus_de_la_quete_a_sa_ligne()
        => Assert.NotNull(QuestRewards.For([new TouchStep("", "", [], new(60, 1f, 1f, 1f, false, []))], 191, 0));

    [Fact]
    public void Aucun_total_sans_niveau_ou_sans_etape()
    {
        Assert.Null(QuestRewards.For([], 100, 0));
        Assert.Null(QuestRewards.For([new TouchStep("", "", [], new(50, 1f, 1f, 1f, false, []))], 0, 0));
    }

    [Fact]
    public void Un_succes_donne_l_xp_au_personnage_et_ses_kamas_au_compte()
    {
        static TouchAchievementReward Row(float xp, float kamas, bool account, int min = -1, TouchItemReward[]? items = null) =>
            new(new TouchStepReward(200, 1f, xp, kamas, false, [new TouchRewardRow(min, -1, items ?? [], 0, 0, 1, 0, 0, 0)]), account);

        var gain = QuestRewards.AchievementFor(
            [Row(0f, 0.8f, true), Row(2f, 0f, false), Row(0f, 0f, true, items: [new(20849, 1)]), Row(5f, 0f, false, min: 201)],
            200, 0);

        Assert.Equal(QuestRewards.Experience(new(200, 1f, 2f, 0f, false, []), 200, 0), gain.Character.Experience);
        Assert.Equal(0, gain.Character.Kamas);
        Assert.Equal(QuestRewards.Kamas(new(200, 1f, 0f, 0.8f, false, []), 200), gain.AccountKamas);
        Assert.Equal([new TouchItemReward(20849, 1)], gain.Character.Items);
        // The row for level 201 and up is out at 200: its title with it.
        Assert.Equal(3, gain.Character.Titles);
    }

    [Fact]
    public void Halte_au_peage_pour_Falcom_193_donne_ce_que_le_jeu_affiche()
    {
        // Read in the game on 2026-11-08: 11 260 715 XP (Pack bonus) and 39 880 kamas, six items.
        TouchItemReward[] items = [new(11939, 1), new(11929, 1), new(11885, 1), new(11337, 1), new(11332, 1), new(11313, 1)];
        TouchAchievementReward[] rows =
        [
            new(new(190, 1f, 0f, 0f, false, [new(-1, -1, items, 0, 0, 0, 0, 0, 0)]), true),
            new(new(190, 1f, 0f, 1f, false, [new(-1, -1, [], 0, 0, 0, 0, 0, 0)]), true),
            new(new(190, 1f, 2f, 0f, false, [new(-1, -1, [], 0, 0, 0, 0, 0, 0)]), false),
        ];

        var gain = QuestRewards.AchievementFor(rows, 193, 150);

        Assert.Equal(11_260_715, gain.Character.Experience);
        Assert.Equal(39_880, gain.AccountKamas);
        Assert.Equal(6, gain.Character.Items.Count);
    }
}
