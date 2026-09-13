using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class PapychaSiteTests
{
    [Theory]
    [InlineData("https://papycha.fr/")]
    [InlineData("https://papycha.fr")]
    [InlineData("https://papycha.fr/quetes/quetes-dastrub/")]
    [InlineData("https://PAPYCHA.FR/quetes/")]
    [InlineData("https://www.papycha.fr/quetes/")]
    [InlineData("  https://papycha.fr/quetes/  ")]
    [InlineData("https://papycha.fr/quetes/#etape-3")]
    public void ReconnaitLesPagesDuSite(string adresse) =>
        Assert.True(PapychaSite.Owns(adresse));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/quetes/quetes-dastrub/")]
    [InlineData("http://papycha.fr/quetes/")]
    [InlineData("https://dofus-touch.fandom.com/")]
    [InlineData("file:///C:/Windows/System32/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>salut</h1>")]
    [InlineData("about:blank")]
    public void RefuseLeReste(string? adresse) =>
        Assert.False(PapychaSite.Owns(adresse));

    /// <summary>
    /// The site's name at the start of the text does not make it a
    /// page of the site: these three forms lead elsewhere, and a
    /// simple prefix check used to let them through.
    /// </summary>
    [Theory]
    [InlineData("https://papycha.fr.ailleurs.example/quetes/")]
    [InlineData("https://papycha.fr@ailleurs.example/quetes/")]
    [InlineData("https://ailleurs.example/https://papycha.fr/quetes/")]
    public void NeSeLaissePasPrendreAuNom(string adresse) =>
        Assert.False(PapychaSite.Owns(adresse));

    [Fact]
    public void L_adresse_de_recherche_porte_le_texte_cherche()
    {
        Assert.Equal("https://papycha.fr/?s=bouftou", PapychaSite.SearchUrl("bouftou"));
    }

    [Fact]
    public void Le_texte_cherche_est_echappe()
    {
        // A search term with spaces and an apostrophe is the common
        // case, and it must not break the address.
        Assert.Equal(
            "https://papycha.fr/?s=Dofus%20Ocre",
            PapychaSite.SearchUrl("Dofus Ocre"));
    }

    [Fact]
    public void Les_blancs_de_bord_ne_comptent_pas()
    {
        Assert.Equal("https://papycha.fr/?s=piou", PapychaSite.SearchUrl("  piou  "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rien_a_chercher_ne_donne_aucune_adresse(string? query)
    {
        // Otherwise the link would lead to the site's empty results
        // page.
        Assert.Null(PapychaSite.SearchUrl(query));
    }

    [Fact]
    public void L_adresse_de_recherche_appartient_bien_au_site()
    {
        // It goes through our windows like the others: it must pass
        // the ownership check.
        Assert.True(PapychaSite.Owns(PapychaSite.SearchUrl("bouftou")));
    }
    /// <summary>
    /// Address captured character for character from the article
    /// footer of the quest "La potion Lèche-bottes", in the "next
    /// quests" column.
    /// </summary>
    private const string ArbreDuSucces =
        "https://papycha.fr/succes/?pqt_success=success:cards.lechage-de-bottes#succes-selectionne";

    [Fact]
    public void L_arbre_des_succes_se_reconnait()
    {
        // The only page of the site that we send to the browser: it
        // is not a guide but a tool that gets expanded and browsed.
        Assert.True(PapychaSite.IsSuccessTree(ArbreDuSucces));
    }

    [Theory]
    [InlineData("https://papycha.fr/succes/")]
    [InlineData("https://papycha.fr/succes")]
    [InlineData("https://papycha.fr/SUCCES/")]
    [InlineData("https://papycha.fr/succes/#succes-selectionne")]
    [InlineData("https://papycha.fr/succes/?pqt_success=x")]
    public void La_barre_finale_la_casse_et_l_ancre_ne_changent_rien(string url)
    {
        // The site serves the same page under these five forms.
        // Missing one would open the tree in one of our windows one
        // time out of five, without knowing why.
        Assert.True(PapychaSite.IsSuccessTree(url));
    }

    [Theory]
    [InlineData("https://papycha.fr/quete-la-potion-leche-bottes/")]
    [InlineData("https://papycha.fr/")]
    [InlineData("https://papycha.fr/succes-de-quelque-chose/")]
    [InlineData("https://papycha.fr/quetes/succes/")]
    [InlineData("https://exemple.test/succes/")]
    [InlineData("http://papycha.fr/succes/")]
    [InlineData(null)]
    [InlineData("")]
    public void Tout_le_reste_continue_de_s_ouvrir_chez_nous(string? url)
    {
        // The exception must remain an exception. A quest, the site's
        // root, a title that starts with "succès" ("achievement"):
        // none of that goes to the browser.
        Assert.False(PapychaSite.IsSuccessTree(url));
    }
}
