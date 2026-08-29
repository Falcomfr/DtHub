using DtHub.Core.Devices;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Devices;

public class DeviceReconnectServiceTests
{
    private static AndroidDevice Known(
        string id = "MATERIEL123",
        string serial = "192.168.1.25:37845",
        string? address = "192.168.1.25",
        int? port = 37845) => new()
        {
            Id = id,
            Serial = serial,
            LastKnownAddress = address,
            LastKnownPort = port,
            IsPaired = true,
        };

    [Fact]
    public async Task Un_appareil_deja_connecte_ne_declenche_aucune_tentative()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\n192.168.1.25:37845\tdevice\n",
        };

        var outcome = await new DeviceReconnectService(adb).TryReconnectAsync(Known(), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.AlreadyConnected, outcome);
        Assert.Empty(adb.ConnectAttempts);
    }

    [Fact]
    public async Task La_derniere_adresse_connue_est_essayee_en_premier()
    {
        var adb = new FakeAdbClient();
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var outcome = await new DeviceReconnectService(adb).TryReconnectAsync(Known(), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.ReconnectedToLastAddress, outcome);
        Assert.Equal("192.168.1.25:37845", Assert.Single(adb.ConnectAttempts));
    }

    [Fact]
    public async Task Un_port_change_est_retrouve_par_decouverte_mdns()
    {
        // Le port de débogage sans fil change à chaque redémarrage du téléphone.
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.25:41999
            """);
        adb.ConnectableAddresses.Add("192.168.1.25:41999");

        var outcome = await new DeviceReconnectService(adb).TryReconnectAsync(Known(), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.ReconnectedByDiscovery, outcome);
        Assert.Equal(["192.168.1.25:37845", "192.168.1.25:41999"], adb.ConnectAttempts);
    }

    [Fact]
    public async Task L_annonce_d_un_autre_telephone_n_est_pas_utilisee()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-TELEPHONE_DU_VOISIN-xx	_adb-tls-connect._tcp	10.0.0.9:41999
            """);

        var outcome = await new DeviceReconnectService(adb)
            .TryReconnectAsync(Known(address: null, port: null), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.NotFound, outcome);
        Assert.Empty(adb.ConnectAttempts);
    }

    [Fact]
    public async Task Un_appareil_introuvable_est_rapporte_sans_lever()
    {
        var outcome = await new DeviceReconnectService(new FakeAdbClient())
            .TryReconnectAsync(Known(), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task Seuls_les_appareils_appaires_ou_a_adresse_connue_sont_tentes()
    {
        var adb = new FakeAdbClient();
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var usbOnly = new AndroidDevice { Id = "USB0001", Serial = "USB0001" };

        var outcomes = await new DeviceReconnectService(adb)
            .TryReconnectAllAsync([Known(), usbOnly], CancellationToken.None);

        Assert.Single(outcomes);
        Assert.Equal(ReconnectOutcome.ReconnectedToLastAddress, outcomes["MATERIEL123"]);
    }

    [Fact]
    public async Task Un_appareil_en_echec_n_interrompt_pas_la_reprise_des_autres()
    {
        var adb = new FakeAdbClient();
        adb.ConnectableAddresses.Add("192.168.1.31:37845");

        var outcomes = await new DeviceReconnectService(adb).TryReconnectAllAsync(
            [Known(), Known("MATERIEL456", "192.168.1.31:37845", "192.168.1.31")],
            CancellationToken.None);

        Assert.Equal(ReconnectOutcome.NotFound, outcomes["MATERIEL123"]);
        Assert.Equal(ReconnectOutcome.ReconnectedToLastAddress, outcomes["MATERIEL456"]);
    }
}
