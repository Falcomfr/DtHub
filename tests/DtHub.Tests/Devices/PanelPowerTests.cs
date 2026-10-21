using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class PanelPowerTests
{
    /// <summary>
    /// Recorded on the 13T Pro, <c>dumpsys SurfaceFlinger --displays</c>,
    /// an account open in DT Hub and the screen held off: the game's
    /// virtual display is listed too, and it is always on.
    /// </summary>
    private const string Eteint = """
        Display 4627039422300187648
            port=0
            connectionType=Internal
            name="MTKDEV"
            powerMode=Off
            optimizationPolicy=optimizeForPerformance
        Virtual Display 11529215050226234079
            name="scrcpy"
            powerMode=On
        """;

    [Fact]
    public void Un_ecran_eteint_sous_le_jeu_se_lit_eteint() =>
        Assert.False(PanelPower.IsLit(Eteint));

    [Fact]
    public void Un_ecran_rallume_par_le_joueur_se_lit_allume() =>
        Assert.True(PanelPower.IsLit(Eteint.Replace("powerMode=Off", "powerMode=On", StringComparison.Ordinal)));

    [Fact]
    public void Un_mode_suivi_de_son_numero_se_lit_aussi() =>
        Assert.True(PanelPower.IsLit(Eteint.Replace("powerMode=Off", "powerMode=On (2)", StringComparison.Ordinal)));

    [Fact]
    public void L_ecran_toujours_allume_d_une_veille_ne_compte_pas_comme_allume() =>
        Assert.False(PanelPower.IsLit(Eteint.Replace("powerMode=Off", "powerMode=Doze", StringComparison.Ordinal)));

    /// <summary>
    /// Recorded from a player's Samsung on 2026-10-21, the screen lit with
    /// the power button under the game: One UI writes the mode in capitals.
    /// </summary>
    private const string SamsungAllume = """
        Display 4630947191136750483
            port=147
            connectionType=Internal
            name=""
            powerMode=ON
            optimizationPolicy=optimizeForPerformance
            HFR_MODE=3
        Virtual Display 11529215048697563217
            name="scrcpy"
            powerMode=ON
        """;

    [Fact]
    public void Un_samsung_rallume_se_lit_allume() =>
        Assert.True(PanelPower.IsLit(SamsungAllume));

    [Fact]
    public void Un_samsung_eteint_se_lit_eteint() =>
        Assert.False(PanelPower.IsLit(SamsungAllume.Replace("powerMode=ON", "powerMode=OFF", StringComparison.Ordinal)));

    [Fact]
    public void Un_pliable_est_allume_si_l_un_de_ses_ecrans_l_est()
    {
        var pliable = Eteint.Replace(
            "Virtual Display",
            "Display 4627039422300187649\n    powerMode=On\nVirtual Display",
            StringComparison.Ordinal);

        Assert.True(PanelPower.IsLit(pliable));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Virtual Display 1\n    powerMode=On")]
    public void Une_reponse_sans_ecran_physique_ne_dit_rien(string? dump) =>
        Assert.Null(PanelPower.IsLit(dump));
}
