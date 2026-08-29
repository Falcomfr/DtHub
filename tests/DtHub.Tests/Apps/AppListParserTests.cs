using DtHub.Core.Apps;

namespace DtHub.Tests.Apps;

public class AppListParserTests
{
    [Fact]
    public void Une_liste_de_paquets_vide_ne_leve_pas()
    {
        Assert.Empty(AppListParser.ParsePackageList(null));
        Assert.Empty(AppListParser.ParsePackageList("Error: no such command\n"));
    }

    [Fact]
    public void Les_paquets_sont_lus_dedupliques_et_tries()
    {
        const string output = """
            package:com.google.android.youtube
            package:com.android.settings
            package:com.android.settings
            """;

        Assert.Equal(
            ["com.android.settings", "com.google.android.youtube"],
            AppListParser.ParsePackageList(output));
    }

    [Fact]
    public void La_forme_detaillee_avec_chemin_d_apk_est_geree()
    {
        const string output =
            "package:/data/app/~~a1b2==/com.exemple.app-x9==/base.apk=com.exemple.app\n";

        Assert.Equal(["com.exemple.app"], AppListParser.ParsePackageList(output));
    }

    [Fact]
    public void Une_ligne_de_paquet_malformee_est_ignoree()
    {
        const string output = """
            package:
            package:pas-un-paquet
            package:com.exemple.app
            """;

        Assert.Equal(["com.exemple.app"], AppListParser.ParsePackageList(output));
    }

