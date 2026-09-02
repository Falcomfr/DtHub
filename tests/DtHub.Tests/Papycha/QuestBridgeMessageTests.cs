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
              "steps": ["Parler à Amayiro", "Aller au Bois"]
            }
            """);

        Assert.NotNull(message);
        Assert.Equal(QuestBridgeMessage.Loaded, message.Kind);
        Assert.Equal("<div>Départ</div>", message.Intro);
        Assert.Equal("<div>Succès</div>", message.Chain);
        Assert.True(message.Departure);
        Assert.Equal(["Parler à Amayiro", "Aller au Bois"], message.Steps);
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
    /// Une page de donjon n'a ni départ ni chaîne : le message vient sans ces
    /// champs, et il reste lisible.
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
    /// Ce que n'importe quelle page peut poster. Aucune de ces formes ne doit
    /// lever : le pont est lu dans un gestionnaire d'événement, où une exception
    /// emporte l'application.
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
    /// Un genre inconnu se lit sans broncher : la fenêtre l'ignore, et le pont
    /// pourra en poster de nouveaux sans qu'une version plus ancienne tombe.
    /// </summary>
    [Fact]
    public void LaisseAlaFenetreLesGenresQuElleNeConnaitPas()
    {
        var message = QuestBridgeMessage.Parse("""{"kind":"autre chose"}""");

        Assert.NotNull(message);
        Assert.Equal("autre chose", message.Kind);
    }

    /// <summary>
    /// Les champs d'un genre connu peuvent être de n'importe quel type : on
    /// garde ce qui se lit et l'on ignore le reste, plutôt que de jeter tout le
    /// message pour un champ.
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
    /// Un tableau d'étapes mêlé rend des étapes vides plutôt que de tomber :
    /// le rang des suivantes ne doit pas bouger, c'est lui qui sert à naviguer.
    /// </summary>
    [Fact]
    public void GardeLeRangDesEtapesMalgreUnTrou()
    {
        var message = QuestBridgeMessage.Parse(
            """{"kind":"loaded","steps":["une",null,3,"quatre"]}""");

        Assert.NotNull(message);
        Assert.Equal(["une", string.Empty, string.Empty, "quatre"], message.Steps);
    }
}
