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

    /// <summary>Attente instantanée : les tests ne doivent rien attendre réellement.</summary>
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static DevicePairingService Service(FakeAdbClient adb) =>
        new(adb, NoDelay)
        {
            ConnectDiscoveryTimeout = TimeSpan.FromMilliseconds(1),
            DiscoveryPollInterval = TimeSpan.Zero,
        };

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
