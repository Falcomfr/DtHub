using DtHub.Core.Almanax;

namespace DtHub.Tests.Almanax;

public class AlmanaxProfessionsTests
{
    private static readonly DateOnly Day = new(2027, 10, 24);

    private static AlmanaxDay With(string bonus, string detail) =>
        new(Day, string.Empty, string.Empty, 1, "Fer", bonus, detail, string.Empty, string.Empty, string.Empty);

    // Read from the portal's Touch block over 2027.
    [Theory]
    [InlineData("Minerai abondant", "Les quantités de Fer récupérées par les Mineurs sont augmentées de 150 %.")]
    [InlineData("Économie d'ingrédients", "Les Mineurs économisent 10 % de leurs ingrédients lorsqu'ils fabriquent des objets.")]
    [InlineData("Économie d'ingrédients", "Les récolteurs économisent 15 % de leurs ingrédients lorsqu'ils fabriquent des objets dans la zone de la forêt des Pins Perdus .")]
    [InlineData("Économie d'ingrédients", "Les artisans économisent 15 % de leurs ingrédients lorsqu'ils fabriquent des objets dans la zone de Brâkmar .")]
    [InlineData("Expérience des métiers", "Les gains d'expérience sont augmentés de 50 % pour les métiers de récolte .")]
    [InlineData("Expérience des métiers", "Les gains d'expérience sont augmentés de 50 % pour tous les métiers.")]
    public void Un_mineur_voit_ses_jours_et_ceux_de_tous_les_recolteurs(string bonus, string detail)
    {
        Assert.True(AlmanaxProfessions.Matches(With(bonus, detail), "mineur"));
    }

    [Theory]
    [InlineData("Bois abondant", "Les quantités de Bois d'If et de Bois de Bambou récupérées par les Bûcherons sont augmentées de 150 %.")]
    [InlineData("Butin", "Les chances d'obtenir du butin sont augmentées de 75 % sur les créatures de type Monstres des Crocs de Verre .")]
    [InlineData("Économie d'ingrédients", "Les Boulangers économisent 10 % de leurs ingrédients lorsqu'ils fabriquent des objets.")]
    public void Un_mineur_ne_voit_pas_les_jours_des_autres(string bonus, string detail)
    {
        Assert.False(AlmanaxProfessions.Matches(With(bonus, detail), "mineur"));
    }

    [Fact]
    public void Un_metier_de_fabrication_ne_voit_pas_les_metiers_de_recolte()
    {
        var harvest = With("Expérience des métiers", "Les gains d'expérience sont augmentés de 50 % pour les métiers de récolte .");

        Assert.False(AlmanaxProfessions.Matches(harvest, "boulanger"));
    }

    [Fact]
    public void Un_metier_de_fabrication_voit_les_jours_des_artisans()
    {
        var crafters = With("Économie d'ingrédients", "Les artisans économisent 15 % de leurs ingrédients lorsqu'ils fabriquent des objets dans la zone de Bonta .");

        Assert.True(AlmanaxProfessions.Matches(crafters, "Bijoutier"));
    }

    [Fact]
    public void Un_bonus_qui_n_est_pas_un_metier_ne_ramene_pas_les_jours_generaux()
    {
        var crafters = With("Économie d'ingrédients", "Les artisans économisent 15 % de leurs ingrédients lorsqu'ils fabriquent des objets dans la zone de Bonta .");

        Assert.False(AlmanaxProfessions.Matches(crafters, "butin"));
        Assert.True(AlmanaxProfessions.Matches(With("Butin", "Les chances d'obtenir du butin sont augmentées."), "butin"));
    }

    [Theory]
    [InlineData("miner", "Plenty of ore", "The quantity of Silver harvested by miners is increased by 150%.")]
    [InlineData("miner", "Saved ingredients", "Harvesters save 15% of their ingredients when they craft items in the Lonesome Pine Trails area.")]
    [InlineData("miner", "Profession experience", "Experience gains are increased by 50% for all harvesting professions .")]
    [InlineData("minero", "Abundancia de minerales", "La cantidad de plata que recolecten los mineros aumentará en un 150%.")]
    [InlineData("minero", "Economía de ingredientes", "Los artesanos ahorran un 15% de ingredientes cuando fabrican objetos en la zona de Sufokia .")]
    [InlineData("lenador", "Abundancia de madera", "Los leñadores conseguirán un 150% más de madera de fresno .")]
    [InlineData("jeweller", "Saved ingredients", "Craftsmen save 15% of their ingredients when they craft items in the Brakmar area.")]
    public void Les_metiers_se_reconnaissent_en_anglais_et_en_espagnol(string query, string bonus, string detail)
    {
        Assert.True(AlmanaxProfessions.Matches(With(bonus, detail), query));
    }

    [Theory]
    [InlineData("forgeur", "Increased challenges", "Bonuses gained by completing challenges are increased by 200% in the Blacksmith Dungeon .")]
    [InlineData("forgeur", "Mayores retos", "Los bonus que se consiguen al superar un reto aumentan en un 200% en la Mazmorra de los Herreros .")]
    [InlineData("jeweller", "Profession experience", "Experience gains are increased by 50% for all craftsmen who can make consumables (baker, butcher, handyman and fishmonger).")]
    [InlineData("mineur", "Points d'expérience", "Les gains d'expérience sont augmentés de 75 % sur les Bandits et les Artisans Sombres .")]
    [InlineData("minero", "Puntos de experiencia", "La experiencia aumenta un 75% con los bandidos y los artesanos oscuros .")]
    public void Un_mot_voisin_ne_fait_pas_un_metier(string query, string bonus, string detail)
    {
        // The dungeon of the smiths is not a smithing bonus, and the
        // consumables day says "craftsmen" without being for all of them.
        Assert.False(AlmanaxProfessions.Matches(With(bonus, detail), query));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Une_recherche_vide_ne_trouve_rien(string query)
    {
        Assert.False(AlmanaxProfessions.Matches(With("Minerai abondant", "par les Mineurs"), query));
    }
}
