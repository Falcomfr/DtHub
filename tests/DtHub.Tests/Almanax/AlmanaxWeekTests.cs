using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxWeekTests
{
    // Read from the portal's week page on October 11, 2026. The 12th
    // carries two events, and the page then lists the day twice.
    private const string Week = """
        {
          "week": [
            { "href": "/fr/almanax/2026-10-12-Ganks Fivingue", "label": "Bonus DOFUS TOUCH :", "value": "Expérience des quêtes" },
            { "href": "/fr/almanax/2026-10-12-Ganks Fivingue", "label": "Quête DOFUS TOUCH :", "value": "Offrande à Diakollec" },
            { "href": "/fr/almanax/2026-10-12-La Fête de la Bamboussole", "label": "Bonus DOFUS TOUCH :", "value": "Expérience des quêtes" },
            { "href": "/fr/almanax/2026-10-13", "label": "Bonus DOFUS TOUCH :", "value": "Apparition des ressources" },
            { "href": "/fr/almanax/2026-10-13", "label": "Quête DOFUS TOUCH :", "value": "Offrande à Astirout" }
          ]
        }
        """;

    [Fact]
    public void Une_semaine_donne_le_bonus_de_chaque_jour()
    {
        var week = AlmanaxWeek.From(Week);

        Assert.Equal("Apparition des ressources", week[new DateOnly(2026, 10, 13)]);
    }

    [Fact]
    public void Un_jour_a_deux_evenements_ne_compte_qu_une_fois()
    {
        var week = AlmanaxWeek.From(Week);

        Assert.Equal(2, week.Count);
        Assert.Equal("Expérience des quêtes", week[new DateOnly(2026, 10, 12)]);
    }

    [Fact]
    public void Le_bonus_de_dofus_est_ignore()
    {
        // The bridge only picks Touch's lines, but any page can post:
        // the label is checked here as well.
        const string json = """
            { "week": [ { "href": "/fr/almanax/2026-10-12", "label": "Bonus DOFUS :", "value": "Voyages dimensionnels" } ] }
            """;

        Assert.Empty(AlmanaxWeek.From(json));
    }

    [Theory]
    [InlineData("Bonus DOFUS TOUCH :")]
    [InlineData("bonus dofus touch:")]
    public void L_etiquette_se_lit_sans_dependre_de_la_casse(string label)
    {
        var json = "{ \"week\": [ { \"href\": \"/en/almanax/2026-10-12\", \"label\": \"" + label + "\", \"value\": \"Quest experience\" } ] }";

        Assert.Equal("Quest experience", AlmanaxWeek.From(json)[new DateOnly(2026, 10, 12)]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("pas du json")]
    [InlineData("{ \"heading\": \"Bonus et Quêtes DOFUS Touch\" }")]
    [InlineData("{ \"week\": [ { \"href\": \"/fr/almanax/semaine\", \"label\": \"Bonus DOFUS TOUCH :\", \"value\": \"Butin\" } ] }")]
    public void Un_message_illisible_ne_donne_rien(string? json)
    {
        Assert.Empty(AlmanaxWeek.From(json));
    }
}
