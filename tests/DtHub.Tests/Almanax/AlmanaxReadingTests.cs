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
        // Better to show nothing than to show another game's offering:
        // on 10 September 2026, DOFUS asked for an "Aile de dragodinde"
        // (dragoturkey wing) where Touch asked for a "Dent" (tooth).
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
        // The page renders its text over several lines, with the
        // template's indentation: the raw text arrives riddled with
        // spaces and line breaks.
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
        // And this is not a failure: the window then displays the whole
        // sentence, which already says what needs to be done.
        Assert.Null(AlmanaxReading.Offering(sentence));
    }

    [Fact]
    public void Un_nombre_sans_objet_derriere_ne_se_lit_pas()
    {
        Assert.Null(AlmanaxReading.Offering("Récupérer 1 et rapporter l'offrande"));
    }

    // Read from the portal's Touch block, January 6, March 17 and
    // September 12, 2027, in the three languages.
    [Theory]
    [InlineData("Quête répétable Une quête répétable spéciale est disponible aujourd'hui près de l'atelier des boulangers du village d'Amakna .")]
    [InlineData("Repeatable quest A special repeatable quest is available today near the Bakers' Workshop in Amakna Village .")]
    [InlineData("Misión repetible Hoy se podrá hacer una misión repetible especial cerca del taller de panaderos del pueblo de Amakna .")]
    [InlineData("Bénédiction du Fin Patraque Ayiri Chkofi attend les aventuriers qui veulent boire un petit coup près de la taverne du village d'Amakna.")]
    [InlineData("Saint Potrick blessing Ayerish Kaffee is waiting near the Amakna Village Inn for adventurers who want to join him for a drink.")]
    [InlineData("Bendición de San Patírico Airish Kofi espera a los aventureros que deseen beber un trago ceca de la taberna del pueblo de Amakna.")]
    [InlineData("Bonta et Brâkmar Les récompenses en points d'expérience des quêtes d'alignement sont augmentées de 100 %. De plus, une quête répétable spéciale est disponible aujourd'hui à l'intérieur des milices de Bonta ou de Brâkmar .")]
    public void Un_jour_qui_demande_d_aller_ailleurs_se_reconnait(string bonus)
    {
        Assert.True(AlmanaxReading.CallsElsewhere(bonus));
    }

    [Theory]
    [InlineData("Quête répétable")]
    [InlineData("Repeatable quest")]
    [InlineData("Misión repetible")]
    [InlineData("Bénédiction de Miss Triste")]
    public void Le_titre_seul_suffit(string title)
    {
        Assert.True(AlmanaxReading.CallsElsewhere(title));
    }

    [Theory]
    [InlineData("Butin Les chances d'obtenir du butin sont augmentées de 75 % sur les créatures de type Monstres des Crocs de Verre .")]
    [InlineData("Expérience des quêtes Les gains d'expérience sont augmentés de 100 % pour toutes les quêtes.")]
    [InlineData("Quest experience")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_bonus_ordinaire_ne_demande_rien_ailleurs(string? bonus)
    {
        Assert.False(AlmanaxReading.CallsElsewhere(bonus));
    }
}
