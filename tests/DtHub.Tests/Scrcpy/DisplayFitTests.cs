using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class DisplayFitTests
{
    [Fact]
    public void La_definition_se_lit_sur_la_ligne_de_commande()
    {
        var used = DisplayFit.FromCommandLine(
            "--serial=USB0001 --new-display=2560x1440/320 --max-fps=60");

        Assert.Equal((2560, 1440), used);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--serial=USB0001 --max-fps=60")]
    [InlineData("--new-display=")]
    [InlineData("--new-display=0x0/240")]
    public void Une_ligne_sans_definition_lisible_ne_rend_rien(string? line)
    {
        Assert.Null(DisplayFit.FromCommandLine(line));
    }

    [Fact]
    public void Un_plafond_au_dessus_de_la_fenetre_est_annonce_sans_effet()
    {
        // The tier chosen is the first one above the window: raising
        // the ceiling further does not ask for a bigger display.
        var phrase = DisplayFit.Describe(2160, (2560, 1440));

        // Said as a way out, not as a wall: a player read "beyond that
        // this setting changes nothing" as 1440p being out of reach.
        Assert.Contains("2560 × 1440", phrase, StringComparison.Ordinal);
        Assert.Contains("agrandissez la fenêtre puis rouvrez-la", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_plafond_qui_mord_ne_se_fait_pas_traiter_d_inutile()
    {
        // Here the ceiling really does lower things: it is useful, and
        // the sentence must not discourage using it.
        var phrase = DisplayFit.Describe(1080, (1920, 1080));

        Assert.Contains("1920 × 1080", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("agrandissez", phrase, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1440, 20.0 / 9)]
    [InlineData(1080, 2392.0 / 1080)]
    public void Un_plafond_rabattu_au_format_du_telephone_n_est_pas_dit_inutile(int ceiling, double phoneAspect)
    {
        // At the phone's ratio, the ceiling is lowered to the same pixels
        // as 16:9: on a 3440x1440 screen, 1440 at 20:9 gives 2840x1280.
        // That ceiling is the one deciding, raising it would give more,
        // and the ratio read back from the rounded display must not make
        // it look higher than it is.
        var used = DisplayLadder.For(2000, 3440, 1440, ceiling, phoneAspect: phoneAspect);
        var phrase = DisplayFit.Describe(ceiling, used);

        Assert.DoesNotContain("agrandissez", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_debit_se_juge_sur_la_fenetre_ouverte_quand_le_plafond_ne_sert_pas()
    {
        // The window runs at 1080 under a ceiling of 1440: judging the
        // bitrate at 1440 announced 0.113 bit per pixel where 0.16 was
        // served, beside a sentence saying the window runs at 1080.
        Assert.Equal((1920, 1080), DisplayFit.Planned(1440, (1920, 1080)));
    }

    [Fact]
    public void Le_debit_se_juge_sur_le_plafond_quand_il_mord_ou_sans_fenetre()
    {
        Assert.Equal((1920, 1080), DisplayFit.Planned(1080, (2560, 1440)));
        Assert.Equal((2560, 1440), DisplayFit.Planned(1440, null));
    }

    [Fact]
    public void Sans_fenetre_ouverte_on_ne_devine_pas()
    {
        Assert.Empty(DisplayFit.Describe(2160, null));
        Assert.Empty(DisplayFit.Describe(2160, (0, 0)));
    }
}
