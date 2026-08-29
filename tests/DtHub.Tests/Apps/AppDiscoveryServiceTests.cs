using DtHub.Core.Adb;
using DtHub.Core.Apps;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Storage;
using DtHub.Tests.Fakes;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Apps;

public sealed class AppDiscoveryServiceTests : IDisposable
{
    private const string QueryActivities = """
        Activity #0: com.android.settings/.Settings
        Activity #1: com.google.android.youtube/.HomeActivity
        Activity #2: com.ankama.dofustouch/.MainActivity
        """;

    private const string SystemPackages = """
        package:com.android.settings
        package:com.google.android.youtube
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-apps-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<AppCatalogDocument> _store;

    public AppDiscoveryServiceTests()
    {
        _store = new JsonDocumentStore<AppCatalogDocument>(
            Path.Combine(_directory, "apps.json"), NullLogger.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private AppDiscoveryService Service(FakeAdbClient adb, IAppLabelProvider? labels = null) =>
        new(adb, labels ?? NullAppLabelProvider.Instance, _store);

    private static FakeAdbClient Adb() => new FakeAdbClient()
        .WithShell("query-activities", QueryActivities)
        .WithShell("pm list packages --user 0 -s", SystemPackages);

    [Fact]
    public async Task Les_applications_lancables_sont_listees_avec_leur_composant()
    {
        using var service = Service(Adb());

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.Equal(3, apps.Count);
        Assert.All(apps, app => Assert.True(app.IsLaunchable));
        Assert.Equal(
            "com.android.settings/com.android.settings.Settings",
            apps.Single(a => a.PackageName == "com.android.settings").LaunchComponent);
    }

    [Fact]
    public async Task Les_applications_systeme_sont_marquees()
    {
        using var service = Service(Adb());

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.True(apps.Single(a => a.PackageName == "com.android.settings").IsSystem);
        Assert.False(apps.Single(a => a.PackageName == "com.ankama.dofustouch").IsSystem);
    }

    [Fact]
    public async Task L_utilisateur_android_est_transmis_a_chaque_commande()
    {
        var adb = new FakeAdbClient()
            .WithShell("query-activities", QueryActivities)
            .WithShell("pm list packages", SystemPackages);

        using var service = Service(adb);
        await service.GetAppsAsync("MATERIEL123", "USB0001", 999, false, CancellationToken.None);

        Assert.All(adb.ShellCalls, call => Assert.Contains("--user 999", call, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Le_meme_paquet_sur_deux_profils_donne_deux_entrees_distinctes()
    {
        var adb = new FakeAdbClient()
            .WithShell("query-activities", QueryActivities)
            .WithShell("pm list packages", SystemPackages);

        using var service = Service(adb);

        var principal = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);
        var clone = await service.GetAppsAsync("MATERIEL123", "USB0001", 999, false, CancellationToken.None);

        var a = principal.Single(x => x.PackageName == "com.ankama.dofustouch");
        var b = clone.Single(x => x.PackageName == "com.ankama.dofustouch");

        Assert.Equal(0, a.UserId);
        Assert.Equal(999, b.UserId);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task Les_vrais_noms_sont_appliques_quand_ils_sont_disponibles()
    {
        var labels = new FakeAppLabelProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["com.google.android.youtube"] = "YouTube",
        });

        using var service = Service(Adb(), labels);

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.Equal("YouTube", apps.Single(a => a.PackageName == "com.google.android.youtube").DisplayName);

        // Sans nom connu, le repli reste lisible.
        Assert.Equal("Dofustouch", apps.Single(a => a.PackageName == "com.ankama.dofustouch").DisplayName);
    }

    [Fact]
    public async Task Un_fournisseur_de_noms_en_panne_ne_fait_pas_perdre_la_liste()
    {
        using var service = Service(Adb(), new FakeAppLabelProvider(throwOnUse: true));

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.Equal(3, apps.Count);
    }

    [Fact]
    public async Task Le_second_appel_lit_le_cache_sans_rebalayer_le_telephone()
    {
        var adb = Adb();
        using var service = Service(adb);

        await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);
        var callsAfterFirst = adb.ShellCalls.Count;

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.Equal(3, apps.Count);
        Assert.Equal(callsAfterFirst, adb.ShellCalls.Count);
    }

    [Fact]
    public async Task Un_rafraichissement_demande_rebalaye()
    {
        var adb = Adb();
        using var service = Service(adb);

        await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);
        var callsAfterFirst = adb.ShellCalls.Count;

        await service.GetAppsAsync("MATERIEL123", "USB0001", 0, true, CancellationToken.None);

        Assert.True(adb.ShellCalls.Count > callsAfterFirst);
    }

    [Fact]
    public async Task La_date_du_dernier_balayage_est_conservee()
    {
        using var service = Service(Adb());

        Assert.Null(await service.GetLastScanAsync("MATERIEL123", 0, CancellationToken.None));

        await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        Assert.NotNull(await service.GetLastScanAsync("MATERIEL123", 0, CancellationToken.None));
    }

    [Fact]
    public async Task Oublier_un_appareil_vide_son_catalogue()
    {
        using var service = Service(Adb());

        await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);
        await service.ForgetAsync("MATERIEL123", CancellationToken.None);

        Assert.Null(await service.GetLastScanAsync("MATERIEL123", 0, CancellationToken.None));
    }

    [Fact]
    public async Task Un_appareil_sans_interrogation_groupee_passe_par_le_repli_paquet_par_paquet()
    {
        // Android ancien : query-activities échoue, on liste puis on résout.
        var adb = new FakeAdbClient()
            .FailShell("query-activities", AdbErrorKind.Unknown)
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity")
            .WithShell("pm list packages --user 0 -s", "package:com.android.settings")
            .WithShell("pm list packages --user 0", "package:com.ankama.dofustouch");

        using var service = Service(adb);

        var apps = await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None);

        var app = Assert.Single(apps);
        Assert.Equal("com.ankama.dofustouch", app.PackageName);
        Assert.True(app.IsLaunchable);
    }

    [Fact]
    public async Task Un_telephone_totalement_muet_rend_une_liste_vide_sans_lever()
    {
        var adb = new FakeAdbClient()
            .FailShell("query-activities", AdbErrorKind.DeviceOffline)
            .FailShell("pm list packages", AdbErrorKind.DeviceOffline);

        using var service = Service(adb);

        Assert.Empty(await service.GetAppsAsync("MATERIEL123", "USB0001", 0, false, CancellationToken.None));
    }

    [Fact]
    public async Task Le_composant_de_lancement_est_resolu_a_la_demande()
    {
        var adb = new FakeAdbClient().WithShell("resolve-activity", "com.exemple.app/.Main");
        using var service = Service(adb);

        var component = await service.ResolveLaunchComponentAsync(
            "USB0001", 999, "com.exemple.app", CancellationToken.None);

        Assert.NotNull(component);
        Assert.Equal("com.exemple.app/com.exemple.app.Main", component.Value);
        Assert.Contains("--user 999", adb.ShellCalls[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_paquet_disparu_ne_resout_aucun_composant()
    {
        var adb = new FakeAdbClient().WithShell("resolve-activity", "No activity found");
        using var service = Service(adb);

        Assert.Null(await service.ResolveLaunchComponentAsync(
            "USB0001", 0, "com.exemple.disparu", CancellationToken.None));
    }
}
