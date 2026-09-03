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
    public void L_adresse_de_contact_appartient_au_site()
    {
        // C'est le repli du signalement : elle doit passer la même garde que
        // les pages de guide, faute de quoi la fenêtre la confierait au
        // navigateur au lieu de l'afficher.
        Assert.True(PapychaSite.Owns(PapychaSite.ContactUrl));
        Assert.Equal("https://papycha.fr/contact/", PapychaSite.ContactUrl);
    }
}
