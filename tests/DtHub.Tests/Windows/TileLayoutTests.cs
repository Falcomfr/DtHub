using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// Splitting the screen into two halves. Pulled out of the service
/// so that the tabbed frame can be docked like a game window: the
/// service only knows about sessions, and the frame is not one.
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
        // A portrait ratio would demand a height far greater than
        // the half is wide: it is bounded by the usable area.
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

    [Fact]
    public void Les_quatre_cases_se_lisent_de_gauche_a_droite_puis_de_haut_en_bas()
    {
        Assert.Equal(new ScreenRect(0, 0, 960, 520), TileLayout.Quarter(Work, 0, 0, (16, 48)));
        Assert.Equal(new ScreenRect(960, 0, 960, 520), TileLayout.Quarter(Work, 1, 0, (16, 48)));
        Assert.Equal(new ScreenRect(0, 520, 960, 520), TileLayout.Quarter(Work, 2, 0, (16, 48)));
        Assert.Equal(new ScreenRect(960, 520, 960, 520), TileLayout.Quarter(Work, 3, 0, (16, 48)));
    }

    [Fact]
    public void Au_dela_de_quatre_les_fenetres_s_empilent_sur_la_derniere_case()
    {
        Assert.Equal(
            TileLayout.Quarter(Work, 3, 16.0 / 9.0, (16, 48)),
            TileLayout.Quarter(Work, 5, 16.0 / 9.0, (16, 48)));
    }

    [Fact]
    public void Une_case_trop_basse_retrecit_la_largeur_au_rapport_de_l_image()
    {
        // A 16:9 game on a quarter of a 1920 x 1040 area wants more
        // height than the quarter has: the width gives way, and no
        // black band appears on the sides.
        var chrome = (Width: 16, Height: 48);
        var rect = TileLayout.Quarter(Work, 0, 16.0 / 9.0, chrome);

        var client = new ScreenRect(0, 0, rect.Width - chrome.Width, rect.Height - chrome.Height);

        Assert.Equal(520, rect.Height);
        Assert.True(rect.Width < 960);
        Assert.Equal(16.0 / 9.0, client.AspectRatio, 2);
    }

    [Fact]
    public void Les_quatre_fenetres_se_serrent_au_centre_de_l_ecran()
    {
        var cases = Enumerable.Range(0, 4)
            .Select(i => TileLayout.Quarter(Work, i, 16.0 / 9.0, (16, 48)))
            .ToList();

        Assert.Equal(960, cases[0].Right);
        Assert.Equal(960, cases[1].X);
        Assert.Equal(520, cases[0].Bottom);
        Assert.Equal(520, cases[2].Y);
    }
}
