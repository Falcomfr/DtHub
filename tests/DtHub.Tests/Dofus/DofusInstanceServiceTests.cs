using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Dofus;

public class DofusInstanceServiceTests
{
    /// <summary>Sortie relevée sur le téléphone de référence, un Xiaomi 13T.</summary>
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

        // Sans profils lisibles, le service retombe sur l'utilisateur
        // principal, qui n'a pas le jeu selon ADB : aucune instance.
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

    /// <summary>Faux ADB où le jeu n'est installé que sur le profil 999.</summary>
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
}
