using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// The shape of a frame being stretched. The numbers are those of a
/// real session: a 1920 x 1080 display, so a 16:9 ratio, and a
/// chassis 22 pixels wide by 84 tall, borders, title bar and tab bar
/// combined.
/// </summary>
public class AspectSizingTests
{
    private const double SeizeNeuvieme = 1920.0 / 1080.0;

    private static readonly (int Width, int Height) Chassis = (22, 84);

    [Fact]
    public void Tirer_un_cote_commande_la_hauteur_et_garde_le_haut()
    {
        // Widening to 1600: the height must follow, and the top
        // edge must stay put.
        var pris = AspectSizing.Constrain(
            new ScreenRect(200, 300, 1600, 900),
            AspectSizing.Right,
            SeizeNeuvieme,
            Chassis,
            480);

        Assert.Equal(200, pris.X);
        Assert.Equal(300, pris.Y);
        Assert.Equal(1600, pris.Width);
        Assert.Equal((int)Math.Round((1600 - 22) / SeizeNeuvieme) + 84, pris.Height);
    }

    [Fact]
    public void Tirer_le_bas_commande_la_largeur_et_garde_la_gauche()
    {
        // This is the case that used to be broken: the bottom edge
        // was inert, the height being immediately recalculated from
        // the width.
        var pris = AspectSizing.Constrain(
            new ScreenRect(200, 300, 2100, 700),
            AspectSizing.Bottom,
            SeizeNeuvieme,
            Chassis,
            480);

        Assert.Equal(200, pris.X);
        Assert.Equal(700, pris.Height);
        Assert.Equal((int)Math.Round((700 - 84) * SeizeNeuvieme) + 22, pris.Width);
    }

    [Fact]
    public void Tirer_un_coin_du_haut_garde_le_bas()
    {
        var propose = new ScreenRect(200, 300, 1200, 900);

        var pris = AspectSizing.Constrain(
            propose, AspectSizing.TopLeft, SeizeNeuvieme, Chassis, 480);

        Assert.Equal(propose.Bottom, pris.Bottom);
        Assert.Equal(1200, pris.Width);
    }

    [Fact]
    public void Tirer_un_coin_du_bas_garde_le_haut()
    {
        var pris = AspectSizing.Constrain(
            new ScreenRect(200, 300, 1200, 900),
            AspectSizing.BottomRight,
            SeizeNeuvieme,
            Chassis,
            480);

        Assert.Equal(300, pris.Y);
        Assert.Equal(1200, pris.Width);
    }

    [Fact]
    public void La_zone_de_jeu_garde_le_rapport_a_un_pixel_pres()
    {
        foreach (var bord in new[]
                 {
                     AspectSizing.Left, AspectSizing.Right, AspectSizing.Top,
                     AspectSizing.Bottom, AspectSizing.TopLeft, AspectSizing.TopRight,
                     AspectSizing.BottomLeft, AspectSizing.BottomRight,
                 })
        {
            var pris = AspectSizing.Constrain(
                new ScreenRect(0, 0, 1337, 911), bord, SeizeNeuvieme, Chassis, 480);

            var jeu = (double)(pris.Width - Chassis.Width) / (pris.Height - Chassis.Height);

            // One pixel of rounding out of a thousand: this is what
            // is left after the conversion to integers, and the
            // recentring of the docked window absorbs it.
            Assert.InRange(jeu, SeizeNeuvieme - 0.005, SeizeNeuvieme + 0.005);
        }
    }

    [Fact]
    public void La_largeur_minimale_est_tenue()
    {
        var pris = AspectSizing.Constrain(
            new ScreenRect(0, 0, 120, 200), AspectSizing.Left, SeizeNeuvieme, Chassis, 480);

        Assert.Equal(480, pris.Width);
    }

    [Fact]
    public void Sans_rapport_connu_rien_n_est_touche()
    {
        // An empty frame has no image to fit: it stretches however
        // it likes.
        var propose = new ScreenRect(10, 20, 300, 400);

        Assert.Equal(propose, AspectSizing.Constrain(propose, AspectSizing.Right, 0, Chassis, 480));
    }
}
