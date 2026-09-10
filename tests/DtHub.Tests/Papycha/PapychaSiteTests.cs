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
}
