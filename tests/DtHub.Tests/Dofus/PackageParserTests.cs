using DtHub.Core.Android;

namespace DtHub.Tests.Dofus;

public class PackageParserTests
{
    [Fact]
    public void Une_liste_de_paquets_vide_ne_leve_pas()
    {
        Assert.Empty(PackageParser.ParsePackageList(null));
        Assert.Empty(PackageParser.ParsePackageList("Error: no such command\n"));
    }

    [Fact]
    public void Les_paquets_sont_lus_dedupliques_et_tries()
    {
        const string output = """
            package:com.google.android.youtube
            package:com.ankama.dofustouch
            package:com.ankama.dofustouch
            """;

        Assert.Equal(
            ["com.ankama.dofustouch", "com.google.android.youtube"],
            PackageParser.ParsePackageList(output));
    }

    [Fact]
    public void La_forme_detaillee_avec_chemin_d_apk_est_geree()
    {
        const string output =
            "package:/data/app/~~a1b2==/com.ankama.dofustouch-x9==/base.apk=com.ankama.dofustouch\n";

        Assert.Equal(["com.ankama.dofustouch"], PackageParser.ParsePackageList(output));
    }

    [Fact]
    public void Une_ligne_de_paquet_malformee_est_ignoree()
    {
        const string output = """
            package:
            package:pas-un-paquet
            package:com.ankama.dofustouch
            """;

        Assert.Equal(["com.ankama.dofustouch"], PackageParser.ParsePackageList(output));
    }

    [Theory]
    [InlineData("com.ankama.dofustouch/.MainActivity", "com.ankama.dofustouch", "com.ankama.dofustouch.MainActivity")]
    [InlineData("com.exemple.app/com.autre.Activite", "com.exemple.app", "com.autre.Activite")]
    [InlineData("com.exemple.app/com.exemple.app.Main$Inner", "com.exemple.app", "com.exemple.app.Main$Inner")]
    public void Un_composant_est_lu_et_la_forme_abregee_est_developpee(
        string token, string expectedPackage, string expectedClass)
    {
        var component = PackageParser.TryParseComponent(token);

        Assert.NotNull(component);
        Assert.Equal(expectedPackage, component.PackageName);
        Assert.Equal(expectedClass, component.ClassName);
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
        Assert.Null(PackageParser.TryParseComponent(token));
    }

    [Fact]
    public void Les_composants_sont_extraits_du_format_verbeux()
    {
        const string output = """
            Activity #0: com.ankama.dofustouch/.MainActivity
            Activity #1: com.google.android.youtube/.HomeActivity
            """;

        Assert.Equal(2, PackageParser.ParseComponents(output).Count);
    }

    [Fact]
    public void Une_absence_d_activite_ne_produit_aucun_composant()
    {
        Assert.Empty(PackageParser.ParseComponents("No activity found"));
        Assert.Empty(PackageParser.ParseComponents(null));
    }

    [Fact]
    public void Un_meme_composant_repete_n_apparait_qu_une_fois()
    {
        const string output = """
            Activity #0: com.ankama.dofustouch/.MainActivity
            Activity #1: com.ankama.dofustouch/.MainActivity
            """;

        Assert.Single(PackageParser.ParseComponents(output));
    }
}