    [Theory]
    [InlineData("com.exemple.app/.MainActivity", "com.exemple.app", "com.exemple.app.MainActivity")]
    [InlineData("com.exemple.app/com.autre.Activite", "com.exemple.app", "com.autre.Activite")]
    [InlineData("com.exemple.app/com.exemple.app.Main$Inner", "com.exemple.app", "com.exemple.app.Main$Inner")]
    public void Un_composant_est_lu_et_la_forme_abregee_est_developpee(
        string token, string expectedPackage, string expectedClass)
    {
        var component = AppListParser.TryParseComponent(token);

        Assert.NotNull(component);
        Assert.Equal(expectedPackage, component.PackageName);
        Assert.Equal(expectedClass, component.ClassName);
        Assert.Equal($"{expectedPackage}/{expectedClass}", component.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pas-un-composant")]
    [InlineData("com.exemple.app")]
    [InlineData("/ActiviteSansPaquet")]
    [InlineData("com.exemple.app/")]
    [InlineData("a/b/c")]
    public void Un_jeton_qui_n_est_pas_un_composant_est_rejete(string? token)
    {
        Assert.Null(AppListParser.TryParseComponent(token));
    }

    [Fact]
    public void Les_composants_sont_extraits_du_format_verbeux_de_query_activities()
    {
        const string output = """
            Activity #0: com.android.settings/.Settings
            Activity #1: com.google.android.youtube/com.google.android.apps.youtube.app.WatchWhileActivity
            """;

        var components = AppListParser.ParseComponents(output);

        Assert.Equal(2, components.Count);
        Assert.Equal("com.android.settings/com.android.settings.Settings", components[0].Value);
    }

    [Fact]
    public void Les_composants_sont_aussi_extraits_du_format_nu()
    {
        const string output = """
            com.android.settings/.Settings
            com.google.android.youtube/.HomeActivity
            """;

        Assert.Equal(2, AppListParser.ParseComponents(output).Count);
    }

    [Fact]
    public void Une_absence_d_activite_ne_produit_aucun_composant()
    {
        Assert.Empty(AppListParser.ParseComponents("No activity found"));
        Assert.Empty(AppListParser.ParseComponents(null));
    }

    [Fact]
    public void Un_meme_composant_repete_n_apparait_qu_une_fois()
    {
        const string output = """
            Activity #0: com.exemple.app/.Main
            Activity #1: com.exemple.app/.Main
            """;

        Assert.Single(AppListParser.ParseComponents(output));
    }

    /// <summary>
    /// Format produit par scrcpy : « * » pour une application système,
    /// « - » pour une application utilisateur, nom aligné sur la colonne 30.
    /// </summary>
    private const string ScrcpyOutput =
        "List of apps:\n"
        + " * Paramètres                     com.android.settings\n"
        + " * YouTube                        com.google.android.youtube\n"
        + " - Firefox                        org.mozilla.firefox\n";

    [Fact]
    public void La_liste_scrcpy_donne_les_vrais_noms_d_applications()
    {
        var apps = AppListParser.ParseScrcpyAppList(ScrcpyOutput);

        Assert.Equal(3, apps.Count);
        Assert.Equal("Paramètres", apps[0].Label);
        Assert.Equal("com.android.settings", apps[0].PackageName);
        Assert.Equal("Firefox", apps[2].Label);
    }

    [Fact]
    public void Les_applications_systeme_sont_distinguees_des_autres()
    {
        var apps = AppListParser.ParseScrcpyAppList(ScrcpyOutput);

        Assert.True(apps[0].IsSystem);
        Assert.False(apps[2].IsSystem);
    }

    [Fact]
    public void Un_nom_trop_long_repousse_le_paquet_sur_la_ligne_suivante()
    {
        // Au-delà de trente caractères, scrcpy passe à la ligne et indente le
        // nom de paquet.
        const string output =
            "List of apps:\n"
            + " - Une application au nom vraiment tres long\n"
            + "                                  com.exemple.nomtreslong\n"
            + " - Firefox                        org.mozilla.firefox\n";

        var apps = AppListParser.ParseScrcpyAppList(output);

        Assert.Equal(2, apps.Count);
        Assert.Equal("Une application au nom vraiment tres long", apps[0].Label);
        Assert.Equal("com.exemple.nomtreslong", apps[0].PackageName);
        Assert.Equal("org.mozilla.firefox", apps[1].PackageName);
    }

    [Fact]
    public void Un_nom_contenant_un_espace_simple_reste_entier()
    {
        const string output =
            "List of apps:\n"
            + " - Google Play Store              com.android.vending\n";

        Assert.Equal("Google Play Store", Assert.Single(AppListParser.ParseScrcpyAppList(output)).Label);
    }

    [Fact]
    public void L_utilisateur_android_est_porte_par_chaque_entree()
    {
        var apps = AppListParser.ParseScrcpyAppList(ScrcpyOutput, userId: 999);

        Assert.All(apps, app => Assert.Equal(999, app.UserId));
    }

    [Fact]
    public void Une_sortie_scrcpy_vide_ou_bruitee_ne_leve_pas()
    {
        Assert.Empty(AppListParser.ParseScrcpyAppList(null));
        Assert.Empty(AppListParser.ParseScrcpyAppList("List of apps:\n"));
        Assert.Empty(AppListParser.ParseScrcpyAppList("ERROR: could not connect\n"));
    }

    [Fact]
    public void Sans_vrai_nom_le_paquet_donne_un_repli_lisible()
    {
        var app = new AndroidApp { PackageName = "com.google.android.youtube", UserId = 0 };

        Assert.Equal("Youtube", app.DisplayName);
        Assert.Equal("Mon app", new AndroidApp { PackageName = "com.exemple.mon_app", UserId = 0 }.DisplayName);
        Assert.Equal("(inconnu)", AndroidApp.HumanizePackageName(null));
    }

    [Fact]
    public void Le_vrai_nom_prime_sur_le_repli()
    {
        var app = new AndroidApp { PackageName = "com.google.android.youtube", UserId = 0, Label = "YouTube" };

        Assert.Equal("YouTube", app.DisplayName);
    }

    [Fact]
    public void Une_application_sans_composant_n_est_pas_lancable()
    {
        var withComponent = new AndroidApp
        {
            PackageName = "com.exemple.app",
            UserId = 0,
            LaunchComponent = "com.exemple.app/.Main",
        };

        Assert.False(new AndroidApp { PackageName = "com.exemple.app", UserId = 0 }.IsLaunchable);
        Assert.True(withComponent.IsLaunchable);
    }
}
