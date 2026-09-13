using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

/// <summary>
/// Ce que le bandeau montre. La règle vivait dans la vue, que les
/// épreuves n'atteignent pas, et trois propriétés la décidaient
/// chacune de leur côté.
/// </summary>
public sealed class ErrorBannerTests
{
    [Fact]
    public void Sans_rien_a_dire_le_bandeau_est_vide()
    {
        var banniere = ErrorBanner.Of([]);

        Assert.True(banniere.IsEmpty);
        Assert.Null(banniere.Text);
        Assert.False(banniere.IsSerious);
    }

    [Fact]
    public void Une_liste_nulle_vaut_une_liste_vide()
    {
        Assert.True(ErrorBanner.Of(null).IsEmpty);
    }

    [Fact]
    public void Chaque_constat_tient_sa_ligne()
    {
        // Un téléphone en a porté trois à la fois, et n'en montrer
        // qu'un cachait les deux autres.
        var banniere = ErrorBanner.Of(
            [BannerLine.Of("Verrouillé."), BannerLine.Of("Stockage bas."), BannerLine.Of("Batterie.")]);

        Assert.Equal(
            "Verrouillé." + Environment.NewLine + "Stockage bas." + Environment.NewLine + "Batterie.",
            banniere.Text);
    }

    [Fact]
    public void Le_texte_affiche_et_ce_qui_decide_l_affichage_sont_la_meme_chose()
    {
        // C'est le défaut d'origine : la visibilité tenait à une
        // propriété et le texte à une autre, donc le bandeau
        // s'ouvrait sur le constat de la fois d'avant.
        var banniere = ErrorBanner.Of([BannerLine.Of("Le lancement a échoué.")]);

        Assert.False(banniere.IsEmpty);
        Assert.Equal("Le lancement a échoué.", banniere.Text);
    }

    [Fact]
    public void Une_ligne_grave_colore_le_bandeau()
    {
        var banniere = ErrorBanner.Of([BannerLine.Of("Stockage bas."), BannerLine.Grave("Batterie à plat.")]);

        Assert.True(banniere.IsSerious);
    }

    [Fact]
    public void Un_constat_grave_absent_du_bandeau_ne_le_colore_pas()
    {
        // La gravité venait du pire constat de toute l'application,
        // y compris ceux qui s'affichent sous le nom d'un téléphone.
        var banniere = ErrorBanner.Of([BannerLine.Of("Stockage bas.")]);

        Assert.False(banniere.IsSerious);
    }

    [Fact]
    public void Une_ligne_blanche_ne_fait_pas_un_bandeau()
    {
        // Un message vide n'est pas une absence de problème, c'est un
        // message que personne n'a écrit.
        Assert.True(ErrorBanner.Of([BannerLine.Of("   "), BannerLine.Of("")]).IsEmpty);
    }

    [Fact]
    public void Une_ligne_blanche_au_milieu_ne_creuse_pas_de_trou()
    {
        var banniere = ErrorBanner.Of(
            [BannerLine.Of("Premier."), BannerLine.Of("  "), BannerLine.Of("Second.")]);

        Assert.Equal("Premier." + Environment.NewLine + "Second.", banniere.Text);
    }

    [Fact]
    public void Un_avis_simple_n_est_pas_grave()
    {
        var banniere = ErrorBanner.Of([new BannerLine("Fenêtre rouverte.", HealthSeverity.Notice)]);

        Assert.False(banniere.IsSerious);
        Assert.Equal("Fenêtre rouverte.", banniere.Text);
    }
}
