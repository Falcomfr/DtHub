using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class ZoomProfileTests
{
    [Fact]
    public void Le_reglage_d_origine_redonne_la_densite_d_origine()
    {
        // 1080 pixels at 240 DPI: exactly what the settings file
        // used to hardcode before zoom existed.
        Assert.Equal(240, ZoomProfile.DpiFor(1080, GameZoom.Normal));
    }

    [Fact]
    public void Une_fenetre_plus_petite_montre_la_meme_chose_en_plus_petit()
    {
        // This was the flaw: the resolution followed the window's
        // size, not the density, so a small window showed half as
        // much ground as a large one.
        var petite = ZoomProfile.DpiFor(720, GameZoom.Normal);
        var grande = ZoomProfile.DpiFor(1440, GameZoom.Normal);

        Assert.Equal(720 * 160 / 720, petite);
        Assert.Equal(2 * petite, grande);
    }

    [Fact]
    public void Les_paliers_se_suivent_du_plus_loin_au_plus_proche()
    {
        // Two neighboring tiers that gave the same density would
        // only serve to cause hesitation: each one must be visibly
        // distinct.
        var densites = Enum.GetValues<GameZoom>()
            .Select(z => ZoomProfile.DpiFor(1080, z))
            .ToList();

        Assert.Equal(densites.OrderBy(d => d), densites);
        Assert.Equal(densites.Count, densites.Distinct().Count());
    }

    [Theory]
    [InlineData(240)]
    [InlineData(7680)]
    public void La_densite_reste_dans_ce_qu_android_accepte(int height)
    {
        foreach (var zoom in Enum.GetValues<GameZoom>())
        {
            var dpi = ZoomProfile.DpiFor(height, zoom);

            Assert.InRange(dpi, 60, 800);
        }
    }

    [Fact]
    public void Une_definition_absurde_retombe_sur_la_densite_d_origine()
    {
        Assert.Equal(240, ZoomProfile.DpiFor(0, GameZoom.Normal));
        Assert.Equal(240, ZoomProfile.DpiFor(-10, GameZoom.Close));
    }

    [Fact]
    public void Les_deux_bouts_vont_aussi_loin_que_le_mecanisme_le_permet()
    {
        // Four tiers whose two ends are both useful are worth more
        // than five where two look alike: the closest one takes the
        // value that used to belong to a fifth tier.
        Assert.Equal(4, Enum.GetValues<GameZoom>().Length);
        Assert.Equal(1120, ZoomProfile.LayoutHeightFor(GameZoom.Widest));
        Assert.Equal(460, ZoomProfile.LayoutHeightFor(GameZoom.Close));

        // Normal remains the original reference: 1080 pixels at 240
        // DPI.
        Assert.Equal(720, ZoomProfile.LayoutHeightFor(GameZoom.Normal));
    }
}
