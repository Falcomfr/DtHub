using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class WifiLinkTests
{
    /// <summary>
    /// Sortie réelle de <c>cmd wifi status</c>, relevée sur le Xiaomi 13T Pro
    /// du poste de développement, Android 16. Raccourcie aux lignes utiles,
    /// mais les lignes conservées sont recopiées au caractère près : c'est tout
    /// l'intérêt d'une épreuve sur une sortie de terrain.
    /// </summary>
    private const string Releve = """
        Wifi is enabled
        Wifi scanning is always available
        ==== Primary ClientModeManager instance ====
        Wifi is connected to "tkt-home"
        WifiInfo: SSID: "tkt-home", BSSID: c8:99:b2:30:15:90, MAC: c6:4c:15:25:7b:b0, IP: /192.168.1.14, Security type: 2, Supplicant state: COMPLETED, Wi-Fi standard: 11n, RSSI: -57, Link speed: 144Mbps, Tx Link speed: 144Mbps, Max Supported Tx Link speed: 144Mbps, Rx Link speed: 144Mbps, Max Supported Rx Link speed: 144Mbps, Frequency: 2412MHz, Net ID: 15, Metered hint: false, score: 60, isUsable: true
        successfulTxPackets: 1368241
        successfulTxPacketsPerSecond: 186.10399554366833
        retriedTxPackets: 169900
        """;

    [Fact]
    public void Le_releve_de_terrain_est_lu_entierement()
    {
        var link = WifiLink.Parse(Releve);

        Assert.NotNull(link);
        Assert.Equal(144, link.LinkSpeedMbps);
        Assert.Equal(2412, link.FrequencyMhz);
        Assert.Equal("11n", link.Standard);
        Assert.Equal(-57, link.Rssi);
        Assert.True(link.Is24GHz);
    }

    [Fact]
    public void La_part_de_reemissions_est_calculee()
    {
        // 169 900 réémissions pour 1 368 241 trames réussies : douze pour cent,
        // ce qui est la signature d'un canal partagé avec des voisins.
        var link = WifiLink.Parse(Releve);

        Assert.NotNull(link);
        Assert.InRange(link.RetryShare, 0.11, 0.13);
    }

    [Fact]
    public void Le_plafond_du_materiel_n_est_pas_pris_pour_la_liaison()
    {
        // « Max Supported Tx Link speed » annonce ce dont la puce est capable,
        // pas ce qui est négocié. Les confondre ferait promettre à une liaison
        // dégradée le débit d'une liaison parfaite.
        const string releve = "Tx Link speed: 72Mbps, Max Supported Tx Link speed: 1200Mbps, Frequency: 5520MHz,";

        var link = WifiLink.Parse(releve);

        Assert.NotNull(link);
        Assert.Equal(72, link.LinkSpeedMbps);
        Assert.False(link.Is24GHz);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Wifi is disabled")]
    [InlineData("Frequency: 2412MHz sans vitesse")]
    public void Une_sortie_sans_l_essentiel_ne_rend_rien(string sortie)
    {
        // Ne rien savoir est un cas ordinaire : appareil en USB, Wi-Fi éteint,
        // ou Android qui nomme les choses autrement. L'application doit
        // continuer sans contrainte, pas échouer.
        Assert.Null(WifiLink.Parse(sortie));
    }

    [Fact]
    public void Une_sortie_absente_ne_rend_rien()
    {
        Assert.Null(WifiLink.Parse(null));
    }
}
