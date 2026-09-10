using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// Le partage de l'écran en deux moitiés. Sorti du service pour que le cadre à
/// onglets puisse être rangé comme une fenêtre de jeu : le service ne connaît
/// que des sessions, et le cadre n'en est pas une.
/// </summary>
public class TileLayoutTests
{
    private static readonly ScreenRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void La_reference_prend_la_droite_et_le_reste_la_gauche()
    {
        var droite = TileLayout.Half(Work, onRight: true, 16.0 / 9.0, (16, 48));
        var gauche = TileLayout.Half(Work, onRight: false, 16.0 / 9.0, (16, 48));

        Assert.Equal(960, droite.X);
        Assert.Equal(0, gauche.X);
        Assert.Equal(960, droite.Width);
        Assert.Equal(gauche.Width, droite.Width);
    }

    [Fact]
    public void Le_rapport_s_applique_a_la_zone_client()
    {
        var chrome = (Width: 16, Height: 48);
        var rect = TileLayout.Half(Work, onRight: false, 16.0 / 9.0, chrome);

        var client = new ScreenRect(0, 0, rect.Width - chrome.Width, rect.Height - chrome.Height);

        Assert.Equal(16.0 / 9.0, client.AspectRatio, 2);
    }

    [Fact]
    public void La_moitie_est_centree_verticalement()
    {
        var rect = TileLayout.Half(Work, onRight: false, 16.0 / 9.0, (16, 48));

        Assert.Equal((Work.Height - rect.Height) / 2, rect.Y);
    }

    [Fact]
    public void Une_source_tres_haute_ne_depasse_pas_la_zone()
    {
        // Un rapport portrait demanderait une hauteur bien plus grande que la
        // moitié n'est large : elle est bornée par la zone utile.
        var rect = TileLayout.Half(Work, onRight: false, 9.0 / 16.0, (16, 48));

        Assert.Equal(Work.Height, rect.Height);
        Assert.Equal(Work.Y, rect.Y);
    }

    [Fact]
    public void Sans_rapport_connu_la_moitie_prend_toute_la_hauteur()
    {
        var rect = TileLayout.Half(Work, onRight: true, 0, (16, 48));

        Assert.Equal(Work.Height, rect.Height);
    }

    [Fact]
    public void Le_partage_suit_l_ecran_et_pas_l_origine()
    {
        var second = new ScreenRect(1920, 100, 2560, 1300);

        var droite = TileLayout.Half(second, onRight: true, 16.0 / 9.0, (0, 0));
        var gauche = TileLayout.Half(second, onRight: false, 16.0 / 9.0, (0, 0));

        Assert.Equal(1920, gauche.X);
        Assert.Equal(1920 + 1280, droite.X);
    }
}
