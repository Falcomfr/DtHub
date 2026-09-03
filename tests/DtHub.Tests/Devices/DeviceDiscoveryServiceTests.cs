using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Devices;

public class DeviceDiscoveryServiceTests
{
    private const string XiaomiProps = """
        [ro.serialno]: [MATERIEL123]
        [ro.product.manufacturer]: [Xiaomi]
        [ro.product.model]: [23078RKD5G]
        [ro.product.marketname]: [13T Pro]
        [ro.build.version.release]: [14]
        [ro.build.version.sdk]: [34]
        """;

    private const string SamsungProps = """
        [ro.serialno]: [MATERIEL456]
        [ro.product.manufacturer]: [samsung]
        [ro.product.model]: [SM-S921B]
        [ro.build.version.release]: [15]
        [ro.build.version.sdk]: [35]
        """;

    [Fact]
    public async Task Plusieurs_telephones_sont_decouverts_et_enrichis()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                USB0001  device usb:1-2 transport_id:1
                USB0002  device usb:1-3 transport_id:2
                """,
        }
            .WithProperties("USB0001", XiaomiProps)
            .WithProperties("USB0002", SamsungProps);

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, result.Devices.Count);
        Assert.Empty(result.Warnings);
        Assert.Contains(result.Devices, d => d.DisplayName == "13T Pro");
        Assert.Contains(result.Devices, d => d.DisplayName == "samsung SM-S921B");
    }

    [Fact]
    public async Task Les_proprietes_ne_sont_relues_qu_une_fois_par_appareil()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 device usb:1-2\n",
        }.WithProperties("USB0001", XiaomiProps);

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        await service.RefreshAsync(CancellationToken.None);
        await service.RefreshAsync(CancellationToken.None);
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(3, adb.ListDevicesCallCount);
        Assert.Equal(1, adb.GetPropertiesCallCount);
    }

    [Fact]
    public async Task Vider_le_cache_force_une_relecture()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 device usb:1-2\n",
        }.WithProperties("USB0001", XiaomiProps);

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        await service.RefreshAsync(CancellationToken.None);
        service.InvalidatePropertyCache();
        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(2, adb.GetPropertiesCallCount);
    }

    [Fact]
    public async Task Un_appareil_non_autorise_reste_liste_avec_un_avertissement()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 unauthorized usb:1-2\n",
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var result = await service.RefreshAsync(CancellationToken.None);

        var device = Assert.Single(result.Devices);
        Assert.Equal(AdbDeviceState.Unauthorized, device.State);
        Assert.False(device.IsConnected);

        // getprop n'est pas tenté sur un appareil qui n'est pas prêt.
        Assert.Equal(0, adb.GetPropertiesCallCount);
    }

    [Fact]
    public async Task Un_getprop_en_echec_n_empeche_pas_l_appareil_d_apparaitre()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 device usb:1-2\n",
        }.FailProperties("USB0001");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Single(result.Devices);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task Un_appareil_connu_mais_absent_est_montre_hors_ligne()
    {
        var known = new AndroidDevice
        {
            Id = "MATERIEL123",
            Serial = "USB0001",
            CustomName = "Téléphone du salon",
        };

        var adb = new FakeAdbClient { DevicesOutput = "List of devices attached\n" };
        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry(known));

        var result = await service.RefreshAsync(CancellationToken.None);

        var device = Assert.Single(result.Devices);
        Assert.Equal("Téléphone du salon", device.DisplayName);
        Assert.False(device.IsConnected);
    }

    [Fact]
    public async Task Un_meme_telephone_vu_en_usb_et_en_wifi_n_apparait_qu_une_fois()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.25:37845  device transport_id:1
                USB0001             device usb:1-2 transport_id:2
                """,
        }
            .WithProperties("USB0001", XiaomiProps)
            .WithProperties("192.168.1.25:37845", XiaomiProps);

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var result = await service.RefreshAsync(CancellationToken.None);

        var device = Assert.Single(result.Devices);
        Assert.Equal("MATERIEL123", device.Id);

        // À égalité d'état, l'USB est retenu parce qu'il est plus stable.
        Assert.Equal(AdbConnectionKind.Usb, device.ConnectionKind);
    }

    [Fact]
    public async Task Les_emulateurs_sont_ecartes()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                emulator-5554  device
                USB0001        device usb:1-2
                """,
        }.WithProperties("USB0001", XiaomiProps);

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Single(result.Devices);
        Assert.DoesNotContain(result.Devices, d => d.Serial.StartsWith("emulator", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Adb_indisponible_affiche_les_appareils_memorises_et_un_avertissement()
    {
        var known = new AndroidDevice { Id = "MATERIEL123", Serial = "USB0001", CustomName = "Salon" };

        var adb = new FakeAdbClient
        {
            DevicesError = new AdbException(
                AdbErrorKind.AdbUnavailable,
                AdbErrorInterpreter.Describe(AdbErrorKind.AdbUnavailable)),
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry(known));

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Single(result.Devices);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task L_appareil_principal_est_presente_en_premier()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                USB0001  device usb:1-2
                USB0002  device usb:1-3
                """,
        }
            .WithProperties("USB0001", XiaomiProps)
            .WithProperties("USB0002", SamsungProps);

        var registry = new InMemoryDeviceRegistry(new AndroidDevice
        {
            Id = "MATERIEL456",
            Serial = "USB0002",
            IsPrimary = true,
        });

        using var service = new DeviceDiscoveryService(adb, registry);

        var result = await service.RefreshAsync(CancellationToken.None);

        Assert.Equal("MATERIEL456", result.Devices[0].Id);
        Assert.True(result.Devices[0].IsPrimary);
    }

    [Fact]
    public async Task Une_connexion_sans_fil_morte_est_coupee()
    {
        // Cas réel : le port du débogage sans fil change au redémarrage du
        // téléphone, et l'ancienne connexion reste listée hors ligne.
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.16:33055  device product:corot_global model:23078PND5G
                192.168.1.16:33805  offline product:corot_global model:23078PND5G
                """,
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(1, await service.PruneStaleWirelessTransportsAsync(CancellationToken.None));
        Assert.Equal(["192.168.1.16:33805"], adb.Disconnected);
    }

    [Fact]
    public async Task Une_connexion_vivante_ou_usb_n_est_jamais_coupee()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.16:33055  device product:corot_global
                USB0001             offline usb:1-2
                """,
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(0, await service.PruneStaleWirelessTransportsAsync(CancellationToken.None));
        Assert.Empty(adb.Disconnected);
    }

    [Fact]
    public async Task Un_adb_indisponible_ne_fait_pas_echouer_le_menage()
    {
        var adb = new FakeAdbClient
        {
            DevicesError = new AdbException(
                AdbErrorKind.AdbUnavailable, AdbErrorInterpreter.Describe(AdbErrorKind.AdbUnavailable)),
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(0, await service.PruneStaleWirelessTransportsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Le_registre_est_mis_a_jour_a_chaque_balayage()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 device usb:1-2\n",
        }.WithProperties("USB0001", XiaomiProps);

        var registry = new InMemoryDeviceRegistry();
        using var service = new DeviceDiscoveryService(adb, registry);

        await service.RefreshAsync(CancellationToken.None);

        var known = await registry.GetKnownAsync(CancellationToken.None);
        Assert.Equal("MATERIEL123", Assert.Single(known).Id);
    }
}
