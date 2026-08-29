using DtHub.Core.Adb;

namespace DtHub.Tests.Adb;

public class AdbDeviceParserTests
{
    private const string DetailedOutput = """
        List of devices attached
        0123456789ABCDEF       device product:aristotle model:23078RKD5G device:aristotle transport_id:1
        192.168.1.25:37845     device product:aristotle model:23078RKD5G device:aristotle transport_id:2
        R58M12ABCDE            unauthorized usb:1-2 transport_id:3
        emulator-5554          offline
        """;

    [Fact]
    public void Sortie_vide_ou_absente_ne_leve_pas_et_ne_rend_rien()
    {
        Assert.Empty(AdbOutputParser.ParseDevices(null));
        Assert.Empty(AdbOutputParser.ParseDevices(string.Empty));
        Assert.Empty(AdbOutputParser.ParseDevices("   \n\t\n"));
        Assert.Empty(AdbOutputParser.ParseDevices("List of devices attached\n\n"));
    }

    [Fact]
    public void Format_court_rend_le_serie_et_l_etat()
    {
        const string output = "List of devices attached\n0123456789ABCDEF\tdevice\n";

        var device = Assert.Single(AdbOutputParser.ParseDevices(output));

        Assert.Equal("0123456789ABCDEF", device.Serial);
        Assert.Equal(AdbDeviceState.Device, device.State);
        Assert.True(device.IsReady);
        Assert.Equal(AdbConnectionKind.Usb, device.ConnectionKind);
    }

    [Fact]
    public void Lignes_de_demarrage_du_demon_sont_ignorees()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            0123456789ABCDEF	device
            """;

        Assert.Single(AdbOutputParser.ParseDevices(output));
    }

    [Fact]
    public void Format_detaille_rend_les_quatre_appareils()
    {
        var devices = AdbOutputParser.ParseDevices(DetailedOutput);

        Assert.Equal(4, devices.Count);
        Assert.Equal(
            ["0123456789ABCDEF", "192.168.1.25:37845", "R58M12ABCDE", "emulator-5554"],
            devices.Select(d => d.Serial));
    }

    [Fact]
    public void Format_detaille_rend_les_metadonnees_du_transport()
    {
        var device = AdbOutputParser.ParseDevices(DetailedOutput)[0];

        Assert.Equal("aristotle", device.Product);
        Assert.Equal("23078RKD5G", device.Model);
        Assert.Equal("aristotle", device.Device);
        Assert.Equal("1", device.TransportId);
    }

    [Fact]
    public void Une_serie_hote_port_est_reconnue_comme_connexion_sans_fil()
    {
        var device = AdbOutputParser.ParseDevices(DetailedOutput)[1];

        Assert.Equal(AdbConnectionKind.Wireless, device.ConnectionKind);
        Assert.Equal("192.168.1.25", device.Host);
        Assert.Equal(37845, device.Port);
    }

    [Fact]
    public void Un_chemin_usb_rapporte_prime_sur_toute_autre_deduction()
    {
        var device = AdbOutputParser.ParseDevices(DetailedOutput)[2];

        Assert.Equal(AdbDeviceState.Unauthorized, device.State);
        Assert.Equal(AdbConnectionKind.Usb, device.ConnectionKind);
        Assert.Equal("1-2", device.UsbPath);
        Assert.Null(device.Host);
    }

    [Fact]
    public void Un_emulateur_est_distingue_d_un_telephone()
    {
        var device = AdbOutputParser.ParseDevices(DetailedOutput)[3];

        Assert.Equal(AdbConnectionKind.Emulator, device.ConnectionKind);
        Assert.Equal(AdbDeviceState.Offline, device.State);
        Assert.False(device.IsReady);
    }

    [Theory]
    [InlineData("device", AdbDeviceState.Device)]
    [InlineData("offline", AdbDeviceState.Offline)]
    [InlineData("unauthorized", AdbDeviceState.Unauthorized)]
    [InlineData("authorizing", AdbDeviceState.Authorizing)]
    [InlineData("connecting", AdbDeviceState.Connecting)]
    [InlineData("bootloader", AdbDeviceState.Bootloader)]
    [InlineData("recovery", AdbDeviceState.Recovery)]
    [InlineData("sideload", AdbDeviceState.Sideload)]
    [InlineData("rescue", AdbDeviceState.Rescue)]
    [InlineData("host", AdbDeviceState.Host)]
    [InlineData("detached", AdbDeviceState.Detached)]
    public void Chaque_etat_connu_est_traduit(string raw, AdbDeviceState expected)
    {
        Assert.Equal(expected, AdbOutputParser.ParseState(raw));
    }

    [Fact]
    public void Un_etat_inconnu_reste_lisible_via_la_chaine_brute()
    {
        var device = AdbOutputParser.ParseDeviceLine("0123456789ABCDEF  quelquechosedenouveau");

        Assert.NotNull(device);
        Assert.Equal(AdbDeviceState.Unknown, device.State);
        Assert.Equal("quelquechosedenouveau", device.RawState);
    }

    [Fact]
    public void Un_defaut_de_permission_usb_est_reconnu()
    {
        var device = AdbOutputParser.ParseDeviceLine("0123456789ABCDEF  no permissions (user in plugdev group)");

        Assert.NotNull(device);
        Assert.Equal(AdbDeviceState.NoPermissions, device.State);
    }

    [Fact]
    public void Un_serie_usb_contenant_deux_points_n_est_pas_pris_pour_une_adresse()
    {
        var (host, port) = AdbOutputParser.SplitNetworkSerial("ABC:1234");

        Assert.Null(host);
        Assert.Null(port);
    }

    [Fact]
    public void Une_adresse_ipv6_entre_crochets_est_decoupee()
    {
        var (host, port) = AdbOutputParser.SplitNetworkSerial("[fe80::1]:5555");

        Assert.Equal("fe80::1", host);
        Assert.Equal(5555, port);
    }

    [Theory]
    [InlineData("192.168.1.25:0")]
    [InlineData("192.168.1.25:70000")]
    [InlineData("192.168.1.25:")]
    [InlineData("192.168.1.25:abcd")]
    public void Un_port_invalide_ne_produit_pas_d_adresse(string serial)
    {
        var (host, port) = AdbOutputParser.SplitNetworkSerial(serial);

        Assert.Null(host);
        Assert.Null(port);
    }

    [Fact]
    public void Un_nom_de_service_mdns_est_traite_comme_du_sans_fil()
    {
        var kind = AdbOutputParser.DetectConnectionKind("adb-0123456789ABCDEF-nJyLWZ._adb-tls-connect._tcp");

        Assert.Equal(AdbConnectionKind.Wireless, kind);
    }

    [Fact]
    public void Le_nom_affiche_retombe_sur_le_serie_sans_modele()
    {
        var withModel = AdbOutputParser.ParseDeviceLine("0123 device model:Pixel_9_Pro");
        var withoutModel = AdbOutputParser.ParseDeviceLine("0123 device");

        Assert.Equal("Pixel 9 Pro", withModel!.DisplayName);
        Assert.Equal("0123", withoutModel!.DisplayName);
    }
}
