using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxMessageTests
{
    private static readonly DateOnly Day = new(2026, 9, 10);

    private const string Touch = """
        {
          "heading": "Bonus et Quêtes DOFUS Touch",
          "day": "10",
          "month": "Septange",
          "meryde": "Mau",
          "bonus": "Bonus : Élevage de Dragodindes",
          "bonusDetail": "Toutes les variétés de Dragodindes Ebène donnent naissance à un bébé supplémentaire.",
          "quest": "Quête : Offrande à Mau",
          "offering": "Récupérer 1 Dent de Dragodinde et rapporter l'offrande à Théodoran Ax",
          "monthEvent": "12 Septange : L'Aurore Pourpre"
        }
        """;

    [Fact]
    public void Une_journee_de_touch_se_lit_en_entier()
    {
        var day = AlmanaxMessage.From(Touch, Day);

        Assert.NotNull(day);
        Assert.Equal(Day, day!.Date);
        Assert.Equal("10 Septange", day.DofusianDay);
        Assert.Equal(1, day.Quantity);
        Assert.Equal("Dent de Dragodinde", day.Item);
        Assert.Equal("Élevage de Dragodindes", day.Bonus);
        Assert.Equal("Offrande à Mau", day.Quest);
        Assert.Equal("Mau", day.Meryde);
        Assert.Equal("12 Septange : L'Aurore Pourpre", day.MonthEvent);
    }

    [Fact]
    public void Un_mois_sans_evenement_ne_fait_rien_tomber()
    {
        // Not every month has one, and the block is then missing from
        // the page.
        const string json = """
            {
              "heading": "Bonus et Quêtes DOFUS Touch",
              "day": "10",
              "month": "Septange"
            }
            """;

        var day = AlmanaxMessage.From(json, Day);

        Assert.NotNull(day);
        Assert.Equal(string.Empty, day!.MonthEvent);
    }

    [Fact]
    public void Le_bloc_de_dofus_est_refuse_en_bloc()
    {
        // The portal serves both games on the same page. That day,
        // DOFUS was asking for an "Aile de dragodinde" (dragoturkey
        // wing): showing it under our title would send the reader
        // looking for the wrong item.
        var json = Touch
            .Replace("Bonus et Quêtes DOFUS Touch", "Bonus et Quêtes DOFUS", StringComparison.Ordinal)
            .Replace("Dent de Dragodinde", "Aile de dragodinde", StringComparison.Ordinal);

        Assert.Null(AlmanaxMessage.From(json, Day));
    }

    [Fact]
    public void Une_phrase_illisible_laisse_la_journee_lisible()
    {
        // The highlight falls away, the rest holds up: the whole
        // sentence already says what needs to be done.
        var json = Touch.Replace(
            "Récupérer 1 Dent de Dragodinde et rapporter l'offrande à Théodoran Ax",
            "Rapporter l'offrande à Théodoran Ax",
            StringComparison.Ordinal);

        var day = AlmanaxMessage.From(json, Day);

        Assert.NotNull(day);
        Assert.Null(day!.Quantity);
        Assert.Null(day.Item);
        Assert.Equal("Rapporter l'offrande à Théodoran Ax", day.Offering);
    }

    [Fact]
    public void Un_champ_absent_ne_fait_pas_tomber_la_journee()
    {
        var json = """{ "heading": "DOFUS Touch bonuses and quests" }""";

        var day = AlmanaxMessage.From(json, Day);

        Assert.NotNull(day);
        Assert.Equal(string.Empty, day!.Bonus);
        Assert.Equal(string.Empty, day.DofusianDay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas du json")]
    [InlineData("[]")]
    [InlineData("\"une chaîne\"")]
    public void Un_message_inexploitable_ne_rend_rien(string? json)
    {
        Assert.Null(AlmanaxMessage.From(json, Day));
    }
}
