using DtHub.Core.Almanax;
using DtHub.Core.Localization;

namespace DtHub.Tests.Almanax;

public class AlmanaxCalendarTests
{
    [Theory]
    [InlineData("fr", "https://www.krosmoz.com/fr/almanax?game=dofustouch")]
    [InlineData("es", "https://www.krosmoz.com/es/almanax?game=dofustouch")]
    [InlineData("en", "https://www.krosmoz.com/en/almanax?game=dofustouch")]
    public void L_adresse_suit_la_langue_de_l_application(string language, string expected)
    {
        Assert.Equal(expected, AlmanaxCalendar.UrlFor(language));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ja")]
    public void Une_langue_inconnue_retombe_sur_la_langue_neutre(string? language)
    {
        Assert.Equal("https://www.krosmoz.com/en/almanax?game=dofustouch", AlmanaxCalendar.UrlFor(language));
    }

    [Fact]
    public void Toutes_les_langues_de_l_application_ont_un_chemin_sur_le_portail()
    {
        // Sans quoi une langue tomberait sur le repli sans qu'on le sache, et
        // afficherait l'Almanax en anglais à qui lit l'application en espagnol.
        foreach (var language in AppLanguage.Supported)
        {
            Assert.Contains("/" + language + "/almanax", AlmanaxCalendar.UrlFor(language), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void L_adresse_porte_toujours_le_filtre_dofus_touch()
    {
        // Sans le filtre, le portail rend l'Almanax de DOFUS, dont les offrandes
        // ne sont pas celles de Touch : « Aile de dragodinde » au lieu de
        // « Dent de Dragodinde » le 10 septembre 2026. Le même écran, un autre
        // jeu, et rien qui le dise.
        foreach (var language in AppLanguage.Supported)
        {
            Assert.EndsWith("?game=dofustouch", AlmanaxCalendar.UrlFor(language), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void L_adresse_d_un_jour_porte_la_date_et_le_filtre()
    {
        Assert.Equal(
            "https://www.krosmoz.com/fr/almanax/2026-09-10?game=dofustouch",
            AlmanaxCalendar.UrlFor("fr", new DateOnly(2026, 9, 10)));
    }

    [Fact]
    public void La_date_est_ecrite_en_iso_quelle_que_soit_la_culture()
    {
        // Sous une culture qui écrit « 10/09/2026 », une date composée avec la
        // culture courante donnerait un chemin que le portail ne connaît pas.
        Assert.Equal(
            "https://www.krosmoz.com/es/almanax/2026-01-05?game=dofustouch",
            AlmanaxCalendar.UrlFor("es", new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void L_adresse_d_un_jour_franchit_son_propre_controle()
    {
        Assert.True(AlmanaxCalendar.Owns(AlmanaxCalendar.UrlFor("fr", new DateOnly(2026, 9, 10))));
    }

    [Theory]
    [InlineData("https://www.krosmoz.com/fr/almanax?game=dofustouch")]
    [InlineData("https://www.krosmoz.com/fr/almanax/2026-09-10")]
    [InlineData("https://www.krosmoz.com/fr/almanax/aide")]
    [InlineData("https://krosmoz.com/en/almanax")]
    public void Une_page_de_l_almanax_reste_dans_la_fenetre(string url)
    {
        Assert.True(AlmanaxCalendar.Owns(url));
    }

    [Theory]
    [InlineData("https://www.krosmoz.com/fr/dofus")]
    [InlineData("https://www.krosmoz.com/")]
    [InlineData("https://www.ankama.com/fr/almanax")]
    [InlineData("http://www.krosmoz.com/fr/almanax")]
    [InlineData("file:///C:/almanax.html")]
    [InlineData("https://krosmoz.com@ailleurs.example/fr/almanax")]
    [InlineData(null)]
    [InlineData("")]
    public void Tout_le_reste_part_au_navigateur(string? url)
    {
        Assert.False(AlmanaxCalendar.Owns(url));
    }
}
