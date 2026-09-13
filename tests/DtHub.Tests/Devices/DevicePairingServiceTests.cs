using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Devices;

public class DevicePairingServiceTests
{
    private const string PairingAndConnect = """
        List of discovered mdns services
        adb-MATERIEL123-nJyLWZ	_adb-tls-pairing._tcp	192.168.1.25:37123
        adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.25:37845
        """;

    private const string ConnectAilleurs = """
        List of discovered mdns services
        adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.16:37845
        """;

    /// <summary>Attente instantanée : les tests ne doivent rien attendre réellement.</summary>
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static DevicePairingService Service(FakeAdbClient adb) =>
        new(adb, NoDelay)
        {
            ConnectDiscoveryTimeout = TimeSpan.FromMilliseconds(1),
            DiscoveryPollInterval = TimeSpan.Zero,
        };

    private static DevicePairingService Service(FakeAdbClient adb, IAddressProbe probe) =>
        new(adb, NoDelay, probe)
        {
            ConnectDiscoveryTimeout = TimeSpan.FromMilliseconds(1),
            DiscoveryPollInterval = TimeSpan.Zero,
        };

    [Fact]
    public async Task Une_adresse_annoncee_muette_n_est_jamais_soumise_a_l_appairage()
    {
        // Measured on two phones: "adb mdns services" gives a single address
        // to every instance it lists, and the one that came out belonged to
        // the other device. The pairing port then answered nowhere, but ADB
        // returns the same "protocol fault" as for a refused code: only a
        // probe tells the two apart.
        var adb = new FakeAdbClient();
        var probe = new FakeAddressProbe();

        var result = await Service(adb, probe)
            .PairAndConnectAsync("192.168.1.16", 43415, "123456", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.AddressUnreachable, result.Status);
        Assert.True(result.NeedsAddress);

        // The code is not spent against an address known not to answer: that
        // is what made an expired code look like the explanation.
        Assert.Empty(adb.PairAttempts);
    }

    [Fact]
    public async Task Le_port_d_appairage_est_retente_sur_les_adresses_qu_adb_connait()
    {
        // When the announcement carries another device's address, the right
        // one is sometimes already at hand: "adb devices" lists live
        // connections only, so it is the source that does not lie.
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.23:43395	device product:raphael_eea model:Mi_9T_Pro device:raphael
                """,
        };

        var probe = new FakeAddressProbe().Answering("192.168.1.23:43415");

        await Service(adb, probe)
            .PairAndConnectAsync("192.168.1.16", 43415, "123456", CancellationToken.None);

        Assert.Equal("192.168.1.23:43415", Assert.Single(adb.PairAttempts));
    }

    [Fact]
    public async Task Une_adresse_muette_ne_compte_pas_comme_un_appairage_acquis()
    {
        // "Paired" drives what follows: the window welcomes the device back
        // and waits for its connection. None of that has any place when the
        // phone never received the code.
        var adb = new FakeAdbClient();

        var result = await Service(adb, new FakeAddressProbe())
            .PairAndConnectAsync("192.168.1.16", 43415, "123456", CancellationToken.None);

        Assert.False(result.Paired);
    }

    [Fact]
    public async Task Le_port_de_connexion_annonce_est_repris_sur_l_adresse_qui_a_repondu()
    {
        // The same defect strikes after pairing: the announced port is right,
        // the host beside it belongs to another device. Comparing hosts then
        // found nothing, and the window asked for a port the network had just
        // handed over.
        var adb = new FakeAdbClient();

        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.16:37845
            """);

        adb.ConnectableAddresses.Add("192.168.1.23:37845");

        var probe = new FakeAddressProbe()
            .Answering("192.168.1.23:43415")
            .Answering("192.168.1.23:37845");

