using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

public class WindowLayoutCalculatorTests
{
    /// <summary>Écran 1920x1080 avec une barre des tâches de 40 pixels en bas.</summary>
    private static readonly MonitorInfo FullHd = new()
    {
        DeviceName = @"\\.\DISPLAY1",
        Bounds = new ScreenRect(0, 0, 1920, 1080),
        WorkArea = new ScreenRect(0, 0, 1920, 1040),
        IsPrimary = true,
    };

    private static readonly MonitorInfo Secondary = new()
    {
        DeviceName = @"\\.\DISPLAY2",
        Bounds = new ScreenRect(1920, 0, 2560, 1440),
        WorkArea = new ScreenRect(1920, 0, 2560, 1400),
    };

    /// <summary>Rapport d'un écran virtuel de téléphone, en portrait.</summary>
    private const double Portrait = 1080.0 / 1920.0;

    [Fact]
    public void La_taille_demandee_est_une_part_de_la_zone_utilisable()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, 70, Portrait, WindowAnchor.Center);

        Assert.Equal(728, rect.Height);
        Assert.True(rect.Height <= FullHd.WorkArea.Height);
    }

    [Fact]
    public void Le_rapport_d_affichage_de_la_source_est_conserve()
    {
        var portrait = WindowLayoutCalculator.Calculate(FullHd, 80, Portrait, WindowAnchor.Center);
        var landscape = WindowLayoutCalculator.Calculate(FullHd, 80, 16.0 / 9.0, WindowAnchor.Center);

        Assert.Equal(Portrait, portrait.AspectRatio, 2);
        Assert.Equal(16.0 / 9.0, landscape.AspectRatio, 2);
        Assert.True(landscape.Width > landscape.Height);
    }

    [Fact]
    public void Une_source_tres_large_est_bornee_par_la_largeur_disponible()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, 90, 21.0 / 9.0, WindowAnchor.Center);

        Assert.True(rect.Width <= (int)Math.Round(FullHd.WorkArea.Width * 0.9));
        Assert.True(rect.Height <= (int)Math.Round(FullHd.WorkArea.Height * 0.9));
    }

    [Fact]
    public void Une_taille_aberrante_est_ramenee_dans_les_bornes()
    {
        var tooSmall = WindowLayoutCalculator.Calculate(FullHd, 0, 0, WindowAnchor.Center);
        var tooLarge = WindowLayoutCalculator.Calculate(FullHd, 500, 0, WindowAnchor.Center);

        Assert.Equal((int)Math.Round(FullHd.WorkArea.Height * 0.2), tooSmall.Height);
        Assert.Equal(FullHd.WorkArea.Height, tooLarge.Height);
    }

    [Theory]
    [InlineData(WindowAnchor.TopLeft, 0, 0)]
    [InlineData(WindowAnchor.TopCenter, 460, 0)]
    [InlineData(WindowAnchor.TopRight, 920, 0)]
    [InlineData(WindowAnchor.MiddleLeft, 0, 220)]
    [InlineData(WindowAnchor.Center, 460, 220)]
    [InlineData(WindowAnchor.MiddleRight, 920, 220)]
    [InlineData(WindowAnchor.BottomLeft, 0, 440)]
    [InlineData(WindowAnchor.BottomCenter, 460, 440)]
    [InlineData(WindowAnchor.BottomRight, 920, 440)]
    public void Chaque_position_de_la_grille_colle_la_fenetre_au_bon_endroit(
        WindowAnchor anchor, int expectedX, int expectedY)
    {
        var area = new ScreenRect(0, 0, 1920, 1080);

        var rect = WindowLayoutCalculator.Place(area, 1000, 640, anchor);

        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
    }

    [Fact]
    public void Une_fenetre_ancree_reste_dans_la_zone_utilisable()
    {
        foreach (var anchor in WindowAnchors.All)
        {
            var rect = WindowLayoutCalculator.Calculate(FullHd, 90, Portrait, anchor);

            Assert.True(rect.X >= FullHd.WorkArea.X, $"{anchor} déborde à gauche");
            Assert.True(rect.Y >= FullHd.WorkArea.Y, $"{anchor} déborde en haut");
            Assert.True(rect.Right <= FullHd.WorkArea.Right, $"{anchor} déborde à droite");
            Assert.True(rect.Bottom <= FullHd.WorkArea.Bottom, $"{anchor} déborde en bas");
        }
    }

    [Fact]
    public void La_barre_des_taches_n_est_jamais_recouverte()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, 100, 0, WindowAnchor.BottomCenter);

        Assert.Equal(FullHd.WorkArea.Bottom, rect.Bottom);
        Assert.True(rect.Bottom < FullHd.Bounds.Bottom);
    }

    [Fact]
    public void Une_fenetre_plus_grande_que_la_zone_est_ramenee_a_sa_taille()
    {
        var rect = WindowLayoutCalculator.Place(new ScreenRect(0, 0, 800, 600), 5000, 5000, WindowAnchor.TopLeft);

        Assert.Equal(new ScreenRect(0, 0, 800, 600), rect);
    }

    [Fact]
    public void Le_configurateur_se_pose_a_l_oppose_du_bloc_de_jeu()
    {
        Assert.Equal(WindowAnchor.TopRight, WindowAnchors.Opposite(WindowAnchor.MiddleLeft));
        Assert.Equal(WindowAnchor.TopLeft, WindowAnchors.Opposite(WindowAnchor.MiddleRight));
        Assert.Equal(WindowAnchor.TopLeft, WindowAnchors.Opposite(WindowAnchor.TopRight));
    }

    [Fact]
    public void Toutes_les_positions_ont_un_libelle()
    {
        foreach (var anchor in WindowAnchors.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(WindowAnchors.Describe(anchor)));
        }

        Assert.Equal(9, WindowAnchors.All.Count);
    }

    [Fact]
    public void L_ecran_prefere_est_respecte_puis_retombe_sur_le_principal_s_il_disparait()
    {
        var monitors = new[] { FullHd, Secondary };

        Assert.Equal(Secondary, WindowLayoutCalculator.ChooseMonitor(monitors, @"\\.\DISPLAY2"));
        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, @"\\.\ECRAN_DEBRANCHE"));
        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, (string?)null));
    }

    [Fact]
    public void L_ecran_est_aussi_choisi_d_apres_une_position()
    {
        var monitors = new[] { FullHd, Secondary };

        Assert.Equal(Secondary, WindowLayoutCalculator.ChooseMonitor(monitors, 2500, 700));
        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, -9000, -9000));
    }

    [Fact]
    public void Sans_aucun_ecran_le_calcul_echoue_explicitement()
    {
        Assert.Throws<InvalidOperationException>(
            () => WindowLayoutCalculator.ChooseMonitor([], (string?)null));
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(4, 3, 0)]
    [InlineData(7, 6, 0)]
    public void Le_parcours_avant_est_circulaire(int count, int current, int expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.NextIndex(count, current));
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(4, 0, 3)]
    [InlineData(4, 2, 1)]
    public void Le_parcours_arriere_est_circulaire(int count, int current, int expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.PreviousIndex(count, current));
    }

    [Fact]
    public void Un_parcours_sans_instance_ne_designe_rien()
    {
        Assert.Equal(-1, WindowLayoutCalculator.NextIndex(0, 0));
        Assert.Equal(-1, WindowLayoutCalculator.PreviousIndex(0, 0));
    }

    [Fact]
    public void Toutes_les_fenetres_recoivent_exactement_le_meme_rectangle()
    {
        // C'est ce qui garantit la superposition parfaite.
        var rects = Enumerable.Range(0, 5)
            .Select(_ => WindowLayoutCalculator.Calculate(FullHd, 70, Portrait, WindowAnchor.MiddleLeft))
            .Distinct()
            .ToList();

        Assert.Single(rects);
    }
}
