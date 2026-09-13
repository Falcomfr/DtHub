using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

public class WindowFrameTests
{
    /// <summary>
    /// Frame measured on the development device, at 150 percent.
    /// </summary>
    private static readonly WindowFrame Mesure = new(Left: 11, Top: 45, Width: 22, Height: 56);

    [Fact]
    public void La_zone_client_est_decalee_du_cadre()
    {
        // The reported bug: the window appeared at (-11, 268) then
        // jumped to (0, 313) two hundred milliseconds later. The gap
        // was exactly the border and the title bar.
        var client = Mesure.ClientOf(new ScreenRect(0, 313, 2522, 1462));

        Assert.Equal(new ScreenRect(11, 358, 2500, 1406), client);
    }

    [Fact]
    public void Le_cadre_ainsi_pose_rend_le_rectangle_exterieur_demande()
    {
        var voulu = new ScreenRect(186, 284, 2522, 1462);

        var client = Mesure.ClientOf(voulu);

        Assert.Equal(voulu.X, client.X - Mesure.Left);
        Assert.Equal(voulu.Y, client.Y - Mesure.Top);
        Assert.Equal(voulu.Width, client.Width + Mesure.Width);
        Assert.Equal(voulu.Height, client.Height + Mesure.Height);
    }

    [Fact]
    public void Une_fenetre_sans_cadre_garde_son_rectangle()
    {
        var voulu = new ScreenRect(0, 0, 1920, 1080);

        Assert.Equal(voulu, WindowFrame.None.ClientOf(voulu));
    }

    [Fact]
    public void Un_rectangle_plus_petit_que_son_cadre_ne_devient_jamais_vide()
    {
        var client = Mesure.ClientOf(new ScreenRect(0, 0, 10, 10));

        Assert.Equal(1, client.Width);
        Assert.Equal(1, client.Height);
    }
}
