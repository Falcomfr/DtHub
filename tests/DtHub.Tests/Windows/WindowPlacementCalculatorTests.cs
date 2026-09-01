using DtHub.Core.Windows;

namespace DtHub.Tests.Windows;

public sealed class WindowPlacementCalculatorTests
{
    private static readonly ScreenRect Principal = new(0, 0, 2560, 1400);
    private static readonly ScreenRect Second = new(3840, 471, 1920, 1040);

    private static WindowPlacement At(int x, int y, int width = 520, int height = 760) =>
        new() { Left = x, Top = y, Right = x + width, Bottom = y + height };

    [Fact]
    public void Une_fenetre_posee_sur_un_ecran_present_se_retrouve() =>
        Assert.True(WindowPlacementCalculator.IsReachable(At(300, 200), [Principal, Second]));

    [Fact]
    public void Une_fenetre_du_second_ecran_se_retrouve_aussi() =>
        Assert.True(WindowPlacementCalculator.IsReachable(At(3900, 600), [Principal, Second]));

    [Fact]
    public void Une_fenetre_du_second_ecran_est_perdue_s_il_est_debranche() =>
        Assert.False(WindowPlacementCalculator.IsReachable(At(3900, 600), [Principal]));

    [Fact]
    public void Un_coin_qui_depasse_a_peine_ne_suffit_pas()
    {
        // Vingt pixels visibles ne donnent pas de quoi saisir la barre de
        // titre : la fenêtre s'ouvrirait sans qu'on puisse la ramener.
        Assert.False(WindowPlacementCalculator.IsReachable(At(2540, 1380), [Principal]));
    }

    [Fact]
    public void Une_fenetre_a_cheval_sur_le_bord_se_retrouve() =>
        Assert.True(WindowPlacementCalculator.IsReachable(At(2200, 900), [Principal]));

    [Fact]
    public void Une_place_sans_surface_ne_s_applique_pas()
    {
        Assert.False(WindowPlacementCalculator.IsReachable(null, [Principal]));
        Assert.False(WindowPlacementCalculator.IsReachable(new WindowPlacement(), [Principal]));
    }

    [Fact]
    public void Sans_ecran_il_n_y_a_nulle_part_ou_aller() =>
        Assert.False(WindowPlacementCalculator.IsReachable(At(0, 0), []));
}
