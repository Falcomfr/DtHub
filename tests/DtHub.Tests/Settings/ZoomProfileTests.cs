using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class ZoomProfileTests
{
    [Fact]
    public void Le_reglage_d_origine_redonne_la_densite_d_origine()
    {
        // 1080 pixels à 240 ppp : exactement ce que le fichier de réglages
        // portait en dur avant que le zoom n'existe.
        Assert.Equal(240, ZoomProfile.DpiFor(1080, GameZoom.Normal));
    }

    [Fact]
    public void Une_fenetre_plus_petite_montre_la_meme_chose_en_plus_petit()
    {
        // C'était le défaut : la définition suivait la taille de la fenêtre,
        // pas la densité, si bien qu'une petite fenêtre montrait deux fois
        // moins de terrain qu'une grande.
        var petite = ZoomProfile.DpiFor(720, GameZoom.Normal);
        var grande = ZoomProfile.DpiFor(1440, GameZoom.Normal);

        Assert.Equal(720 * 160 / 720, petite);
        Assert.Equal(2 * petite, grande);
    }

    [Fact]
    public void Plus_c_est_eloigne_plus_la_densite_baisse()
    {
        var eloignee = ZoomProfile.DpiFor(1080, GameZoom.Wide);
        var normale = ZoomProfile.DpiFor(1080, GameZoom.Normal);
        var proche = ZoomProfile.DpiFor(1080, GameZoom.Close);

        Assert.True(eloignee < normale);
        Assert.True(normale < proche);
    }

    [Theory]
    [InlineData(240)]
    [InlineData(7680)]
    public void La_densite_reste_dans_ce_qu_android_accepte(int height)
    {
        foreach (var zoom in Enum.GetValues<GameZoom>())
        {
            var dpi = ZoomProfile.DpiFor(height, zoom);

            Assert.InRange(dpi, 60, 640);
        }
    }

    [Fact]
    public void Une_definition_absurde_retombe_sur_la_densite_d_origine()
    {
        Assert.Equal(240, ZoomProfile.DpiFor(0, GameZoom.Normal));
        Assert.Equal(240, ZoomProfile.DpiFor(-10, GameZoom.Close));
    }
}
