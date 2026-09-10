using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxReadingTests
{
    [Theory]
    [InlineData("Bonus et Quêtes DOFUS Touch")]
    [InlineData("DOFUS Touch bonuses and quests")]
    [InlineData("Bonus y misiones DOFUS Touch")]
    public void Le_bloc_de_touch_est_reconnu_dans_les_trois_langues(string heading)
    {
        Assert.True(AlmanaxReading.IsTouch(heading));
    }

    [Theory]
    [InlineData("Bonus et Quêtes DOFUS")]
    [InlineData("DOFUS bonuses and quests")]
    [InlineData("")]
    [InlineData(null)]
    public void Le_bloc_de_dofus_est_refuse(string? heading)
    {
        // Mieux vaut ne rien montrer que montrer l'offrande d'un autre jeu :
        // le 10 septembre 2026, DOFUS demandait une Aile de dragodinde là où
        // Touch demandait une Dent.
        Assert.False(AlmanaxReading.IsTouch(heading));
    }

    [Theory]
    [InlineData("Bonus : Élevage de Dragodindes", "Élevage de Dragodindes")]
    [InlineData("Bonus: Dragoturkey breeding", "Dragoturkey breeding")]
    [InlineData("Bonus: Cría de dragopavos", "Cría de dragopavos")]
    [InlineData("Quête : Offrande à Mau", "Offrande à Mau")]
    [InlineData("Quest: Offering for Mau", "Offering for Mau")]
    [InlineData("Misión: Ofrenda para Mau", "Ofrenda para Mau")]
    public void Le_prefixe_du_portail_tombe(string given, string expected)
    {
        Assert.Equal(expected, AlmanaxReading.AfterColon(given));
    }

    [Fact]
    public void Un_intitule_sans_deux_points_est_rendu_tel_quel()
    {
        Assert.Equal("Élevage de Dragodindes", AlmanaxReading.AfterColon("  Élevage de Dragodindes  "));
    }

    [Theory]
    [InlineData("Récupérer 1 Dent de Dragodinde et rapporter l'offrande à Théodoran Ax", 1, "Dent de Dragodinde")]
    [InlineData("Find 1 Dragoturkey Tooth and take the offering to Antyklime Ax", 1, "Dragoturkey Tooth")]
    [InlineData("Recolectar 1 Diente de dragopavo y llevárselo a Ontoral Zo", 1, "Diente de dragopavo")]
    [InlineData("Récupérer 2 Corne de Dragoeuf Guerrier et rapporter l'offrande à Théodoran Ax", 2, "Corne de Dragoeuf Guerrier")]
    [InlineData("Récupérer 10 Ectoplasme et rapporter l'offrande à Théodoran Ax", 10, "Ectoplasme")]
    public void L_offrande_se_lit_dans_les_trois_langues(string sentence, int quantity, string item)
    {
        var offering = AlmanaxReading.Offering(sentence);

        Assert.NotNull(offering);
        Assert.Equal(quantity, offering!.Value.Quantity);
        Assert.Equal(item, offering.Value.Item);
    }

    [Fact]
    public void Les_espaces_multiples_du_portail_ne_genent_pas()
    {
        // La page rend son texte sur plusieurs lignes, avec l'indentation du
        // gabarit : le texte brut arrive criblé d'espaces et de sauts.
        var offering = AlmanaxReading.Offering(
            "\n               Récupérer 1  Dent de Dragodinde  et rapporter l'offrande à Théodoran Ax             ");

        Assert.Equal((1, "Dent de Dragodinde"), offering);
    }

    [Theory]
    [InlineData("Rapporter l'offrande à Théodoran Ax")]
    [InlineData("")]
    [InlineData(null)]
    public void Une_phrase_sans_nombre_ne_se_lit_pas(string? sentence)
    {
        // Et ce n'est pas un échec : la fenêtre affiche alors la phrase
        // entière, qui dit déjà ce qu'il faut faire.
        Assert.Null(AlmanaxReading.Offering(sentence));
    }

    [Fact]
    public void Un_nombre_sans_objet_derriere_ne_se_lit_pas()
    {
        Assert.Null(AlmanaxReading.Offering("Récupérer 1 et rapporter l'offrande"));
    }
}
