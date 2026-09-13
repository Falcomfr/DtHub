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
        // Le palier retenu est le premier au-dessus de la fenêtre : monter le
        // plafond au-delà ne demande pas un afficheur plus grand.
        var phrase = DisplayFit.Describe(2160, (2560, 1440));

        Assert.Contains("2560 × 1440", phrase, StringComparison.Ordinal);
        Assert.Contains("ne change rien", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_plafond_qui_mord_ne_se_fait_pas_traiter_d_inutile()
    {
        // Ici le plafond rabaisse pour de bon : il sert, et la phrase ne doit
        // pas décourager de s'en servir.
        var phrase = DisplayFit.Describe(1080, (1920, 1080));

        Assert.Contains("1920 × 1080", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("ne change rien", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void Sans_fenetre_ouverte_on_ne_devine_pas()
    {
        Assert.Empty(DisplayFit.Describe(2160, null));
        Assert.Empty(DisplayFit.Describe(2160, (0, 0)));
    }
}
