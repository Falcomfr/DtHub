using DtHub.Core.Android;

namespace DtHub.Tests.Android;

public sealed class LauncherIconChoiceTests
{
    private static ApkEntry E(string name, long length = 1000) => new(name, length);

    /// <summary>La plus dense l'emporte : elle sera réduite, jamais agrandie.</summary>
    [Fact]
    public void La_plus_dense_est_choisie()
    {
        var choix = LauncherIconChoice.Choose(
        [
            E("res/mipmap-mdpi-v4/ic_launcher.png", 5429),
            E("res/mipmap-xxxhdpi-v4/ic_launcher.png", 51018),
            E("res/mipmap-xhdpi-v4/ic_launcher.png", 17013),
        ]);

        Assert.Equal("res/mipmap-xxxhdpi-v4/ic_launcher.png", choix);
    }

    /// <summary>
    /// Les morceaux d'une icône adaptative ne sont pas des icônes : montrer un
    /// avant-plan seul donnerait une image tronquée, et un arrière-plan seul
    /// un carré de couleur.
    /// </summary>
    [Fact]
    public void Les_morceaux_d_une_icone_adaptative_sont_ecartes()
    {
        var choix = LauncherIconChoice.Choose(
        [
            E("res/mipmap-xxxhdpi-v26/ic_launcher_foreground.png", 42012),
            E("res/mipmap-xxhdpi-v26/ic_launcher_background.png", 26607),
            E("res/mipmap-mdpi-v4/ic_launcher.png", 5429),
        ]);

        Assert.Equal("res/mipmap-mdpi-v4/ic_launcher.png", choix);
    }

    [Fact]
    public void La_ronde_sert_de_repli_quand_l_entiere_manque()
    {
        var choix = LauncherIconChoice.Choose(
        [
            E("res/mipmap-xhdpi-v4/ic_launcher_round.png", 12000),
            E("res/mipmap-ldpi-v26/ic_launcher_foreground.png", 2796),
        ]);

        Assert.Equal("res/mipmap-xhdpi-v4/ic_launcher_round.png", choix);
    }

    /// <summary>
    /// Une application qui ne livre qu'une icône adaptative en XML : rien à
    /// extraire, et la liste reste celle d'aujourd'hui.
    /// </summary>
    [Fact]
    public void Sans_image_matricielle_on_ne_rend_rien() =>
        Assert.Null(LauncherIconChoice.Choose(
        [
            E("res/mipmap-ldpi-v26/ic_launcher.xml", 448),
            E("res/mipmap-anydpi-v26/ic_launcher.xml", 448),
        ]));

    [Fact]
    public void Une_image_trop_lourde_est_refusee() =>
        Assert.Null(LauncherIconChoice.Choose(
            [E("res/mipmap-xxxhdpi-v4/ic_launcher.png", LauncherIconChoice.MaximumBytes + 1)]));

    [Fact]
    public void Une_entree_vide_est_refusee() =>
        Assert.Null(LauncherIconChoice.Choose([E("res/mipmap-hdpi-v4/ic_launcher.png", 0)]));

    [Fact]
    public void Une_archive_sans_icone_ne_rend_rien() =>
        Assert.Null(LauncherIconChoice.Choose(
            [E("res/drawable/notify_panel_notification_icon_bg.png", 138)]));

    [Fact]
    public void Un_listage_vide_ne_rend_rien() => Assert.Null(LauncherIconChoice.Choose([]));

    /// <summary>
    /// À densité et taille égales, le choix ne doit pas dépendre de l'ordre de
    /// lecture de l'archive.
    /// </summary>
    [Fact]
    public void Le_choix_ne_depend_pas_de_l_ordre()
    {
        ApkEntry[] entrees =
        [
            E("res/mipmap-hdpi-v4/ic_launcher.png", 1000),
            E("res/drawable-hdpi-v4/ic_launcher.png", 1000),
        ];

        Assert.Equal(
            LauncherIconChoice.Choose(entrees),
            LauncherIconChoice.Choose([.. entrees.Reverse()]));
    }

    /// <summary>Le vrai listage du jeu, réduit à ses entrées d'icône.</summary>
    [Fact]
    public void Sur_le_jeu_visé_c_est_la_plus_dense_des_entieres()
    {
        var choix = LauncherIconChoice.Choose(ApkListing.ParseEntries("""
                448  1981-01-01 01:01   res/mipmap-ldpi-v26/ic_launcher.xml
               5429  1981-01-01 01:01   res/mipmap-mdpi-v4/ic_launcher.png
               2858  1981-01-01 01:01   res/mipmap-mdpi-v4/ic_launcher_round.png
              17013  1981-01-01 01:01   res/mipmap-xhdpi-v4/ic_launcher.png
              32355  1981-01-01 01:01   res/mipmap-xxhdpi-v4/ic_launcher.png
              51018  1981-01-01 01:01   res/mipmap-xxxhdpi-v4/ic_launcher.png
              42012  1981-01-01 01:01   res/mipmap-xxxhdpi-v26/ic_launcher_foreground.png
            """));

        Assert.Equal("res/mipmap-xxxhdpi-v4/ic_launcher.png", choix);
    }
}
