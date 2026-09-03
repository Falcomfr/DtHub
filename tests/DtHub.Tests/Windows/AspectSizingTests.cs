using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// La forme d'un cadre qu'on étire. Les chiffres sont ceux d'une vraie session :
/// afficheur 1920 x 1080, donc un rapport de 16:9, et un châssis de 22 pixels de
/// large sur 84 de haut, bordures, barre de titre et barre d'onglets réunies.
/// </summary>
public class AspectSizingTests
{
    private const double SeizeNeuvieme = 1920.0 / 1080.0;

    private static readonly (int Width, int Height) Chassis = (22, 84);

    [Fact]
    public void Tirer_un_cote_commande_la_hauteur_et_garde_le_haut()
    {
        // On élargit à 1600 : la hauteur doit suivre, et le bord du haut rester.
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
        // C'est le cas qui ne marchait pas : le bord du bas était inerte, la
        // hauteur étant aussitôt recalculée depuis la largeur.
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

            // Un pixel d'arrondi sur mille : c'est ce qui reste après le passage
            // en entiers, et le recentrage de la fenêtre logée l'absorbe.
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
        // Un cadre vide n'a pas d'image à loger : il s'étire comme il veut.
        var propose = new ScreenRect(10, 20, 300, 400);

        Assert.Equal(propose, AspectSizing.Constrain(propose, AspectSizing.Right, 0, Chassis, 480));
    }
}
