using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

public class AdbWirelessParserTests
{
    private const string MdnsOutput = """
        List of discovered mdns services
        adb-MATERIEL123-nJyLWZ	_adb-tls-pairing._tcp	192.168.1.25:37123
        adb-MATERIEL123-nJyLWZ	_adb-tls-connect._tcp	192.168.1.25:37845
        adb-MATERIEL456-QqRtUv	_adb-tls-connect._tcp	192.168.1.31:41002
        """;

    [Fact]
    public void Une_sortie_mdns_vide_ne_leve_pas()
    {
        Assert.Empty(AdbOutputParser.ParseMdnsServices(null));
        Assert.Empty(AdbOutputParser.ParseMdnsServices("List of discovered mdns services\n"));
    }

    [Fact]
    public void Les_services_mdns_sont_lus_avec_leur_adresse()
    {
        var services = AdbOutputParser.ParseMdnsServices(MdnsOutput);

        Assert.Equal(3, services.Count);
        Assert.Equal("192.168.1.25", services[0].Host);
        Assert.Equal(37123, services[0].Port);
        Assert.Equal("192.168.1.25:37845", services[1].Address);
    }

    [Fact]
    public void Le_service_d_appairage_et_celui_de_connexion_sont_distingues()
    {
        var services = AdbOutputParser.ParseMdnsServices(MdnsOutput);

        Assert.True(services[0].IsPairing);
        Assert.False(services[0].IsConnect);
        Assert.True(services[1].IsConnect);
        Assert.False(services[1].IsPairing);
    }

    [Fact]
    public void Une_annonce_est_rattachee_a_un_appareil_par_son_numero_de_serie()
    {
        var services = AdbOutputParser.ParseMdnsServices(MdnsOutput);

        Assert.True(services[1].MatchesSerial("MATERIEL123"));
        Assert.False(services[1].MatchesSerial("MATERIEL456"));
        Assert.False(services[1].MatchesSerial(null));
    }

    [Fact]
    public void Les_lignes_d_entete_et_de_version_du_demon_sont_ignorees()
    {
        const string output = """
            mdns daemon version [Openscreen discovery 0.0.0]
            List of discovered mdns services
            adb-A-b	_adb-tls-connect._tcp	10.0.0.5:5555
            """;

        Assert.Single(AdbOutputParser.ParseMdnsServices(output));
    }

    [Fact]
    public void Une_ligne_mdns_incomplete_est_ignoree_sans_lever()
    {
        const string output = """
            List of discovered mdns services
            adb-A-b	_adb-tls-connect._tcp
            adb-C-d	_adb-tls-connect._tcp	pas-une-adresse
            adb-E-f	_adb-tls-connect._tcp	10.0.0.5:5555
            """;

        Assert.Single(AdbOutputParser.ParseMdnsServices(output));
    }

    [Fact]
    public void Un_appairage_reussi_rend_l_identifiant_de_l_appareil()
    {
        var result = AdbOutputParser.ParsePairResult(
            "Successfully paired to 192.168.1.25:37123 [guid=adb-MATERIEL123-nJyLWZ]");

        Assert.True(result.Succeeded);
        Assert.Equal("adb-MATERIEL123-nJyLWZ", result.DeviceGuid);
    }

    [Theory]
    [InlineData("Failed: Wrong password")]
    [InlineData("Failed: Unable to start pairing client. Check the port and try again.")]
    [InlineData("adb: error: unknown host")]
    public void Un_appairage_en_echec_conserve_la_raison(string output)
    {
        var result = AdbOutputParser.ParsePairResult(output);

        Assert.False(result.Succeeded);
        Assert.Equal(output, result.FailureReason);
    }

    [Fact]
    public void Une_sortie_d_appairage_vide_est_un_echec()
    {
        Assert.False(AdbOutputParser.ParsePairResult(null).Succeeded);
        Assert.False(AdbOutputParser.ParsePairResult("   ").Succeeded);
    }

    [Fact]
    public void Une_connexion_reussie_est_reconnue()
    {
        var result = AdbOutputParser.ParseConnectResult("connected to 192.168.1.25:37845");

        Assert.True(result.Succeeded);
        Assert.False(result.AlreadyConnected);
    }

    [Fact]
    public void Une_connexion_deja_etablie_est_un_succes_signale_comme_tel()
    {
        var result = AdbOutputParser.ParseConnectResult("already connected to 192.168.1.25:37845");

        Assert.True(result.Succeeded);
        Assert.True(result.AlreadyConnected);
    }

    [Theory]
    [InlineData("failed to connect to '192.168.1.25:37845': Connection refused")]
    [InlineData("cannot connect to 192.168.1.25:37845: No connection could be made (10061)")]
    [InlineData("unable to connect to 192.168.1.25:37845")]
    public void Un_echec_de_connexion_conserve_la_raison(string output)
    {
        var result = AdbOutputParser.ParseConnectResult(output);

        Assert.False(result.Succeeded);
        Assert.Equal(output, result.FailureReason);
    }

    [Fact]
    public void Une_sortie_de_connexion_inattendue_est_traitee_comme_un_echec()
    {
        Assert.False(AdbOutputParser.ParseConnectResult("quelque chose d'inconnu").Succeeded);
        Assert.False(AdbOutputParser.ParseConnectResult(null).Succeeded);
    }
}
