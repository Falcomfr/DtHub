using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Le site écrit ses prérequis de trois formes, et deux ne sont pas des titres
/// de quête. Les lire telles quelles laissait un prérequis sur huit sans suite.
/// </summary>
public sealed class PrerequisiteLabelTests
{
    [Theory]
    [InlineData("Dévotion aux dieux", "Dévotion aux dieux")]
    [InlineData("À un poil près", "À un poil près")]
    public void Un_titre_nu_se_rend_tel_quel(string libelle, string attendu)
    {
        var lu = PrerequisiteLabel.Of(libelle);

        Assert.Equal(attendu, lu.Name);
        Assert.False(lu.IsSuccess);
    }

    /// <summary>Un jalon n'est pas une quête, mais l'état qu'elle laisse.</summary>
    [Theory]
    [InlineData("L'essentiel est dans le Lac gelé atteint", "L'essentiel est dans le Lac gelé")]
    [InlineData("Une arrivée mouvementée atteinte", "Une arrivée mouvementée")]
    public void Un_jalon_rend_la_quete_qui_le_pose(string libelle, string attendu)
    {
        var lu = PrerequisiteLabel.Of(libelle);

        Assert.Equal(attendu, lu.Name);
        Assert.False(lu.IsSuccess);
    }

    /// <summary>
    /// La forme qui coûtait sa suite à « La légende du Chevalier de l'Automne ».
    /// </summary>
    [Theory]
    [InlineData("Succès Un nouveau départ réalisé", "Un nouveau départ")]
    [InlineData("Succes Devenir une légende realise", "Devenir une légende")]
    public void Un_succes_se_reconnait_et_se_nomme(string libelle, string attendu)
    {
        var lu = PrerequisiteLabel.Of(libelle);

        Assert.Equal(attendu, lu.Name);
        Assert.True(lu.IsSuccess);
    }

    /// <summary>
    /// Le site n'écrit plus de crochets, mais un catalogue en cache peut dater
    /// d'avant : la règle reste.
    /// </summary>
    [Fact]
    public void Un_ancien_prefixe_entre_crochets_disparait()
    {
        var lu = PrerequisiteLabel.Of("[FIN] L'essentiel est dans le Lac gelé");

        Assert.Equal("L'essentiel est dans le Lac gelé", lu.Name);
        Assert.False(lu.IsSuccess);
    }

    /// <summary>
    /// Une quête dont le titre finit par le mot « succès » n'est pas un succès :
    /// seule la tournure entière compte.
    /// </summary>
    [Fact]
    public void Le_mot_seul_ne_fait_pas_un_succes()
    {
        Assert.False(PrerequisiteLabel.Of("Succès et déboires").IsSuccess);
        Assert.Equal("Succès et déboires", PrerequisiteLabel.Of("Succès et déboires").Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_libelle_vide_ne_nomme_rien(string? libelle) =>
        Assert.Equal(string.Empty, PrerequisiteLabel.Of(libelle).Name);
}
