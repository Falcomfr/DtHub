using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class WifiLinkTests
{
    /// <summary>
    /// Real output of <c>cmd wifi status</c>, captured on the Xiaomi
    /// 13T Pro of the development machine, Android 16. Shortened to
    /// the useful lines, but the lines kept are copied character for
    /// character: that is the whole point of a test built on
    /// field-captured output.
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
        // 169,900 retries out of 1,368,241 successful frames: twelve
        // percent, which is the signature of a channel shared with
        // neighbors.
        var link = WifiLink.Parse(Releve);

        Assert.NotNull(link);
        Assert.InRange(link.RetryShare, 0.11, 0.13);
    }

    [Fact]
    public void Le_plafond_du_materiel_n_est_pas_pris_pour_la_liaison()
    {
        // "Max Supported Tx Link speed" announces what the chip is
        // capable of, not what is actually negotiated. Confusing the
        // two would promise a degraded link the throughput of a
        // perfect one.
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
        // Knowing nothing is an ordinary case: device over USB, Wi-Fi
        // turned off, or Android naming things differently. The
        // application must keep going without friction, not fail.
        Assert.Null(WifiLink.Parse(sortie));
    }

    [Fact]
    public void Une_sortie_absente_ne_rend_rien()
    {
        Assert.Null(WifiLink.Parse(null));
    }

    [Fact]
    public void Un_standard_annonce_par_un_nombre_ne_gene_pas()
    {
        // Captured on a Mi 9T Pro running Android 11: it writes
        // "Wi-Fi standard: 4" where the 13T Pro writes "11n". And it
        // is on 2.4 GHz, which the summary must report.
        const string releve =
            "WifiInfo: SSID: tkt-home, Wi-Fi standard: 4, RSSI: -61, Link speed: 144Mbps, "
            + "Tx Link speed: 144Mbps, Frequency: 2462MHz, Net ID: 5";

        var link = WifiLink.Parse(releve);

        Assert.NotNull(link);
        Assert.Equal("4", link!.Standard);
        Assert.Equal(2462, link.FrequencyMhz);
        Assert.Equal(-61, link.Rssi);
        Assert.True(link.Is24GHz);
    }

    /// <summary>
    /// The threshold, taken from both sides. It moved here from the
    /// health check so the vitals band and the findings could not drift
    /// apart, and an inclusive comparison is what the health check
    /// always did: this pins it so the move cannot have changed it.
    /// </summary>
    [Fact]
    public void Le_seuil_d_encombrement_est_inclusif()
    {
        Assert.True(Liaison(WifiLink.Crowded).IsCrowded);
        Assert.False(Liaison(WifiLink.Crowded - 0.001).IsCrowded);
    }

    [Fact]
    public void Un_canal_propre_n_est_pas_encombre()
    {
        Assert.False(Liaison(0.05).IsCrowded);
    }

    [Fact]
    public void Un_canal_qui_perd_quatre_trames_sur_dix_est_encombre()
    {
        Assert.True(Liaison(0.387).IsCrowded);
    }

    private static WifiLink Liaison(double retryShare) =>
        new(LinkSpeedMbps: 866, FrequencyMhz: 5220, Standard: "11ac", Rssi: -59, RetryShare: retryShare);
}
