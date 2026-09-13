using DtHub.Core.Android;
using DtHub.Infrastructure.Android;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Android;

/// <summary>
/// Le fournisseur d'icônes, sans téléphone : le faux client ADB rejoue les
/// sorties relevées sur le vrai.
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

        // La plus dense, et l'archive elle-même n'a jamais été transférée.
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
    /// Deux profils du même téléphone au même instant : le balayage passe
    /// toutes les trois secondes, et une extraction par ligne le doublerait.
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
    /// Une application qui ne livre qu'une icône adaptative : rien à extraire,
    /// et l'on ne redemande pas, le téléphone ne changera pas d'avis.
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
    /// Ce que rend un « unzip » qui n'a pas trouvé l'entrée : du texte sur la
    /// sortie standard. L'écrire dans un fichier nommé « .png » donnerait une
    /// image que l'affichage refuserait ensuite sans un mot.
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
    /// L'identité d'un appareil sans fil qui n'a jamais répondu est son
    /// adresse, dont les deux-points sont interdits dans un nom de fichier.
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

    /// <summary>Deux appareils voisins ne doivent pas se confondre après nettoyage du nom.</summary>
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
