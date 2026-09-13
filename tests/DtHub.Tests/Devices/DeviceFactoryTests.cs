using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class DeviceFactoryTests
{
    private static readonly Dictionary<string, string> XiaomiProperties = new(StringComparer.Ordinal)
    {
        ["ro.serialno"] = "MATERIEL123",
        ["ro.product.manufacturer"] = "Xiaomi",
        ["ro.product.model"] = "23078RKD5G",
        ["ro.product.marketname"] = "13T Pro",
        ["ro.product.device"] = "aristotle",
        ["ro.build.version.release"] = "14",
        ["ro.build.version.sdk"] = "34",
    };

    private static AdbDeviceEntry Usb(string serial = "USB0001") =>
        AdbOutputParser.ParseDeviceLine($"{serial} device usb:1-2 transport_id:1")!;

    private static AdbDeviceEntry Wireless(string address = "192.168.1.25:37845") =>
        AdbOutputParser.ParseDeviceLine($"{address} device transport_id:2")!;

    [Fact]
    public void Les_proprietes_systeme_alimentent_le_modele()
    {
        var device = DeviceFactory.Create(Usb(), XiaomiProperties);

        Assert.Equal("Xiaomi", device.Manufacturer);
        Assert.Equal("23078RKD5G", device.Model);
        Assert.Equal("13T Pro", device.MarketName);
        Assert.Equal("aristotle", device.DeviceCodename);
        Assert.Equal("14", device.AndroidVersion);
        Assert.Equal(34, device.SdkVersion);
    }

    [Fact]
    public void L_identite_stable_vient_du_numero_de_serie_materiel()
    {
        var overUsb = DeviceFactory.Create(Usb(), XiaomiProperties);
        var overWifi = DeviceFactory.Create(Wireless(), XiaomiProperties);

        // Same phone, two transports: a single identity.
        Assert.Equal("MATERIEL123", overUsb.Id);
        Assert.Equal(overUsb.Id, overWifi.Id);
        Assert.NotEqual(overUsb.Serial, overWifi.Serial);
    }

    [Fact]
    public void Sans_numero_materiel_l_identite_usb_retombe_sur_le_serie_adb()
    {
        var device = DeviceFactory.Create(Usb("USB0001"));

        Assert.Equal("USB0001", device.Id);
    }

    [Fact]
    public void Sans_numero_materiel_une_connexion_sans_fil_conserve_l_identite_connue()
    {
        var known = DeviceFactory.Create(Usb("USB0001"));

        // The phone switches to Wi-Fi and hides its serial number:
        // without carrying over the known identity, it would count as
        // a second device.
        var device = DeviceFactory.Create(Wireless(), properties: null, known: known);

        Assert.Equal("USB0001", device.Id);
    }

    [Fact]
    public void Sans_rien_de_connu_une_identite_de_repli_est_marquee_comme_telle()
    {
        var device = DeviceFactory.Create(Wireless("192.168.1.25:37845"));

        Assert.StartsWith(DeviceFactory.FallbackIdPrefix, device.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_nom_choisi_par_l_utilisateur_survit_a_une_redecouverte()
    {
        var known = DeviceFactory.Create(Usb(), XiaomiProperties) with
        {
            CustomName = "Téléphone du salon",
            IsPrimary = true,
            IsPaired = true,
        };

        var device = DeviceFactory.Create(Usb(), XiaomiProperties, known);

        Assert.Equal("Téléphone du salon", device.CustomName);
        Assert.Equal("Téléphone du salon", device.DisplayName);
        Assert.True(device.IsPrimary);
        Assert.True(device.IsPaired);
    }

    [Fact]
    public void Une_decouverte_sans_getprop_n_efface_pas_ce_qui_etait_connu()
    {
        var known = DeviceFactory.Create(Usb(), XiaomiProperties);

        // Real case: unauthorized device, getprop is not possible.
        var unauthorized = AdbOutputParser.ParseDeviceLine("USB0001 unauthorized usb:1-2")!;
        var device = DeviceFactory.Create(unauthorized, properties: null, known: known);

        Assert.Equal("Xiaomi", device.Manufacturer);
        Assert.Equal("13T Pro", device.MarketName);
        Assert.Equal(AdbDeviceState.Unauthorized, device.State);
        Assert.False(device.IsConnected);
    }

    [Fact]
    public void Une_connexion_sans_fil_implique_que_l_appareil_est_appaire()
    {
        // You cannot be connected wirelessly without having paired the
        // device: observing this allows automatic reconnection
        // afterwards.
        var wireless = DeviceFactory.Create(Wireless(), XiaomiProperties);
        var usb = DeviceFactory.Create(Usb(), XiaomiProperties);

        Assert.True(wireless.IsPaired);
        Assert.False(usb.IsPaired);
    }

    [Fact]
    public void L_appairage_constate_survit_a_un_branchement_usb()
    {
        var known = DeviceFactory.Create(Wireless(), XiaomiProperties);

        Assert.True(DeviceFactory.Create(Usb(), XiaomiProperties, known).IsPaired);
    }

    [Fact]
    public void L_adresse_de_reconnexion_est_memorisee_en_sans_fil()
    {
        var device = DeviceFactory.Create(Wireless("192.168.1.25:37845"), XiaomiProperties);

        Assert.Equal("192.168.1.25", device.LastKnownAddress);
        Assert.Equal(37845, device.LastKnownPort);
        Assert.Equal("192.168.1.25:37845", device.ReconnectAddress);
    }

    [Fact]
    public void Un_branchement_usb_n_efface_pas_la_derniere_adresse_connue()
    {
        var known = DeviceFactory.Create(Wireless("192.168.1.25:37845"), XiaomiProperties);

        var device = DeviceFactory.Create(Usb(), XiaomiProperties, known);

        Assert.Equal(AdbConnectionKind.Usb, device.ConnectionKind);
        Assert.Equal("192.168.1.25:37845", device.ReconnectAddress);
    }

    [Fact]
    public void Le_nom_affiche_suit_un_ordre_de_priorite_previsible()
    {
        var entry = Usb();

        var marketName = DeviceFactory.Create(entry, XiaomiProperties);
        var modelOnly = DeviceFactory.Create(entry, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ro.product.manufacturer"] = "Samsung",
            ["ro.product.model"] = "SM-S921B",
        });
        var nothing = DeviceFactory.Create(entry);

        Assert.Equal("13T Pro", marketName.DisplayName);
        Assert.Equal("Samsung SM-S921B", modelOnly.DisplayName);
        Assert.Equal("USB0001", nothing.DisplayName);
    }

    [Fact]
    public void Le_nom_affiche_ne_repete_pas_le_constructeur_deja_present_dans_le_modele()
    {
        var device = DeviceFactory.Create(Usb(), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ro.product.manufacturer"] = "Google",
            ["ro.product.model"] = "Google Pixel 9 Pro",
        });

        Assert.Equal("Google Pixel 9 Pro", device.DisplayName);
    }

    [Fact]
    public void Les_valeurs_de_remplissage_d_android_sont_ignorees()
    {
        var device = DeviceFactory.Create(Usb(), new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ro.serialno"] = "unknown",
            ["ro.product.marketname"] = "unknown",
            ["ro.build.version.sdk"] = "0",
        });

        Assert.Equal("USB0001", device.Id);
        Assert.Null(device.MarketName);
        Assert.Null(device.SdkVersion);
    }

    [Fact]
    public void Un_appareil_connu_est_retrouve_par_son_numero_materiel()
    {
        var known = DeviceFactory.Create(Usb(), XiaomiProperties);

        var matched = DeviceFactory.Match([known], Wireless(), "MATERIEL123");

        Assert.Same(known, matched);
    }

    [Fact]
    public void Un_appareil_connu_est_retrouve_par_sa_derniere_adresse()
    {
        var known = DeviceFactory.Create(Wireless("192.168.1.25:37845"), XiaomiProperties);

        // The wireless debugging port changes on every restart of the
        // phone.
        var matched = DeviceFactory.Match([known], Wireless("192.168.1.25:41231"));

        Assert.Same(known, matched);
    }

    [Fact]
    public void Un_appareil_inconnu_ne_correspond_a_rien()
    {
        var known = DeviceFactory.Create(Usb("USB0001"), XiaomiProperties);

        Assert.Null(DeviceFactory.Match([known], Usb("AUTRE9999")));
    }
}