        var result = await Service(adb, probe)
            .PairAndConnectAsync("192.168.1.23", 43415, "123456", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.Connected, result.Status);
        Assert.Equal("192.168.1.23:37845", result.Address);
    }

    [Fact]
    public async Task Une_annonce_dont_l_adresse_est_muette_n_accuse_pas_l_appareil()
    {
        // A refused connection counted as proof of a broken pairing, and sent
        // the user back to type a code. Since the announced address can belong
        // to another device, it is no longer proof: as long as nothing
        // answers, there is nothing to hold against the phone.
        var adb = new FakeAdbClient();

        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.16:37845
            """);

        var result = await Service(adb, new FakeAddressProbe())
            .ConnectAnnouncedAsync([], null, CancellationToken.None);

        Assert.Empty(result.Refused);
        Assert.Empty(adb.ConnectAttempts);
    }

    [Fact]
    public async Task Une_adresse_deja_sondee_ne_l_est_pas_a_chaque_tour()
    {
        // A silent address costs the probe's whole deadline, measured at two
        // seconds against a phone that drops instead of refusing. The loop
        // polls until its own deadline, so retrying the same dead address on
        // every turn buys nothing and stretches the wait.
        var adb = new FakeAdbClient();

        for (var turn = 0; turn < 6; turn++)
        {
            adb.MdnsOutputs.Enqueue(ConnectAilleurs);
        }

        var probe = new FakeAddressProbe();

        var service = new DevicePairingService(adb, NoDelay, probe)
        {
            ConnectDiscoveryTimeout = TimeSpan.FromMilliseconds(30),
            DiscoveryPollInterval = TimeSpan.Zero,
        };

        await service.WaitForConnectServiceAsync("192.168.1.23", CancellationToken.None);

        Assert.Equal("192.168.1.23:37845", Assert.Single(probe.Probed));
    }

    [Fact]
    public async Task Un_appairage_reussi_enchaine_sur_la_connexion()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var result = await Service(adb).PairAndConnectAsync("192.168.1.25", 37123, "123456", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.Connected, result.Status);
        Assert.True(result.Connected);
        Assert.Equal("192.168.1.25:37845", result.Address);
        Assert.Equal("adb-MATERIEL123-nJyLWZ", result.DeviceGuid);
    }

    [Fact]
    public async Task Le_code_d_appairage_est_transmis_a_adb_mais_pas_conserve_dans_le_resultat()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var result = await Service(adb).PairAndConnectAsync("192.168.1.25", 37123, "654321", CancellationToken.None);

        Assert.Equal("654321", Assert.Single(adb.PairingCodesSeen));
        Assert.DoesNotContain("654321", result.UserMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("654321", result.Address ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_code_refuse_donne_un_message_qui_explique_quoi_faire()
    {
        var adb = new FakeAdbClient { PairOutcome = AdbPairResult.Failure("Failed: Wrong password") };

        var result = await Service(adb).PairAndConnectAsync("192.168.1.25", 37123, "000000", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.PairingFailed, result.Status);
        Assert.False(result.Paired);
        Assert.Contains("code", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(adb.ConnectAttempts);
    }

    [Fact]
    public async Task Un_mdns_muet_laisse_l_appairage_acquis_et_demande_le_port()
    {
        // Cas fréquent : le réseau ou le pare-feu bloque le mDNS.
        var adb = new FakeAdbClient();

        var result = await Service(adb).PairAndConnectAsync("192.168.1.25", 37123, "123456", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.ConnectPortNotFound, result.Status);
        Assert.True(result.Paired);
        Assert.Contains("port", result.UserMessage, StringComparison.OrdinalIgnoreCase);

        // Appairé n'est pas connecté, et c'est tout l'objet de la distinction :
        // la fenêtre annonçait « il se connectera tout seul » sur ce cas-là.
        Assert.False(result.Connected);
        Assert.True(result.NeedsPort);
    }

    [Fact]
    public async Task Un_port_decouvert_mais_injoignable_est_signale_distinctement()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);

        var result = await Service(adb).PairAndConnectAsync("192.168.1.25", 37123, "123456", CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.ConnectFailed, result.Status);
        Assert.True(result.Paired);
        Assert.Equal("192.168.1.25:37845", result.Address);

        // Le port était connu : le redemander n'aiderait pas.
        Assert.False(result.Connected);
        Assert.False(result.NeedsPort);
    }

    [Fact]
    public async Task Le_sondage_mdns_reessaie_jusqu_a_voir_l_annonce()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("List of discovered mdns services\n");
        adb.MdnsOutputs.Enqueue("List of discovered mdns services\n");
        adb.MdnsOutputs.Enqueue(PairingAndConnect);

        var service = new DevicePairingService(adb, NoDelay)
        {
            ConnectDiscoveryTimeout = TimeSpan.FromSeconds(5),
            DiscoveryPollInterval = TimeSpan.Zero,
        };

        var found = await service.WaitForConnectServiceAsync("192.168.1.25", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(37845, found.Port);
    }

    [Fact]
    public async Task Seul_le_service_de_connexion_de_l_hote_vise_est_retenu()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue("""
            List of discovered mdns services
            adb-AUTRE-xx	_adb-tls-connect._tcp	192.168.1.99:41000
            adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.25:37845
            """);

        var found = await Service(adb).WaitForConnectServiceAsync("192.168.1.25", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("192.168.1.25:37845", found.Address);
    }

    [Fact]
    public async Task Une_connexion_manuelle_reussie_est_rapportee_comme_telle()
    {
        var adb = new FakeAdbClient();
        adb.ConnectableAddresses.Add("192.168.1.25:41000");

        var result = await Service(adb).ConnectAsync("192.168.1.25", 41000, CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.Connected, result.Status);
        Assert.Equal("192.168.1.25:41000", result.Address);
    }

    [Fact]
    public async Task Une_connexion_manuelle_en_echec_donne_un_message_actionnable()
    {
        var result = await Service(new FakeAdbClient()).ConnectAsync("192.168.1.25", 41000, CancellationToken.None);

        Assert.Equal(WirelessPairingStatus.ConnectFailed, result.Status);
        Assert.Contains("réseau", result.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Une_annonce_ecartee_n_est_pas_reprise_par_la_reconnexion()
    {
        // Le téléphone continue de s'annoncer après une rupture : ADB garde sa
        // clé et le débogage sans fil reste actif. Sans la mémoire de l'écart,
        // la reconnexion automatique le reprenait au balayage suivant.
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var connected = await Service(adb).ConnectAnnouncedAsync(
            [],
            new HashSet<string>(StringComparer.Ordinal) { "MATERIEL123" },
            CancellationToken.None);

        Assert.Empty(connected.Connected);
        Assert.Empty(connected.Refused);
        Assert.Empty(adb.ConnectAttempts);
    }

    [Fact]
    public async Task Un_ecart_ne_vaut_que_pour_l_appareil_ecarte()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var connected = await Service(adb).ConnectAnnouncedAsync(
            [],
            new HashSet<string>(StringComparer.Ordinal) { "AUTRETELEPHONE" },
            CancellationToken.None);

        Assert.Equal("192.168.1.25:37845", Assert.Single(connected.Connected));
    }

    [Fact]
    public async Task Sans_ecart_la_reconnexion_reprend_ce_qui_s_annonce()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var connected = await Service(adb).ConnectAnnouncedAsync([], null, CancellationToken.None);

        Assert.Equal("192.168.1.25:37845", Assert.Single(connected.Connected));
        Assert.Empty(connected.Refused);
    }

    [Fact]
    public async Task Une_annonce_qui_refuse_la_connexion_est_signalee()
    {
        // Le cas mesuré sur un vrai téléphone : il s'annonce, son port est
        // ouvert, la connexion TCP passe, et ADB est refusé juste après. Une
        // adresse périmée ne peut pas l'expliquer, puisque l'appareil vient de
        // dire lui-même où il écoute. Il ne reconnaît plus la clé de ce PC.
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);

        var outcome = await Service(adb).ConnectAnnouncedAsync([], null, CancellationToken.None);

        Assert.Empty(outcome.Connected);
        Assert.Equal("MATERIEL123", Assert.Single(outcome.Refused));
    }

    [Fact]
    public async Task Une_annonce_reprise_ne_compte_pas_comme_un_refus()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);
        adb.ConnectableAddresses.Add("192.168.1.25:37845");

        var outcome = await Service(adb).ConnectAnnouncedAsync([], null, CancellationToken.None);

        Assert.Empty(outcome.Refused);
    }

    [Fact]
    public async Task Les_telephones_en_attente_d_appairage_sont_proposes()
    {
        var adb = new FakeAdbClient();
        adb.MdnsOutputs.Enqueue(PairingAndConnect);

        var candidates = await Service(adb).FindPairingCandidatesAsync(CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.IsPairing);
        Assert.Equal("192.168.1.25:37123", candidate.Address);
    }
}
