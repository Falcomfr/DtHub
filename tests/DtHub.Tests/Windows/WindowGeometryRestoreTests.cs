using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

/// <summary>
/// Works out a remembered geometry when the screen configuration
/// has changed between two sessions. Pure calculation, with no
/// window and no phone.
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
        // The window was on the second screen, which is no longer
        // there. None of its rectangle falls on the remaining
        // screen.
        var remaining = Monitor(@"\\.\DISPLAY1", 0, 0, 1920, 1080);

        var restored = WindowLayoutCalculator.RestoreRemembered(
            new ScreenRect(4000, 300, 800, 600), @"\\.\DISPLAY2", new ScreenRect(3840, 0, 1920, 1080),
            [remaining]);

        Assert.Null(restored);
    }

    [Fact]
    public void Un_ecran_renumerote_est_reconnu_par_le_recouvrement()
    {
        // The name no longer matches, but the window falls entirely
        // on this screen: it stays there.
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
        // A minimized window returns a rectangle at (-32000,
        // -32000). Restoring it would place the window off every
        // screen.
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
