using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class DeviceHealthTests
{
    private static BatteryReading Battery(int percent, bool charging = false) =>
        new(percent, charging, Celsius: 30);

    private static StorageReading Storage(double gigabytes) =>
        new((long)(gigabytes * 1024 * 1024 * 1024));

    private static WifiLink Link(int frequency) =>
        new(LinkSpeedMbps: 144, FrequencyMhz: frequency, Standard: "11n", Rssi: -57, RetryShare: 0.1);

    [Fact]
    public void Un_appareil_en_forme_n_a_rien_a_dire()
    {
        var findings = DeviceHealth.Review(
            new ThermalReading(0, 34.4),
            Battery(80),
            Storage(299),
            Link(5180));

        Assert.Empty(findings);
        Assert.Null(DeviceHealth.Worst(findings));
    }

    [Fact]
    public void Un_appareil_muet_sur_tout_n_invente_rien()
    {
        Assert.Empty(DeviceHealth.Review(null, null, null, null));
    }

    [Fact]
    public void Le_plus_grave_vient_en_tete()
    {
        // Batterie critique et chaleur modérée : c'est la batterie qui coupera
        // la séance, c'est elle qui doit parler.
        var findings = DeviceHealth.Review(
            new ThermalReading(ThermalReading.Throttling, 44),
            Battery(5),
            Storage(299),
            Link(2412));

        Assert.Equal(HealthSeverity.Serious, findings[0].Severity);
        Assert.Contains("5", findings[0].Message, StringComparison.Ordinal);
        Assert.Equal(DeviceHealth.Worst(findings), findings[0].Message);
    }

    [Fact]
    public void La_liaison_ferme_toujours_la_marche()
    {
        // Elle ne casse rien et se compense déjà par le tampon vidéo : elle
        // est dite pour que « ça saccade » ait une réponse, pas pour alarmer.
        var findings = DeviceHealth.Review(null, Battery(15), null, Link(2412));

        Assert.Equal(2, findings.Count);
        Assert.Equal(HealthSeverity.Notice, findings[^1].Severity);
    }

    [Fact]
    public void Une_liaison_en_cinq_gigahertz_ne_dit_rien()
    {
        Assert.Empty(DeviceHealth.Review(null, null, null, Link(5180)));
    }

    [Fact]
    public void Un_telephone_branche_ne_figure_pas_au_bilan()
    {
        Assert.Empty(DeviceHealth.Review(null, Battery(4, charging: true), null, null));
    }

    [Theory]
    [InlineData(5, HealthSeverity.Serious)]
    [InlineData(10, HealthSeverity.Serious)]
    [InlineData(11, HealthSeverity.Warning)]
    [InlineData(20, HealthSeverity.Warning)]
    public void La_batterie_durcit_sous_dix_pour_cent(int percent, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(null, Battery(percent), null, null);

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(0.3, HealthSeverity.Serious)]
    [InlineData(1.5, HealthSeverity.Warning)]
    public void La_place_libre_durcit_au_bord_du_vide(double gigabytes, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(null, null, Storage(gigabytes), null);

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(ThermalReading.Severe, HealthSeverity.Serious)]
    [InlineData(ThermalReading.Throttling, HealthSeverity.Warning)]
    public void La_chaleur_durcit_au_bridage_lourd(int status, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(new ThermalReading(status, 50), null, null, null);

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Fact]
    public void Les_messages_viennent_des_lectures_et_ne_sont_pas_reecrits()
    {
        // Deux textes pour un même fait finiraient par diverger.
        var battery = Battery(7);

        var findings = DeviceHealth.Review(null, battery, null, null);

        Assert.Equal(battery.Describe(), Assert.Single(findings).Message);
    }

    [Fact]
    public void Trois_constats_graves_se_suivent_sans_se_perdre()
    {
        var findings = DeviceHealth.Review(
            new ThermalReading(ThermalReading.Severe, 60),
            Battery(3),
            Storage(0.2),
            Link(2412));

        Assert.Equal(4, findings.Count);
        Assert.Equal(3, findings.Count(f => f.Severity == HealthSeverity.Serious));
        Assert.Equal(HealthSeverity.Notice, findings[^1].Severity);
    }
}
