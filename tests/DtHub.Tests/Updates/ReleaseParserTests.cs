using DtHub.Core.Updates;

namespace DtHub.Tests.Updates;

public sealed class ReleaseParserTests
{
    private const string Json = """
        {
          "tag_name": "v0.2.0",
          "draft": false,
          "prerelease": false,
          "body": "### Ajoute\n- Une chose.",
          "assets": [
            { "name": "DtHub.exe", "size": 63212298,
              "browser_download_url": "https://exemple/DtHub.exe" },
            { "name": "DtHub.exe.sha256", "size": 74,
              "browser_download_url": "https://exemple/DtHub.exe.sha256" }
          ]
        }
        """;

    [Fact]
    public void Lit_une_livraison_complete()
    {
        var release = ReleaseParser.Parse(Json, "DtHub.exe");

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("https://exemple/DtHub.exe", release.DownloadUrl);
        Assert.Equal("https://exemple/DtHub.exe.sha256", release.DigestUrl);
        Assert.Equal(63212298, release.SizeBytes);
        Assert.Contains("Une chose.", release.Notes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    public void Ecarte_un_brouillon_et_un_essai(string from, string to) =>
        Assert.Null(ReleaseParser.Parse(Json.Replace(from, to, StringComparison.Ordinal), "DtHub.exe"));

    [Fact]
    public void Ecarte_une_livraison_sans_empreinte()
    {
        // An executable that cannot be verified is not offered.
        var json = Json.Replace("DtHub.exe.sha256", "autre-chose.txt", StringComparison.Ordinal);

        Assert.Null(ReleaseParser.Parse(json, "DtHub.exe"));
    }

    [Fact]
    public void Ecarte_une_livraison_sans_executable()
    {
        var json = Json.Replace("\"name\": \"DtHub.exe\"", "\"name\": \"DtHub.zip\"", StringComparison.Ordinal);

        Assert.Null(ReleaseParser.Parse(json, "DtHub.exe"));
    }

    [Fact]
    public void Ecarte_une_etiquette_qui_ne_porte_pas_de_version() =>
        Assert.Null(ReleaseParser.Parse(
            Json.Replace("v0.2.0", "derniere", StringComparison.Ordinal), "DtHub.exe"));

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("V1.4.12", 1, 4, 12)]
    [InlineData("2.0", 2, 0, 0)]
    public void Lit_la_version_de_l_etiquette(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), ReleaseParser.VersionOf(tag));

    [Fact]
    public void Ramene_une_version_a_trois_nombres()
    {
        // "0.2.0" from the repository and "0.2.0.0" from the assembly
        // must compare equal, or the application will believe itself
        // behind its own version.
        Assert.Equal(
            ReleaseParser.Normalize(new Version(0, 2, 0, 0)),
            ReleaseParser.Normalize(new Version(0, 2, 0)));
    }

    [Fact]
    public void Ne_s_etrangle_pas_sur_un_document_illisible()
    {
        Assert.Null(ReleaseParser.Parse("ceci n'est pas du json", "DtHub.exe"));
        Assert.Null(ReleaseParser.Parse(null, "DtHub.exe"));
        Assert.Null(ReleaseParser.Parse("[]", "DtHub.exe"));
    }
}
