using DtHub.Infrastructure.Papycha;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Papycha;

public class EmbeddedTouchIdTableTests
{
    private static Core.Papycha.TouchIdTable Load() =>
        new EmbeddedTouchIdTable(NullLogger<EmbeddedTouchIdTable>.Instance).Load();

    /// <summary>
    /// 51 links, not the 50 of the first measure: that one only followed
    /// quests paired by name, and the hand-paired "Kapla ! | Survivant !"
    /// page (7044) also carries a game achievement.
    /// </summary>
    [Fact]
    public void La_table_embarquee_apparie_773_quetes_87_donjons_115_series_et_51_liens()
    {
        var table = Load();

        Assert.Equal(773, table.Quests.Count);
        Assert.Equal(87, table.Dungeons.Count);
        Assert.Equal(115, table.Series.Count);
        Assert.Equal(51, table.Links.Count);
    }

    private static readonly int[] ShadowSeries = [37852, 37899, 37929, 37963, 38001];

    /// <summary>Papycha gives "Le Fil d'Adrian" no prerequisite: only the game's order puts it after "Dédale au pluriel".</summary>
    [Fact]
    public void A_l_ombre_depuis_trop_longtemps_suit_l_ordre_du_jeu()
    {
        var ranks = Load().Ranks;

        Assert.Equal([1, 2, 3, 4, 5], ShadowSeries.Select(id => ranks[id]));
        Assert.Equal([37899], Load().Needs[37929]);
    }

    /// <summary>"L'Ascension." (681) is the achievement's, "L'Ascension" (1501) the repeatable one that needs it.</summary>
    [Fact]
    public void Les_deux_pages_L_Ascension_ont_chacune_leur_quete()
    {
        var table = Load();

        Assert.Equal([681], table.Quests[4272]);
        Assert.Equal([1501], table.Quests[23741]);
        Assert.DoesNotContain(23741, table.Links.Keys);
    }

    [Fact]
    public void La_page_Kapla_Survivant_donne_deux_quetes()
    {
        Assert.Equal([934, 935], Load().Quests[7044]);
    }

    [Fact]
    public void Cavernes_Nourricieres_n_est_pas_appariee() =>
        Assert.DoesNotContain(8219, Load().Dungeons.Keys);

    [Fact]
    public void Un_donjon_de_boss_porte_le_monstre()
    {
        var moon = Load().Dungeons[8232];

        Assert.Empty(moon.Dungeons);
        Assert.Equal(226, moon.Monster);
    }

    /// <summary>
    /// The game's "one quest or another" objectives (D219, 2026-10-24): ten pairs in "Agriculture ou alchimie", six in
    /// Allister's achievement, the three alignment prologues of "L'âme de glace" and the fifteen devotions.
    /// </summary>
    [Fact]
    public void La_table_embarquee_porte_les_18_groupes_d_alternatives_du_jeu()
    {
        var groups = Load().Alternatives;

        Assert.Equal(18, groups.Length);
        Assert.Equal(16, groups.Count(g => g.Length == 2));
        Assert.Contains(groups, g => g.SequenceEqual([11038, 11056]));
        Assert.Contains(groups, g => g.SequenceEqual([1551, 36740, 36750]));
        Assert.Contains(groups, g => g.Length == 15);
    }

    /// <summary>86 of the 115 series' achievements ask for paired quests only: finishing their quests finishes them.</summary>
    [Fact]
    public void La_table_embarquee_porte_les_86_succes_faits_de_quetes()
    {
        var whole = Load().Whole;

        Assert.Equal(86, whole.Count);
        Assert.Equal([[37852], [37899], [37929], [37963], [38001]], whole[1595]);
    }
}
