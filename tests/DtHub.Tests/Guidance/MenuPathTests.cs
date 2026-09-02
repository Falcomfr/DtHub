using DtHub.Core.Guidance;

namespace DtHub.Tests.Guidance;

public sealed class MenuPathTests
{
    /// <summary>Le vrai chemin d'un Xiaomi, tel que la fiche de marque l'écrit.</summary>
    [Fact]
    public void Un_chemin_se_lit_comme_une_suite_d_ecrans()
    {
        var steps = MenuPath.Steps(
            "Paramètres  ›  Applications  ›  Gérer les applications  ›  DOFUS Touch");

        Assert.Equal(
            ["Paramètres", "Applications", "Gérer les applications", "DOFUS Touch"],
            steps.Select(s => s.Label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  ›  ›  ")]
    public void Un_chemin_vide_ne_donne_aucun_ecran(string? path) =>
        Assert.Empty(MenuPath.Steps(path));

    [Fact]
    public void Un_seul_libelle_donne_un_seul_ecran() =>
        Assert.Single(MenuPath.Steps("Paramètres"));

    /// <summary>
    /// Une virgule dans un libellé n'est pas un séparateur : « À propos du
    /// téléphone, ou de la tablette » est une seule ligne de menu.
    /// </summary>
    [Fact]
    public void Une_virgule_ne_coupe_pas_un_libelle()
    {
        var steps = MenuPath.Steps("Paramètres  ›  À propos du téléphone, ou de la tablette");

        Assert.Equal(2, steps.Count);
        Assert.Equal("À propos du téléphone, ou de la tablette", steps[1].Label);
    }

    /// <summary>
    /// L'illustration doit se redessiner à l'identique : elle bougerait sinon
    /// sous les yeux de qui rouvre la fenêtre.
    /// </summary>
    [Fact]
    public void Le_dessin_est_le_meme_a_chaque_lecture()
    {
        const string Path = "Paramètres  ›  Applications  ›  DOFUS Touch  ›  Batterie";

        Assert.Equal(
            MenuPath.Steps(Path).Select(s => s.Row),
            MenuPath.Steps(Path).Select(s => s.Row));
    }

    [Fact]
    public void Chaque_ligne_surlignee_tient_dans_l_ecran() =>
        Assert.All(
            MenuPath.Steps("Paramètres  ›  Applications  ›  Batterie  ›  Sans restriction"),
            s => Assert.InRange(s.Row, 0, MenuPath.Rows - 1));

    /// <summary>
    /// Deux écrans de suite surlignés à la même hauteur donnent une image qui
    /// semble figée.
    /// </summary>
    [Fact]
    public void Deux_ecrans_voisins_ne_se_ressemblent_pas()
    {
        var steps = MenuPath.Steps("Batterie  ›  Batterie  ›  Batterie  ›  Batterie");

        for (var i = 1; i < steps.Count; i++)
        {
            Assert.NotEqual(steps[i - 1].Row, steps[i].Row);
        }
    }
}
