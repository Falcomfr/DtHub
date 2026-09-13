using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Dofus;

public class DofusInstanceServiceTests
{
    /// <summary>
    /// Output captured on the reference phone, a Xiaomi 13T.
    /// </summary>
    private const string RealUsers = """
        Users:
        	UserInfo{0:Alice Martin:4c13} running
        	UserInfo{999:XSpace:801010} running
        """;

    private static AndroidDevice Device(bool connected = true) => new()
    {
        Id = "MATERIEL123",
        Serial = "USB0001",
        State = connected ? AdbDeviceState.Device : AdbDeviceState.Offline,
        Manufacturer = "Xiaomi",
        Model = "23078PND5G",
        MarketName = "Xiaomi 13T",
    };

    private static DofusInstanceService Service(FakeAdbClient adb) =>
        new(adb, new AndroidUserService(adb));

    [Fact]
    public async Task Le_jeu_installe_sur_deux_profils_donne_deux_instances()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var instances = await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(2, instances.Count);
        Assert.Equal([0, 999], instances.Select(i => i.UserId));
        Assert.All(instances, i => Assert.Equal(
            "com.ankama.dofustouch/com.ankama.dofustouch.MainActivity", i.LaunchComponent));
    }

    [Fact]
    public async Task Chaque_instance_porte_le_nom_de_son_profil_android()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var instances = await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        // The editable name only carries the Android profile; the
        // product name is added to the window title, not here.
        Assert.Equal("Principal", instances.Single(i => i.UserId == 0).DisplayName);
        Assert.Equal("XSpace", instances.Single(i => i.UserId == 999).DisplayName);
    }

    [Fact]
    public async Task Un_profil_sans_le_jeu_ne_produit_pas_d_instance()
    {
        var adb = new StatefulAdb();

        var instances = await new DofusInstanceService(adb, new AndroidUserService(adb))
            .DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(999, Assert.Single(instances).UserId);
    }

    [Fact]
    public async Task Les_cles_d_instance_sont_distinctes_par_profil()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var instances = await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(2, instances.Select(i => i.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("MATERIEL123|999|com.ankama.dofustouch", instances.Single(i => i.UserId == 999).Key);
    }

    [Fact]
    public async Task Un_telephone_hors_ligne_n_est_pas_interroge()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch");

        var instances = await Service(adb).DiscoverAsync([Device(connected: false)], CancellationToken.None);

        Assert.Empty(instances);
        Assert.Empty(adb.ShellCalls);
    }

    [Fact]
    public async Task Un_telephone_muet_ne_fait_pas_echouer_le_balayage()
    {
        var adb = new FakeAdbClient()
            .FailShell("pm list users", AdbErrorKind.DeviceOffline)
            .FailShell("pm list packages", AdbErrorKind.DeviceOffline);

        // Without readable profiles, the service falls back to the
        // main user, who does not have the game according to ADB: no
        // instance.
        Assert.Empty(await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None));
    }

    [Fact]
    public async Task Un_paquet_absent_du_profil_est_correctement_detecte()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "");

        Assert.False(await Service(adb).IsInstalledAsync("USB0001", 0, CancellationToken.None));
    }

    [Fact]
    public async Task Le_paquet_recherche_est_reglable()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.exemple.autre")
            .WithShell("resolve-activity", "com.exemple.autre/.Main");

        var service = Service(adb);
        service.PackageName = "com.exemple.autre";

        var instances = await service.DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(2, instances.Count);
        Assert.All(instances, i => Assert.Equal("com.exemple.autre", i.PackageName));
    }

    [Fact]
    public async Task Le_profil_android_est_transmis_a_chaque_commande()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Contains(adb.ShellCalls, c => c.Contains("--user 999", StringComparison.Ordinal));
        Assert.Contains(adb.ShellCalls, c => c.Contains("--user 0", StringComparison.Ordinal));
    }

    /// <summary>
    /// Fake ADB where the game is only installed on profile 999.
    /// </summary>
    private sealed class StatefulAdb : FakeAdbClientBase
    {
        public override string Shell(string joined) => joined switch
        {
            var c when c.StartsWith("pm list users", StringComparison.Ordinal) => RealUsers,
            var c when c.Contains("--user 999", StringComparison.Ordinal)
                       && c.StartsWith("pm list packages", StringComparison.Ordinal) =>
                "package:com.ankama.dofustouch",
            var c when c.Contains("resolve-activity", StringComparison.Ordinal) =>
                "com.ankama.dofustouch/.MainActivity",
            _ => string.Empty,
        };
    }

    [Fact]
    public async Task Une_copie_installee_sous_un_nom_derive_est_trouvee_aussi()
    {
        // Per-profile cloning, the one the application targets, keeps
        // the package name intact. Some vendor overlays install their
        // copy under a derived name, which the strict comparison used
        // to make invisible even though the command had reported it
        // correctly.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell(
                "pm list packages",
                "package:com.ankama.dofustouch\npackage:com.ankama.dofustouch.clone2")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var instances = await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(4, instances.Count);
        Assert.Contains(instances, i => i.PackageName == "com.ankama.dofustouch.clone2");

        // The reference package stays first: that is the common case.
        Assert.Equal("com.ankama.dofustouch", instances[0].PackageName);
    }

    [Fact]
    public async Task Un_paquet_sans_rapport_n_est_pas_pris_pour_une_copie()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell(
                "pm list packages",
                "package:com.ankama.dofustouch\npackage:com.example.autrejeu")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var instances = await Service(adb).DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal(2, instances.Count);
        Assert.All(instances, i => Assert.Equal("com.ankama.dofustouch", i.PackageName));
    }

    [Fact]
    public async Task Une_liste_de_profils_illisible_est_dite_et_non_tue()
    {
        // Without this word, a device whose overlay restricts "pm
        // list users" returns a single instance and looks exactly
        // like a device that really has only one profile. The second
        // account seems to have vanished.
        var adb = new FakeAdbClient()
            .FailShell("pm list users")
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var service = Service(adb);
        var instances = await service.DiscoverAsync([Device()], CancellationToken.None);

        Assert.Single(instances);
        Assert.Single(service.Warnings);
        Assert.Contains("profils", service.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_liste_de_profils_lue_ne_produit_aucun_avertissement()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", RealUsers)
            .WithShell("pm list packages", "package:com.ankama.dofustouch")
            .WithShell("resolve-activity", "com.ankama.dofustouch/.MainActivity");

        var service = Service(adb);
        await service.DiscoverAsync([Device()], CancellationToken.None);

        Assert.Empty(service.Warnings);
    }

    [Fact]
    public async Task Un_profil_qui_repond_sans_le_jeu_est_releve_comme_tel()
    {
        // The record is used to forget the matching account. It must
        // only contain profiles that really answered.
        var adb = new StatefulAdb();
        var service = new DofusInstanceService(adb, new AndroidUserService(adb));

        _ = await service.DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Equal([0], service.ProfilesWithoutGame["MATERIEL123"]);
    }

    [Fact]
    public async Task Un_profil_qui_n_a_pas_su_repondre_n_est_pas_declare_vide()
    {
        // All the caution lies right there. The failure used to
        // return an empty list, indistinguishable from a response
        // saying "nothing": relying on it to erase an account would
        // have lost it at the first hiccup from ADB.
        var adb = new FakeAdbClient()
            .FailShell("pm list packages")
            .WithShell("pm list users", RealUsers);

        var service = Service(adb);

        var instances = await service.DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.Empty(instances);
        Assert.Empty(service.ProfilesWithoutGame["MATERIEL123"]);
    }

    [Fact]
    public async Task Une_liste_de_profils_illisible_ne_releve_aucun_profil_vide()
    {
        // Without the profile list, we do not even know which
        // profiles we would be talking about: the device does not
        // enter the record at all.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", "Error: could not access users")
            .WithShell("pm list packages", string.Empty);

        var service = Service(adb);

        _ = await service.DiscoverOnDeviceAsync(Device(), CancellationToken.None);

        Assert.DoesNotContain("MATERIEL123", service.ProfilesWithoutGame.Keys);
    }
}
