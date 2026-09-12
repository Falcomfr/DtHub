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
    /// Le nom du site au début du texte ne fait pas une page du site : ces
    /// trois formes mènent ailleurs, et un simple préfixe en laissait passer.
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
        // Un objet à espaces et à apostrophe est le cas courant, et il ne doit
        // pas casser l'adresse.
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
        // Sans quoi le lien mènerait à la page de résultats vide du site.
        Assert.Null(PapychaSite.SearchUrl(query));
    }

    [Fact]
    public void L_adresse_de_recherche_appartient_bien_au_site()
    {
        // Elle passe par nos fenêtres comme les autres : elle doit franchir le
        // contrôle d'appartenance.
        Assert.True(PapychaSite.Owns(PapychaSite.SearchUrl("bouftou")));
    }
    /// <summary>
    /// Adresse relevee au caractère près dans le pied d'article de la quete
    /// « La potion Lèche-bottes », colonne des suivantes.
    /// </summary>
    private const string ArbreDuSucces =
        "https://papycha.fr/succes/?pqt_success=success:cards.lechage-de-bottes#succes-selectionne";

    [Fact]
    public void L_arbre_des_succes_se_reconnait()
    {
        // La seule page du site qu'on renvoie au navigateur : ce n'est pas un
        // guide mais un outil qu'on déplie et qu'on parcourt.
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
        // Le site sert la même page sous ces cinq formes. En rater une ouvrirait
        // l'arbre dans une de nos fenêtres une fois sur cinq, sans qu'on sache
        // pourquoi.
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
        // L'exception doit rester une exception. Une quête, la racine du site,
        // un titre qui commence par « succès » : rien de cela ne part au
        // navigateur.
        Assert.False(PapychaSite.IsSuccessTree(url));
    }
}
