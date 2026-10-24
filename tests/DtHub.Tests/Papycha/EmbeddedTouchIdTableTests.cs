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
    public void La_table_embarquee_apparie_765_quetes_87_donjons_115_series_et_51_liens()
    {
        var table = Load();

        Assert.Equal(765, table.Quests.Count);
        Assert.Equal(87, table.Dungeons.Count);
        Assert.Equal(115, table.Series.Count);
        Assert.Equal(51, table.Links.Count);
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
}
