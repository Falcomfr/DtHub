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

    /// <summary>Second écran, placé à droite du principal.</summary>
    private static readonly MonitorInfo Secondary = new()
    {
        DeviceName = @"\\.\DISPLAY2",
        Bounds = new ScreenRect(1920, 0, 2560, 1440),
        WorkArea = new ScreenRect(1920, 0, 2560, 1400),
    };

    /// <summary>Rapport d'un écran virtuel de téléphone, en portrait.</summary>
    private const double Portrait = 1080.0 / 1920.0;

    private static readonly WindowSizePresets Presets = WindowSizePresets.Default;

    [Fact]
    public void Les_quatre_pourcentages_par_defaut_sont_ceux_annonces()
    {
        Assert.Equal([60, 70, 80, 90], WindowSizePresets.Default.Percentages);
        Assert.Equal(5, WindowSizePresets.Default.Count);
        Assert.True(WindowSizePresets.Default.IsFullscreen(4));
    }

    [Fact]
    public void Une_taille_en_pourcentage_tient_dans_la_zone_utilisable()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 0, Portrait);

        // 60 % de 1040 pixels de haut, la largeur suit le rapport portrait.
        Assert.Equal(624, rect.Height);
        Assert.Equal(351, rect.Width);
        Assert.True(rect.Height <= FullHd.WorkArea.Height);
    }

    [Fact]
    public void Une_fenetre_est_centree_dans_la_zone_utilisable()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 2, Portrait);

        Assert.Equal(FullHd.WorkArea.CenterX, rect.CenterX);
        Assert.Equal(FullHd.WorkArea.CenterY, rect.CenterY);
    }

    [Fact]
    public void La_barre_des_taches_n_est_jamais_recouverte_hors_plein_ecran()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 3, Portrait);

        Assert.True(rect.Bottom <= FullHd.WorkArea.Bottom, $"Bas de fenêtre : {rect.Bottom}");
    }

    [Fact]
    public void Le_plein_ecran_couvre_l_ecran_entier_barre_des_taches_comprise()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, Presets.FullscreenIndex, Portrait);

        Assert.Equal(FullHd.Bounds, rect);
    }

    [Fact]
    public void Le_rapport_d_affichage_de_la_source_est_conserve()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 3, Portrait);

        Assert.Equal(Portrait, rect.AspectRatio, 2);
    }

    [Fact]
    public void Une_source_en_paysage_donne_une_fenetre_en_paysage()
    {
        const double landscape = 16.0 / 9.0;

        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 1, landscape);

        Assert.Equal(landscape, rect.AspectRatio, 2);
        Assert.True(rect.Width > rect.Height);
    }

    [Fact]
    public void Une_source_paysage_large_est_bornee_par_la_largeur_disponible()
    {
        // À 90 %, une source 21:9 déborderait en largeur si seule la hauteur
        // était prise en compte.
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 3, 21.0 / 9.0);

        Assert.True(rect.Width <= (int)Math.Round(FullHd.WorkArea.Width * 0.9));
        Assert.True(rect.Height <= (int)Math.Round(FullHd.WorkArea.Height * 0.9));
    }

    [Fact]
    public void Sans_rapport_impose_la_fenetre_remplit_le_pourcentage_demande()
    {
        var rect = WindowLayoutCalculator.Calculate(FullHd, Presets, 3, 0);

        Assert.Equal(1728, rect.Width);
        Assert.Equal(936, rect.Height);
    }

    [Fact]
    public void Les_tailles_croissent_avec_le_pourcentage()
    {
        var heights = Enumerable.Range(0, 4)
            .Select(i => WindowLayoutCalculator.Calculate(FullHd, Presets, i, Portrait).Height)
            .ToList();

        Assert.Equal(heights.OrderBy(h => h), heights);
        Assert.Equal(4, heights.Distinct().Count());
    }

    [Fact]
    public void Un_indice_de_taille_hors_bornes_retombe_sur_la_valeur_la_plus_proche()
    {
        var tooLow = WindowLayoutCalculator.Calculate(FullHd, Presets, -5, Portrait);
        var first = WindowLayoutCalculator.Calculate(FullHd, Presets, 0, Portrait);

        Assert.Equal(first, tooLow);
    }

    [Fact]
    public void Les_pourcentages_sont_personnalisables()
    {
        var custom = new WindowSizePresets { Percentages = [50, 75, 100] };

        var rect = WindowLayoutCalculator.Calculate(FullHd, custom, 0, Portrait);

        Assert.Equal(520, rect.Height);
        Assert.Equal(4, custom.Count);
        Assert.True(custom.IsFullscreen(3));
    }

    [Fact]
    public void Des_pourcentages_aberrants_sont_corriges_plutot_que_refuses()
    {
        var messy = new WindowSizePresets { Percentages = [500, 0, 80, 80, -3] };

        var cleaned = messy.Sanitized();

        Assert.Equal([20, 80, 100], cleaned.Percentages);
    }

    [Fact]
    public void Une_liste_de_pourcentages_vide_retombe_sur_les_valeurs_par_defaut()
    {
        Assert.Equal(
            WindowSizePresets.Default.Percentages,
            new WindowSizePresets { Percentages = [] }.Sanitized().Percentages);
    }

    [Fact]
    public void Un_ecran_sans_zone_utilisable_declaree_utilise_ses_dimensions_completes()
    {
        var monitor = FullHd with { WorkArea = new ScreenRect(0, 0, 0, 0) };

        var rect = WindowLayoutCalculator.Calculate(monitor, Presets, 3, 0);

        Assert.Equal(972, rect.Height);
    }

    [Fact]
    public void L_ecran_est_choisi_d_apres_la_position_de_la_fenetre()
    {
        var monitors = new[] { FullHd, Secondary };

        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, 100, 100));
        Assert.Equal(Secondary, WindowLayoutCalculator.ChooseMonitor(monitors, 2500, 700));
    }

    [Fact]
    public void Une_position_hors_de_tout_ecran_retombe_sur_l_ecran_principal()
    {
        var monitors = new[] { FullHd, Secondary };

        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, -9000, -9000));
    }

    [Fact]
    public void L_ecran_prefere_est_respecte_puis_retombe_sur_le_principal_s_il_disparait()
    {
        var monitors = new[] { FullHd, Secondary };

        Assert.Equal(Secondary, WindowLayoutCalculator.ChooseMonitor(monitors, @"\\.\DISPLAY2"));
        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, @"\\.\ECRAN_DEBRANCHE"));
        Assert.Equal(FullHd, WindowLayoutCalculator.ChooseMonitor(monitors, null));
    }

    [Fact]
    public void Sans_aucun_ecran_le_calcul_echoue_explicitement()
    {
        Assert.Throws<InvalidOperationException>(
            () => WindowLayoutCalculator.ChooseMonitor([], 0, 0));
    }

    [Theory]
    [InlineData(2, 0, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(4, 2, 3)]
    [InlineData(4, 3, 0)]
    [InlineData(7, 6, 0)]
    public void Le_parcours_des_sessions_est_circulaire_quel_que_soit_leur_nombre(
        int count, int current, int expected)
    {
        Assert.Equal(expected, WindowLayoutCalculator.NextIndex(count, current));
    }

    [Fact]
    public void Un_parcours_sans_session_ne_designe_rien()
    {
        Assert.Equal(-1, WindowLayoutCalculator.NextIndex(0, 0));
    }

    [Fact]
    public void Une_session_courante_disparue_ramene_a_la_premiere()
    {
        Assert.Equal(0, WindowLayoutCalculator.NextIndex(3, -1));
        Assert.Equal(0, WindowLayoutCalculator.NextIndex(3, 99));
    }

    [Fact]
    public void Toutes_les_fenetres_empilees_recoivent_exactement_le_meme_rectangle()
    {
        // Le mode STACK repose entièrement là-dessus : un seul calcul, appliqué
        // à toutes les sessions.
        var rects = Enumerable.Range(0, 5)
            .Select(_ => WindowLayoutCalculator.Calculate(FullHd, Presets, 2, Portrait))
            .Distinct()
            .ToList();

        Assert.Single(rects);
    }
}
