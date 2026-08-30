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
    public void Les_paliers_se_suivent_du_plus_loin_au_plus_proche()
    {
        // Deux paliers voisins qui donneraient la même densité ne serviraient
        // qu'à faire hésiter : chacun doit se voir.
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
        // Quatre paliers dont les deux extrémités sont utiles valent mieux que
        // cinq dont deux se ressemblent : le plus proche prend la valeur qui
        // était celle d'un cinquième palier.
        Assert.Equal(4, Enum.GetValues<GameZoom>().Length);
        Assert.Equal(1120, ZoomProfile.LayoutHeightFor(GameZoom.Widest));
        Assert.Equal(460, ZoomProfile.LayoutHeightFor(GameZoom.Close));

        // La normale reste la référence d'origine : 1080 pixels à 240 ppp.
        Assert.Equal(720, ZoomProfile.LayoutHeightFor(GameZoom.Normal));
    }
}
