using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class QuestBridgeMessageTests
{
    [Fact]
    public void LitUnGuideCadre()
    {
        var message = QuestBridgeMessage.Parse(
            """
            {
              "kind": "loaded",
              "intro": "<div>Départ</div>",
              "chain": "<div>Succès</div>",
              "departure": true,
              "steps": [
                { "text": "Parler à Amayiro", "title": false },
                { "text": "Aller au Bois", "title": false }
              ]
            }
            """);

        Assert.NotNull(message);
        Assert.Equal(QuestBridgeMessage.Loaded, message.Kind);
        Assert.Equal("<div>Départ</div>", message.Intro);
        Assert.Equal("<div>Succès</div>", message.Chain);
        Assert.True(message.Departure);

        Assert.Equal(
            [new QuestStep("Parler à Amayiro", false), new QuestStep("Aller au Bois", false)],
            message.Steps);
    }

    /// <summary>
    /// A dungeon, raid, lair or path sheet is read through its section
    /// titles. The bridge says which ones are titles: that is what
    /// decides whether to show them.
    /// </summary>
    [Fact]
    public void LitLaNatureDeChaqueEtape()
    {
        var message = QuestBridgeMessage.Parse(
            """
            {
              "kind": "loaded",
              "steps": [
                { "text": "", "title": false },
                { "text": "Les salles", "title": true },
                { "text": "Parlez à Otomaï", "title": false }
              ]
            }
            """);

        Assert.NotNull(message);

        Assert.Equal(
            [
                new QuestStep(string.Empty, false),
                new QuestStep("Les salles", true),
                new QuestStep("Parlez à Otomaï", false),
            ],
            message.Steps);
    }

    /// <summary>
    /// A step written as a bare string stays readable, and counts as an
    /// instruction: that is the form the bridge used to write before it
    /// carried the nature field, and the only one a page of the site
    /// could ever post on its own.
    /// </summary>
    [Fact]
    public void UneEtapeSansNatureCompteCommeUneConsigne()
    {
        var message = QuestBridgeMessage.Parse(
            """{"kind":"loaded","steps":["Parler à Amayiro"]}""");

        var step = Assert.Single(message!.Steps);

        Assert.Equal("Parler à Amayiro", step.Text);
        Assert.False(step.IsTitle);
    }

    /// <summary>
    /// A nature written as anything other than a true boolean does not
    /// turn a paragraph into a title: better to show nothing than to
    /// show prose.
    /// </summary>
    [Theory]
    [InlineData("\"oui\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void UneNatureQuiNEstPasVraieNeTitreRien(string valeur)
    {
        var message = QuestBridgeMessage.Parse(
            """{"kind":"loaded","steps":[{"text":"Les salles","title":VALEUR}]}"""
                .Replace("VALEUR", valeur, StringComparison.Ordinal));

        Assert.False(Assert.Single(message!.Steps).IsTitle);
    }

    [Fact]
    public void LitUneEtape()
    {
        var message = QuestBridgeMessage.Parse("""{"kind":"step","index":4}""");

        Assert.NotNull(message);
        Assert.Equal(QuestBridgeMessage.Step, message.Kind);
        Assert.Equal(4, message.Index);
    }

    /// <summary>
    /// A dungeon page has neither a departure nor a chain: the message
    /// comes without these fields, and it stays readable.
    /// </summary>
    [Fact]
    public void SePasseDesChampsQueLeSiteNeDonnePas()
    {
        var message = QuestBridgeMessage.Parse("""{"kind":"loaded"}""");

        Assert.NotNull(message);
        Assert.Null(message.Intro);
        Assert.Null(message.Chain);
        Assert.False(message.Departure);
        Assert.Empty(message.Steps);
    }

    /// <summary>
    /// What any page might post. None of these forms must throw: the
    /// bridge is read inside an event handler, where an exception takes
    /// the whole application down with it.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("pas du json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("[{\"kind\":\"loaded\"}]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"loaded\"")]
    [InlineData("{}")]
    [InlineData("""{"genre":"loaded"}""")]
    [InlineData("""{"kind":null}""")]
    [InlineData("""{"kind":7}""")]
    [InlineData("""{"kind":{"loaded":true}}""")]
    [InlineData("""{"kind":"step"}""")]
    [InlineData("""{"kind":"step","index":"4"}""")]
    [InlineData("""{"kind":"step","index":null}""")]
    [InlineData("""{"kind":"step","index":1.5}""")]
    [InlineData("""{"kind":"step","index":99999999999999}""")]
    public void NeRendRienDeCeQuOnNePeutPasLire(string? charge) =>
        Assert.Null(QuestBridgeMessage.Parse(charge));

    /// <summary>
    /// An unknown kind is read without flinching: the window ignores it,
    /// and the bridge will be able to post new ones later without an
    /// older version crashing.
    /// </summary>
    [Fact]
    public void LaisseAlaFenetreLesGenresQuElleNeConnaitPas()
    {
        var message = QuestBridgeMessage.Parse("""{"kind":"autre chose"}""");

        Assert.NotNull(message);
        Assert.Equal("autre chose", message.Kind);
    }

    /// <summary>
    /// The fields of a known kind can be of any type: what can be read
    /// is kept and the rest is ignored, rather than discarding the whole
    /// message over one field.
    /// </summary>
    [Fact]
    public void IgnoreLesChampsDuMauvaisType()
    {
        var message = QuestBridgeMessage.Parse(
            """{"kind":"loaded","intro":12,"chain":[],"departure":"oui","steps":"une"}""");

        Assert.NotNull(message);
        Assert.Null(message.Intro);
        Assert.Null(message.Chain);
        Assert.False(message.Departure);
        Assert.Empty(message.Steps);
    }

    /// <summary>
    /// A mixed-up steps array yields empty steps rather than crashing:
    /// the rank of the following ones must not move, since it is what is
    /// used to navigate.
    /// </summary>
    [Fact]
    public void GardeLeRangDesEtapesMalgreUnTrou()
    {
        var message = QuestBridgeMessage.Parse(
            """{"kind":"loaded","steps":["une",null,3,"quatre"]}""");

        Assert.NotNull(message);

        Assert.Equal(
            [
                new QuestStep("une", false),
                new QuestStep(string.Empty, false),
                new QuestStep(string.Empty, false),
                new QuestStep("quatre", false),
            ],
            message.Steps);
    }
}
