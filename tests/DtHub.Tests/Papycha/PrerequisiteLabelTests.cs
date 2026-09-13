using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// The site writes its prerequisites in three forms, and two of them
/// are not quest titles. Reading them as is left one prerequisite in
/// eight dangling.
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

    /// <summary>
    /// A milestone is not a quest, but the state it leaves behind.
    /// </summary>
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
    /// The form that used to cost "La légende du Chevalier de
    /// l'Automne" its continuation.
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
    /// The site no longer writes brackets, but a cached catalog can
    /// date from before: the rule stays.
    /// </summary>
    [Fact]
    public void Un_ancien_prefixe_entre_crochets_disparait()
    {
        var lu = PrerequisiteLabel.Of("[FIN] L'essentiel est dans le Lac gelé");

        Assert.Equal("L'essentiel est dans le Lac gelé", lu.Name);
        Assert.False(lu.IsSuccess);
    }

    /// <summary>
    /// A quest whose title ends with the word "succès" ("achievement")
    /// is not an achievement: only the whole phrasing counts.
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
