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
    public async Task Une_adresse_qui_ne_repond_pas_ne_retient_pas_la_liste()
    {
        // Mesuré sur le vrai ADB : un téléphone éteint laisse le système
        // attendre vingt-deux secondes avant de rendre la main, et la liste
        // des appareils attendait avec lui. L'échéance rend la main, et le
        // balayage mDNS prend le relais, lui qui sait retrouver un appareil
        // dont le port a changé.
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.25:41999
            """);
        adb.SilentAddresses.Add("192.168.1.25:37845");
        adb.ConnectableAddresses.Add("192.168.1.25:41999");

        var service = new DeviceReconnectService(adb, TimeSpan.FromMilliseconds(60));

        var outcome = await service.TryReconnectAsync(Known(), CancellationToken.None);

        Assert.Equal(ReconnectOutcome.ReconnectedByDiscovery, outcome);
    }

    [Fact]
    public async Task L_arret_demande_par_l_utilisateur_n_est_pas_confondu_avec_l_echeance()
    {
        // L'échéance se rattrape, l'arrêt demandé non : les confondre ferait
        // continuer un balayage que quelqu'un vient d'annuler.
        var adb = new FakeAdbClient();

        adb.SilentAddresses.Add("192.168.1.25:37845");

        using var stop = new CancellationTokenSource();

        stop.CancelAfter(TimeSpan.FromMilliseconds(60));

        var service = new DeviceReconnectService(adb, TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.TryReconnectAsync(Known(), stop.Token));
    }

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
    public async Task Tous_les_appareils_connus_sont_tentes()
    {
        // Le filtre par drapeau était trop strict : un appareil mémorisé par
        // une version antérieure n'a pas forcément l'information, alors qu'il
        // est parfaitement joignable.
        var adb = new FakeAdbClient();
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var withoutHistory = new AndroidDevice { Id = "AUTRE", Serial = "AUTRE" };

        var outcomes = await new DeviceReconnectService(adb)
            .TryReconnectAllAsync([Known(), withoutHistory], CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(ReconnectOutcome.ReconnectedToLastAddress, outcomes["MATERIEL123"]);
        Assert.Equal(ReconnectOutcome.NotFound, outcomes["AUTRE"]);
    }

    [Fact]
    public async Task Un_appareil_sans_adresse_memorisee_est_retrouve_par_son_annonce_reseau()
    {
        // Cas réel : le téléphone était connecté sous son nom de service mDNS,
        // sans adresse exploitable, et se réannonce après une coupure.
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-SERIAL0123456789-1V3FXQ	_adb-tls-connect._tcp	192.168.1.16:33805
            """);
        adb.ConnectableAddresses.Add("192.168.1.16:33805");

        var device = new AndroidDevice
        {
            Id = "SERIAL0123456789",
            Serial = "adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp",
        };

        var outcome = await new DeviceReconnectService(adb).TryReconnectAsync(device, CancellationToken.None);

        Assert.Equal(ReconnectOutcome.ReconnectedByDiscovery, outcome);
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
