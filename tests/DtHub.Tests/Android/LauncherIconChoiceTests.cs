using DtHub.Core.Android;

namespace DtHub.Tests.Android;

public sealed class LauncherIconChoiceTests
{
    private static ApkEntry E(string name, long length = 1000) => new(name, length);

    /// <summary>
    /// The densest one wins: it will be scaled down, never enlarged.
    /// </summary>
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
    /// The pieces of an adaptive icon are not icons: showing a
    /// foreground alone would give a cropped image, and a background
    /// alone a plain color square.
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
    /// An application that only ships an adaptive icon in XML: there
    /// is nothing to extract, and the list stays as it is today.
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
    /// At equal density and size, the choice must not depend on the
    /// order in which the archive is read.
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

    /// <summary>
    /// The game's actual listing, reduced to its icon entries.
    /// </summary>
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
