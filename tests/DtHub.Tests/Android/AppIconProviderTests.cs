using DtHub.Core.Android;
using DtHub.Infrastructure.Android;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Android;

/// <summary>
/// The icon provider, without a phone: the fake ADB client replays the
/// outputs captured on the real one.
/// </summary>
public sealed class AppIconProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dthub-icones-" + Guid.NewGuid().ToString("N"));

    private readonly FakeAppPaths _paths;

    public AppIconProviderTests()
    {
        _paths = new FakeAppPaths(_root);
        _paths.EnsureCreated();
    }

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];

    private const string Chemins =
        "package:/data/app/~~abc==/com.ankama.dofustouch-def==/base.apk";

    private const string Listage = """
        Archive:  base.apk
          Length      Date    Time    Name
        ---------  ---------- -----   ----
            51018  1981-01-01 01:01   res/mipmap-xxxhdpi-v4/ic_launcher.png
            17013  1981-01-01 01:01   res/mipmap-xhdpi-v4/ic_launcher.png
        """;

    private static AppIconRequest Demande(int userId = 0) =>
        new("MATERIEL123", "192.168.1.14:40187", userId, "com.ankama.dofustouch");

    private static FakeAdbClient Sain() =>
        new FakeAdbClient()
            .WithShell("pm path", Chemins)
            .WithShell("unzip -l", Listage)
            .WithExecOut("unzip -p", Png);

    private AppIconProvider Provider(FakeAdbClient adb) =>
        new(adb, _paths, NullLogger<AppIconProvider>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task L_icone_est_extraite_et_posee_dans_le_cache()
    {
        var adb = Sain();

        var path = await Provider(adb).GetAsync(Demande(), CancellationToken.None);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.Equal(Png, await File.ReadAllBytesAsync(path, CancellationToken.None));

        // The densest one, and the archive itself was never transferred.
        Assert.Contains(adb.ExecOutCalls, c => c.Contains("xxxhdpi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_seconde_demande_ne_parle_plus_au_telephone()
    {
        var adb = Sain();
        var provider = Provider(adb);

        await provider.GetAsync(Demande(), CancellationToken.None);

        var avant = adb.ShellCalls.Count + adb.ExecOutCalls.Count;

        Assert.NotNull(await provider.GetAsync(Demande(), CancellationToken.None));
        Assert.NotNull(provider.Find("MATERIEL123", "com.ankama.dofustouch"));
        Assert.Equal(avant, adb.ShellCalls.Count + adb.ExecOutCalls.Count);
    }

    /// <summary>
    /// Two profiles of the same phone at the same instant: the scan
    /// runs every three seconds, and one extraction per line would
    /// double it.
    /// </summary>
    [Fact]
    public async Task Des_demandes_simultanees_n_extraient_qu_une_fois()
    {
        var adb = Sain();
        var provider = Provider(adb);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => provider.GetAsync(Demande(), CancellationToken.None)));

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Single(adb.ExecOutCalls);
    }

    [Fact]
    public async Task Sans_unzip_sur_le_telephone_rien_ne_leve()
    {
        var adb = new FakeAdbClient().WithShell("pm path", Chemins);

        Assert.Null(await Provider(adb).GetAsync(Demande(), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_paths.CacheDirectory, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Un_paquet_absent_n_ouvre_meme_pas_l_archive()
    {
        var adb = new FakeAdbClient().WithShell("pm path", string.Empty);

        Assert.Null(await Provider(adb).GetAsync(Demande(999), CancellationToken.None));
        Assert.DoesNotContain(adb.ShellCalls, c => c.Contains("unzip", StringComparison.Ordinal));
    }

    /// <summary>
    /// An application that only ships an adaptive icon: nothing to
    /// extract, and we do not ask again, the phone will not change its
    /// mind.
    /// </summary>
    [Fact]
    public async Task Sans_image_matricielle_on_ne_redemande_pas()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm path", Chemins)
            .WithShell("unzip -l", """
                  448  1981-01-01 01:01   res/mipmap-anydpi-v26/ic_launcher.xml
                """);

        var provider = Provider(adb);

        Assert.Null(await provider.GetAsync(Demande(), CancellationToken.None));

        var avant = adb.ShellCalls.Count;

        Assert.Null(await provider.GetAsync(Demande(), CancellationToken.None));
        Assert.Equal(avant, adb.ShellCalls.Count);
    }

    /// <summary>
    /// What "unzip" returns when it has not found the entry: text on
    /// standard output. Writing it to a file named ".png" would give
    /// an image that the viewer would then refuse without a word.
    /// </summary>
    [Fact]
    public async Task Une_sortie_qui_n_est_pas_une_image_est_refusee()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm path", Chemins)
            .WithShell("unzip -l", Listage)
            .WithExecOut("unzip -p", "unzip: cannot find entry"u8.ToArray());

        Assert.Null(await Provider(adb).GetAsync(Demande(), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_paths.CacheDirectory, "*.png", SearchOption.AllDirectories));
    }

    /// <summary>
    /// The identity of a wireless device that has never answered is
    /// its address, whose colons are forbidden in a file name.
    /// </summary>
    [Fact]
    public async Task Une_identite_avec_deux_points_donne_un_nom_de_fichier_legal()
    {
        var path = await Provider(Sain()).GetAsync(
            new AppIconRequest("adb:192.168.1.25:5555", "192.168.1.25:5555", 0, "com.ankama.dofustouch"),
            CancellationToken.None);

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.DoesNotContain(':', Path.GetFileName(path));
    }

    /// <summary>
    /// Two neighboring devices must not be confused after the name is
    /// sanitized.
    /// </summary>
    [Fact]
    public async Task Deux_appareils_voisins_ne_partagent_pas_leur_icone()
    {
        var provider = Provider(Sain());

        var premier = await provider.GetAsync(
            new AppIconRequest("1.2.3.4:5555", "s", 0, "p"), CancellationToken.None);

        var second = await provider.GetAsync(
            new AppIconRequest("1.2.3.4_5555", "s", 0, "p"), CancellationToken.None);

        Assert.NotEqual(premier, second);
    }

    [Theory]
    [InlineData("", "s", "p")]
    [InlineData("d", "", "p")]
    [InlineData("d", "s", "")]
    public async Task Une_demande_incomplete_ne_parle_pas_au_telephone(
        string deviceId,
        string serial,
        string package)
    {
        var adb = Sain();

        Assert.Null(await Provider(adb).GetAsync(
            new AppIconRequest(deviceId, serial, 0, package), CancellationToken.None));

        Assert.Empty(adb.ShellCalls);
    }
}
