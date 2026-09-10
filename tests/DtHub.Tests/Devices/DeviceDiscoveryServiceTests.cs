using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Processes;
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

    [Fact]
    public async Task Les_deux_transports_d_un_meme_telephone_sont_coupes()
    {
        // Mesuré sur l'appareil de développement : ADB en ouvre deux, celui de
        // l'adresse et celui du nom mDNS qu'il découvre tout seul. Ne couper
        // que le premier laissait le téléphone joignable.
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.16:44477                                device
                adb-CMBU79RCINVSFYUO-1V3FXQ._adb-tls-connect._tcp  device
                """,
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var device = new AndroidDevice
        {
            Id = "CMBU79RCINVSFYUO",
            Serial = "192.168.1.16:44477",
            LastKnownAddress = "192.168.1.16",
            LastKnownPort = 44477,
        };

        var cut = await service.DisconnectDeviceAsync(device, CancellationToken.None);

        Assert.Equal(
            ["192.168.1.16:44477", "adb-CMBU79RCINVSFYUO-1V3FXQ._adb-tls-connect._tcp"],
            cut);
        Assert.Equal(cut, adb.Disconnected);
    }

    [Fact]
    public async Task La_coupure_epargne_les_transports_des_autres_telephones()
    {
        var adb = new FakeAdbClient
        {
            DevicesOutput = """
                List of devices attached
                192.168.1.16:44477  device
                192.168.1.99:41000  device
                USB0001             device usb:1-2
                """,
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var device = new AndroidDevice { Id = "MATERIEL123", Serial = "192.168.1.16:44477" };

        Assert.Equal(
            ["192.168.1.16:44477"],
            await service.DisconnectDeviceAsync(device, CancellationToken.None));
    }

    [Fact]
    public async Task L_adresse_memorisee_est_coupee_meme_si_adb_ne_repond_plus()
    {
        var adb = new FakeAdbClient
        {
            DevicesError = new AdbException(
                AdbErrorKind.AdbUnavailable,
                AdbErrorInterpreter.Describe(AdbErrorKind.AdbUnavailable)),
        };

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var device = new AndroidDevice
        {
            Id = "MATERIEL123",
            Serial = "192.168.1.16:44477",
            LastKnownAddress = "192.168.1.16",
            LastKnownPort = 44477,
        };

        Assert.Equal(
            ["192.168.1.16:44477"],
            await service.DisconnectDeviceAsync(device, CancellationToken.None));
    }

    [Fact]
    public async Task La_sonde_d_entree_envoie_la_touche_inconnue_et_lit_le_silence()
    {
        var adb = new FakeAdbClient();
        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var verdict = await service.CheckInputInjectionAsync("USB0001", CancellationToken.None);

        Assert.Equal(InputInjection.Works, verdict);
        Assert.Equal("shell input keyevent 0", Assert.Single(adb.ExecuteCalls));
    }

    [Fact]
    public async Task La_sonde_d_entree_rapporte_un_refus_nomme()
    {
        var adb = new FakeAdbClient().WithExecute(
            "keyevent",
            new ProcessResult
            {
                ExitCode = 255,
                StandardOutput = string.Empty,
                StandardError = "java.lang.SecurityException: Injecting to another application requires INJECT_EVENTS permission",
                Duration = TimeSpan.Zero,
            });

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(
            InputInjection.Denied,
            await service.CheckInputInjectionAsync("USB0001", CancellationToken.None));
    }

    [Fact]
    public async Task La_sonde_d_entree_ne_conclut_pas_sans_appareil()
    {
        var adb = new FakeAdbClient();
        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(
            InputInjection.Unknown,
            await service.CheckInputInjectionAsync("   ", CancellationToken.None));

        Assert.Empty(adb.ExecuteCalls);
    }

    [Fact]
    public async Task La_sonde_d_entree_ne_conclut_pas_sur_un_delai_depasse()
    {
        var adb = new FakeAdbClient().WithExecute(
            "keyevent",
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = string.Empty,
                StandardError = string.Empty,
                Duration = TimeSpan.FromSeconds(5),
                TimedOut = true,
            });

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Equal(
            InputInjection.Unknown,
            await service.CheckInputInjectionAsync("USB0001", CancellationToken.None));
    }

    [Fact]
    public async Task La_batterie_est_lue_puis_gardee_une_minute()
    {
        // Même raison que la chaleur : la question coûte un aller-retour, et
        // une batterie ne perd pas dix pour cent en dix secondes.
        var adb = new FakeAdbClient().WithShell("battery", "  level: 42\n  scale: 100\n  AC powered: false");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var premier = await service.GetBatteryAsync("USB0001", CancellationToken.None);
        var second = await service.GetBatteryAsync("USB0001", CancellationToken.None);

        Assert.Equal(42, premier?.Percent);
        Assert.Equal(42, second?.Percent);
        Assert.Single(adb.ShellCalls, c => c.Contains("battery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_appareil_muet_sur_sa_batterie_ne_fait_rien_echouer()
    {
        var adb = new FakeAdbClient().WithShell("battery", "dumpsys: service battery does not exist");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Null(await service.GetBatteryAsync("USB0001", CancellationToken.None));
        Assert.Null(await service.GetBatteryAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task La_place_libre_est_lue_puis_gardee()
    {
        // Gardée bien plus longtemps que les autres : la place ne bouge pas en
        // séance, et c'est une assurance, pas une surveillance.
        var adb = new FakeAdbClient().WithShell(
            "df",
            "Filesystem 1K-blocks Used Available Use% Mounted on\n/dev/x 100 50 313535476 36% /data");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var premier = await service.GetStorageAsync("USB0001", CancellationToken.None);
        _ = await service.GetStorageAsync("USB0001", CancellationToken.None);

        Assert.Equal(313535476L * 1024, premier?.FreeBytes);
        Assert.Single(adb.ShellCalls, c => c.Contains("df", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_appareil_muet_sur_sa_place_ne_fait_rien_echouer()
    {
        var adb = new FakeAdbClient().WithShell("df", "df: /data: Permission denied");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Null(await service.GetStorageAsync("USB0001", CancellationToken.None));
        Assert.Null(await service.GetStorageAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task La_chaleur_est_lue_puis_gardee_une_minute()
    {
        // La question coûte un aller-retour de shell, et le panneau sonde
        // jusqu'à deux fois par seconde. La chaleur, elle, ne bouge pas à ce
        // rythme.
        var adb = new FakeAdbClient().WithShell("thermalservice", "Thermal Status: 3");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        var premier = await service.GetThermalAsync("USB0001", CancellationToken.None);
        var second = await service.GetThermalAsync("USB0001", CancellationToken.None);

        Assert.Equal(3, premier?.Status);
        Assert.Equal(3, second?.Status);
        Assert.Single(adb.ShellCalls, c => c.Contains("thermalservice", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_appareil_muet_sur_sa_chaleur_ne_fait_rien_echouer()
    {
        var adb = new FakeAdbClient().WithShell(
            "thermalservice", "dumpsys: service thermalservice does not exist");

        using var service = new DeviceDiscoveryService(adb, new InMemoryDeviceRegistry());

        Assert.Null(await service.GetThermalAsync("USB0001", CancellationToken.None));
        Assert.Null(await service.GetThermalAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task Un_balayage_ne_lit_le_registre_qu_une_fois()
    {
        // Le registre relit son fichier à chaque demande. Réclamer les
        // mémorisés puis les écartés séparément le faisait lire deux fois par
        // balayage, alors que les deux vivent dans le même document.
        var adb = new FakeAdbClient
        {
            DevicesOutput = "List of devices attached\nUSB0001 device usb:1-2\n",
        }.WithProperties("USB0001", XiaomiProps);

        var registry = new InMemoryDeviceRegistry();
        using var service = new DeviceDiscoveryService(adb, registry);

        await service.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, registry.Reads);
    }

    [Fact]
    public async Task Un_appareil_ecarte_sort_du_balayage_entier()
    {
        // Le défaut rapporté : rompre l'association effaçait bien l'entrée,
        // puis le balayage suivant la remettait, le téléphone étant toujours
        // joignable. La rupture ne durait donc qu'un instant.
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

        var registry = new InMemoryDeviceRegistry();
        _ = registry.Discarded.Add("MATERIEL123");

        using var service = new DeviceDiscoveryService(adb, registry);

        var discovery = await service.RefreshAsync(CancellationToken.None);

        // ADB continue de le voir, et rien n'y changera : le serveur rejoint
        // tout seul un téléphone qui s'annonce et dont il garde la clé. C'est
        // pourquoi il doit sortir de ce que la découverte rend, et pas
        // seulement de ce qu'elle écrit : sinon la ligne reste à l'écran.
        Assert.DoesNotContain(discovery.Devices, d => d.Id == "MATERIEL123");
        Assert.Equal("MATERIEL456", Assert.Single(discovery.Devices).Id);

        var known = await registry.GetKnownAsync(CancellationToken.None);

        Assert.Equal("MATERIEL456", Assert.Single(known).Id);
    }

    [Fact]
    public async Task Un_appareil_ecarte_ne_revient_pas_davantage_par_le_souvenir()
    {
        // L'écart efface l'entrée, mais un registre mémorisé d'avant la rupture
        // pourrait encore la porter. La liste des hors ligne est bâtie sur ce
        // registre : elle doit l'écarter elle aussi.
        var adb = new FakeAdbClient { DevicesOutput = "List of devices attached\n" };

        var registry = new InMemoryDeviceRegistry();
        await registry.UpsertRangeAsync(
            [new AndroidDevice { Id = "MATERIEL123", Serial = "192.168.1.16:44477" }],
            CancellationToken.None);
        _ = registry.Discarded.Add("MATERIEL123");

        using var service = new DeviceDiscoveryService(adb, registry);

        var discovery = await service.RefreshAsync(CancellationToken.None);

        Assert.Empty(discovery.Devices);
    }
}
