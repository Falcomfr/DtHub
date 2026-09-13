using DtHub.Core.Guidance;

namespace DtHub.Tests.Guidance;

public sealed class MenuPathTests
{
    /// <summary>
    /// Le vrai chemin d'un Xiaomi. Quatre segments donnent trois écrans : on
    /// est dans « Paramètres » et l'on y touche « Applications », et ainsi de
    /// suite. Le dernier segment est la ligne du dernier écran, non un écran
    /// vide de plus.
    /// </summary>
    [Fact]
    public void Un_chemin_donne_un_ecran_de_moins_que_de_segments()
    {
        var screens = MenuPath.Screens(
            "Paramètres  ›  Applications  ›  Gérer les applications  ›  DOFUS Touch");

        Assert.Equal(
            ["Paramètres", "Applications", "Gérer les applications"],
            screens.Select(s => s.Title));

        Assert.Equal(
            ["Applications", "Gérer les applications", "DOFUS Touch"],
            screens.Select(s => s.Tap));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  ›  ›  ")]
    public void Un_chemin_vide_ne_donne_aucun_ecran(string? path) =>
        Assert.Empty(MenuPath.Screens(path));

    /// <summary>
    /// Un seul segment : l'écran où se rendre, sans ligne à toucher. Inventer
    /// une ligne montrerait quelque chose que la fiche ne dit pas.
    /// </summary>
    [Fact]
    public void Un_seul_libelle_donne_un_ecran_sans_ligne()
    {
        var screen = Assert.Single(MenuPath.Screens("Paramètres"));

        Assert.Equal("Paramètres", screen.Title);
        Assert.Empty(screen.Tap);
    }

    /// <summary>
    /// Une virgule dans un libellé n'est pas un séparateur : « À propos du
    /// téléphone, ou de la tablette » est une seule ligne de menu.
    /// </summary>
    [Fact]
    public void Une_virgule_ne_coupe_pas_un_libelle()
    {
        var screen = Assert.Single(
            MenuPath.Screens("Paramètres  ›  À propos du téléphone, ou de la tablette"));

        Assert.Equal("Paramètres", screen.Title);
        Assert.Equal("À propos du téléphone, ou de la tablette", screen.Tap);
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
            MenuPath.Screens(Path).Select(s => s.Row),
            MenuPath.Screens(Path).Select(s => s.Row));
    }

    [Fact]
    public void Chaque_ligne_tient_dans_l_ecran() =>
        Assert.All(
            MenuPath.Screens("Paramètres  ›  Applications  ›  Batterie  ›  Sans restriction"),
            s => Assert.InRange(s.Row, 0, MenuPath.Rows - 1));

    /// <summary>
    /// Deux écrans de suite dont la ligne est à la même hauteur donnent une
    /// image qui semble figée.
    /// </summary>
    [Fact]
    public void Deux_ecrans_voisins_ne_se_ressemblent_pas()
    {
        var screens = MenuPath.Screens("A  ›  Batterie  ›  Batterie  ›  Batterie");

        for (var i = 1; i < screens.Count; i++)
        {
            Assert.NotEqual(screens[i - 1].Row, screens[i].Row);
        }
    }

    /// <summary>
    /// La hauteur suit la ligne à toucher, et non l'écran : c'est elle qu'on
    /// cherche des yeux, et elle doit se retrouver au même endroit d'une fiche
    /// à l'autre.
    /// </summary>
    [Fact]
    public void La_hauteur_suit_la_ligne_et_non_l_ecran()
    {
        var premier = MenuPath.Screens("Paramètres  ›  Batterie");
        var second = MenuPath.Screens("Applications  ›  Batterie");

        Assert.Equal(premier[0].Row, second[0].Row);
    }

    /// <summary>
    /// Un dessin qui bouge à chaque ouverture serait pire que des barres
    /// toutes égales : le même écran doit se rendre pareil.
    /// </summary>
    [Fact]
    public void L_habillage_ne_bouge_pas_d_une_lecture_a_l_autre()
    {
        for (var rang = 0; rang < MenuPath.Rows; rang++)
        {
            Assert.Equal(MenuPath.Decor("Paramètres", rang), MenuPath.Decor("Paramètres", rang));
        }
    }

    [Fact]
    public void La_longueur_des_barres_reste_lisible_comme_une_liste()
    {
        for (var rang = 0; rang < MenuPath.Rows; rang++)
        {
            Assert.InRange(MenuPath.Decor("Applications", rang).BarShare, 0.5, 1.0);
        }
    }

    [Fact]
    public void La_teinte_reste_dans_la_palette()
    {
        for (var rang = 0; rang < MenuPath.Rows; rang++)
        {
            Assert.InRange(MenuPath.Decor("Applications", rang).Tint, 0, MenuPath.Tints - 1);
        }
    }

    /// <summary>
    /// Cinq lignes rigoureusement identiques se verraient : ni les longueurs
    /// ni les teintes ne le sont.
    /// </summary>
    [Fact]
    public void Les_lignes_d_un_meme_ecran_ne_se_ressemblent_pas_toutes()
    {
        var habillages = Enumerable
            .Range(0, MenuPath.Rows)
            .Select(rang => MenuPath.Decor("Paramètres supplémentaires", rang))
            .ToList();

        Assert.True(habillages.Select(d => d.BarShare).Distinct().Count() > 1, "longueurs égales");
        Assert.True(habillages.Select(d => d.Tint).Distinct().Count() > 1, "teintes égales");
    }

    /// <summary>
    /// Un écran de navigation ne porte pas quatre bascules. Il en porte une,
    /// exactement : assez pour que la liste ressemble à des réglages, pas assez
    /// pour qu'elle ressemble à un tableau de bord.
    /// </summary>
    [Theory]
    [InlineData("Paramètres")]
    [InlineData("Applications")]
    [InlineData("Système")]
    [InlineData("Batterie")]
    [InlineData("Paramètres supplémentaires")]
    [InlineData("Gérer les applications")]
    [InlineData("DOFUS Touch")]
    public void Un_ecran_ne_porte_qu_un_seul_interrupteur(string ecran)
    {
        var interrupteurs = Enumerable
            .Range(0, MenuPath.Rows)
            .Count(rang => MenuPath.Decor(ecran, rang).HasSwitch);

        Assert.Equal(1, interrupteurs);
    }

    /// <summary>
    /// Ni tous les réglages ni aucun n'annoncent leur état sous leur nom.
    /// </summary>
    [Fact]
    public void Les_sous_titres_restent_minoritaires()
    {
        string[] ecrans =
        [
            "Paramètres", "Applications", "Système", "Batterie",
            "Paramètres supplémentaires", "Gérer les applications", "DOFUS Touch",
        ];

        var lignes = ecrans
            .SelectMany(nom => Enumerable.Range(0, MenuPath.Rows).Select(rang => MenuPath.Decor(nom, rang)))
            .ToList();

        Assert.InRange(lignes.Count(d => d.HasSubtitle), 1, lignes.Count / 2);
    }
}
