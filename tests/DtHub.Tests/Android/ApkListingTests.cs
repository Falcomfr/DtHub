using DtHub.Core.Android;

namespace DtHub.Tests.Android;

/// <summary>
/// The outputs are those captured on the reference phone, copied
/// as-is, archive path included.
/// </summary>
public sealed class ApkListingTests
{
    private const string Chemins = """
        package:/data/app/~~xuDfcEjUvE94gznBoE9mew==/com.ankama.dofustouch-sUENBipI9PD3f3XLRiFNRQ==/split_config.fr.apk
        package:/data/app/~~xuDfcEjUvE94gznBoE9mew==/com.ankama.dofustouch-sUENBipI9PD3f3XLRiFNRQ==/base.apk
        package:/data/app/~~xuDfcEjUvE94gznBoE9mew==/com.ankama.dofustouch-sUENBipI9PD3f3XLRiFNRQ==/split_config.xxhdpi.apk
        """;

    private const string Listage = """
        Archive:  /data/app/~~xuDfcEjUvE94gznBoE9mew==/com.ankama.dofustouch-sUENBipI9PD3f3XLRiFNRQ==/base.apk
          Length      Date    Time    Name
        ---------  ---------- -----   ----
              448  1981-01-01 01:01   res/mipmap-ldpi-v26/ic_launcher.xml
             5429  1981-01-01 01:01   res/mipmap-mdpi-v4/ic_launcher.png
             2439  1981-01-01 01:01   res/mipmap-hdpi-v26/ic_launcher_background.png
            17013  1981-01-01 01:01   res/mipmap-xhdpi-v4/ic_launcher.png
        ---------                     -------
            25329                     4 files
        """;

    [Fact]
    public void L_archive_de_base_vient_avant_ses_morceaux()
    {
        var paths = ApkListing.ParsePaths(Chemins);

        Assert.Equal(3, paths.Count);
        Assert.EndsWith("base.apk", paths[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Failure [not installed for 0]")]
    public void Sans_chemin_on_ne_rend_rien(string? sortie) =>
        Assert.Empty(ApkListing.ParsePaths(sortie));

    /// <summary>
    /// The header, the dashed rule lines, and the total do not
    /// have the shape of an entry: they fall away without having
    /// to be named.
    /// </summary>
    [Fact]
    public void Seules_les_entrees_sont_retenues()
    {
        var entries = ApkListing.ParseEntries(Listage);

        Assert.Equal(4, entries.Count);
        Assert.Equal(5429, entries.Single(e => e.Name.EndsWith("mdpi-v4/ic_launcher.png", StringComparison.Ordinal)).Length);
        Assert.DoesNotContain(entries, e => e.Name.Contains("files", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.Name.Contains("Name", StringComparison.Ordinal));
    }

    /// <summary>
    /// A name that contains a space runs to the end of the line.
    /// </summary>
    [Fact]
    public void Un_nom_a_espace_est_rendu_entier()
    {
        var entries = ApkListing.ParseEntries(
            "     1234  1981-01-01 01:01   res/raw/un fichier nomme ainsi.png");

        Assert.Equal("res/raw/un fichier nomme ainsi.png", entries.Single().Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unzip: cannot find or open truc.apk")]
    public void Un_listage_illisible_ne_rend_rien(string? sortie) =>
        Assert.Empty(ApkListing.ParseEntries(sortie));
}
