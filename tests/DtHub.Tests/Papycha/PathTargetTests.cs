using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

public sealed class PathTargetTests
{
    /// <summary>
    /// Les donjons que les chemins relevés peuvent nommer. Tirés du catalogue
    /// réel : ce sont eux qui font ou défont chaque rapprochement.
    /// </summary>
    private static readonly string[] Donjons =
    [
        "Canopée du Kimbo",
        "Château du Wa Wabbit",
        "Terrier du Wa Wabbit",
        "Tanière Givrefoux",
        "Repaire de Skeunk",
        "Caverne du Koulosse",
        "Donjon des Dragoeufs",
        "Mine de Sakaï",
        "Arche d'Otomaï",
        "Donjon du Minotoror",
        "Le Chouque",
        "Moon",
    ];

    private static PathSide Cote(string titre) => PathTarget.Of(titre, Donjons);

    [Theory]
    // Les six qui mènent à un donjon, relevés sur le site.
    [InlineData("du donjon du Skeunk")]
    [InlineData("donjon du Koulosse")]
    [InlineData("Château du Wa Wabbit")]
    [InlineData("Terrier du Wa Wabbit")]
    [InlineData("Canopée du Kimbo")]
    [InlineData("Tanière Givrefoux")]
    public void Un_chemin_de_donjon_va_aux_donjons(string titre) =>
        Assert.Equal(PathSide.Dungeons, Cote(titre));

    [Theory]
    // Les quinze autres : des accès, des zaaps, des guides de trajet.
    [InlineData("Ilots de Moon")]
    [InlineData("Les souterrains d’Astrub")]
    [InlineData("Restat sur Otomaï | chemin optimisé")]
    [InlineData("Ile de Sakaï")]
    [InlineData("Aller sur l’île de Frigost")]
    [InlineData("Aller sur l’île du Minotoror")]
    [InlineData("Chouque")]
    [InlineData("Obtenir le sort Cawotte")]
    [InlineData("Zaap du Village Enseveli")]
    [InlineData("Aller sur l’Île d’Otomaï")]
    [InlineData("trouver Otomaï")]
    [InlineData("Zaap du village de la canopée et Zoth")]
    [InlineData("Aller sur l’île de Moon")]
    [InlineData("Wabbit GM")]
    [InlineData("Aller sur l’île des Wabbits")]
    public void Un_chemin_de_lieu_va_aux_quetes(string titre) =>
        Assert.Equal(PathSide.Quests, Cote(titre));

    [Fact]
    public void Un_seul_mot_commun_ne_suffit_pas()
    {
        // C'est ce qui écarte les faux : sans cette exigence, le zaap de la
        // canopée passerait pour le chemin de la Canopée du Kimbo, et l'accès à
        // l'île de Sakaï pour celui de la Mine de Sakaï.
        Assert.Equal(PathSide.Quests, Cote("Zaap du village de la canopée et Zoth"));
        Assert.Equal(PathSide.Quests, Cote("Ile de Sakaï"));
    }

    [Fact]
    public void Le_mot_donjon_tranche_a_lui_seul()
    {
        // Sans même connaître un seul donjon : le chemin dit où il mène. Le
        // pluriel compte autant, le site écrivant les deux.
        Assert.Equal(PathSide.Dungeons, PathTarget.Of("Chemin du donjon perdu", []));
        Assert.Equal(PathSide.Dungeons, PathTarget.Of("Chemin des donjons du nord", []));
    }

    [Fact]
    public void Le_mot_donjon_ne_compte_pas_comme_mot_distinctif()
    {
        // « donjon du Koulosse » et « Donjon des Dragoeufs » partageraient sinon
        // ce mot-là. Le rapprochement doit porter sur les noms propres.
        Assert.Equal(PathSide.Quests, PathTarget.Of("Le repaire des Dragoeufs", ["Donjon des Skeunks"]));
    }

    [Fact]
    public void Un_titre_vide_va_aux_quetes()
    {
        Assert.Equal(PathSide.Quests, PathTarget.Of(null, Donjons));
        Assert.Equal(PathSide.Quests, PathTarget.Of("   ", Donjons));
    }
}
