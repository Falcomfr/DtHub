using DtHub.Core.Updates;

namespace DtHub.Tests.Updates;

public sealed class ReleaseParserTests
{
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
    public void Lit_la_livraison_depuis_la_page_ou_mene_latest()
    {
        var release = ReleaseParser.FromTagPage(
            new Uri("https://github.com/Falcomfr/DtHub/releases/tag/v0.7.11"), "DtHub.exe");

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 7, 11), release.Version);
        Assert.Equal(
            "https://github.com/Falcomfr/DtHub/releases/download/v0.7.11/DtHub.exe",
            release.DownloadUrl);
        Assert.Equal(
            "https://github.com/Falcomfr/DtHub/releases/download/v0.7.11/DtHub.exe.sha256",
            release.DigestUrl);
        Assert.Equal(
            "https://github.com/Falcomfr/DtHub/releases/download/v0.7.11/notes.en.md",
            release.NoteUrls["en"]);
        Assert.Equal(
            "https://github.com/Falcomfr/DtHub/releases/download/v0.7.11/notes.fr.md",
            release.NoteUrls["fr"]);
    }

    [Theory]
    // No release yet: GitHub sends "latest" back to the list.
    [InlineData("https://github.com/Falcomfr/DtHub/releases")]
    [InlineData("https://github.com/Falcomfr/DtHub/releases/tag/derniere")]
    [InlineData("https://github.com/login")]
    public void Ecarte_une_page_qui_n_est_pas_celle_d_une_version(string landed) =>
        Assert.Null(ReleaseParser.FromTagPage(new Uri(landed), "DtHub.exe"));
}
