using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// Sort d'une géométrie mémorisée quand la configuration d'écrans a changé
/// entre deux sessions. Calcul pur, sans fenêtre ni téléphone.
/// </summary>
public sealed class WindowGeometryRestoreTests
{
    private static MonitorInfo Monitor(
        string name, int x, int y, int width, int height, int taskbar = 0, bool primary = true) => new()
        {
            DeviceName = name,
            Bounds = new ScreenRect(x, y, width, height),
            WorkArea = new ScreenRect(x, y, width, height - taskbar),
            IsPrimary = primary,
        };

    [Fact]
    public void Un_ecran_identique_rend_le_rectangle_tel_quel()
    {
        var screen = Monitor(@"\\.\DISPLAY1", 0, 0, 3840, 2160, taskbar: 72);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(300, 200, 900, 900), screen.DeviceName, screen.Bounds, [screen]);

        Assert.Equal(new ScreenRect(300, 200, 900, 900), restored);
    }

    [Fact]
    public void Un_changement_de_definition_remet_le_rectangle_a_l_echelle()
    {
        var now = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080, taskbar: 40);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(0, 0, 1920, 1080), now.DeviceName, new ScreenRect(0, 0, 3840, 2160), [now]);

        Assert.Equal(new ScreenRect(0, 0, 960, 540), restored);
    }

    [Fact]
    public void Un_rectangle_remis_a_l_echelle_reste_dans_la_zone_utilisable()
    {
        var now = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080, taskbar: 40);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(0, 0, 3840, 2160), now.DeviceName, new ScreenRect(0, 0, 3840, 2160), [now]);

        Assert.NotNull(restored);
        Assert.True(restored!.Value.Bottom <= now.WorkArea.Bottom);
    }

    [Fact]
    public void Un_ecran_debranche_fait_retomber_sur_l_ancrage()
    {
        // La fenêtre était sur le second écran, qui n'est plus là. Rien de son
        // rectangle ne tombe sur l'écran restant.
        var remaining = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(4000, 300, 800, 600), @"\\.\DISPLAY2", new ScreenRect(3840, 0, 1920, 1080),
            [remaining]);

        Assert.Null(restored);
    }

    [Fact]
    public void Un_ecran_renumerote_est_reconnu_par_le_recouvrement()
    {
        // Le nom ne correspond plus, mais la fenêtre tombe entièrement sur cet
        // écran : elle y reste.
        var screen = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080, taskbar: 40);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(100, 100, 800, 600), @"\\.\DISPLAY2", new ScreenRect(0, 0, 1920, 1080), [screen]);

        Assert.Equal(new ScreenRect(100, 100, 800, 600), restored);
    }

    [Fact]
    public void Un_rectangle_degenere_est_refuse()
    {
        var screen = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080);

        Assert.Null(WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(0, 0, 0, 0), screen.DeviceName, screen.Bounds, [screen]));
    }

    [Fact]
    public void Une_fenetre_reduite_par_Windows_est_refusee()
    {
        // Une fenêtre iconifiée rend un rectangle en (-32000, -32000). Le
        // restaurer placerait la fenêtre hors de tout écran.
        var screen = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080);

        Assert.Null(WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(-32000, -32000, 160, 28), screen.DeviceName, screen.Bounds, [screen]));
    }

    [Fact]
    public void Un_rectangle_a_cheval_reste_sur_l_ecran_qui_le_porte_le_plus()
    {
        var left = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080, taskbar: 40);
        var right = Monitor(@"\\.\DISPLAY9", 1920, 0, 1920, 1080, taskbar: 40, primary: false);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(1800, 100, 400, 300), @"\\.\DISPARU", new ScreenRect(0, 0, 1920, 1080),
            [left, right]);

        Assert.NotNull(restored);
        Assert.True(restored!.Value.X >= 1920);
    }
}
