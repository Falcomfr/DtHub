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
    public void La_bulle_porte_tous_les_constats_quand_le_bandeau_n_en_montre_qu_un()
    {
        // The case measured on a real phone: three findings at the same
        // time, of which only one used to appear, the other two
        // nowhere.
        var findings = DeviceHealth.Review(
            null,
            Battery(6),
            null,
            Link(2437),
            lockedWindows: true,
            unpreparedBattery: true);

        var tout = DeviceHealth.Every(findings);

        Assert.Equal(4, findings.Count);
        Assert.Equal(DeviceHealth.Worst(findings), findings[0].Message);
        Assert.All(findings, f => Assert.Contains(f.Message, tout, StringComparison.Ordinal));

        // One line per finding, and nothing more.
        Assert.Equal(
            findings.Count,
            tout!.Split(Environment.NewLine, StringSplitOptions.None).Length);
    }

    [Fact]
    public void Sans_constat_la_bulle_ne_dit_rien()
    {
        Assert.Null(DeviceHealth.Every(DeviceHealth.Review(null, null, null, null)));
    }

    [Fact]
    public void Un_appareil_qui_refuse_les_clics_le_dit()
    {
        var mort = DeviceHealth.Review(null, null, null, null, deadInput: true);

        Assert.Equal(HealthSeverity.Serious, Assert.Single(mort).Severity);
        Assert.Empty(DeviceHealth.Review(null, null, null, null));
    }

    [Fact]
    public void Le_cadenas_passe_devant_les_clics_morts()
    {
        // Both are serious, but as long as the lock is there we cannot
        // even see the game: there is nowhere to click.
        var deux = DeviceHealth.Review(null, null, null, null, lockedWindows: true, deadInput: true);
        var cadenas = DeviceHealth.Review(null, null, null, null, lockedWindows: true);

        Assert.Equal(2, deux.Count);
        Assert.Equal(Assert.Single(cadenas).Message, deux[0].Message);
    }

    [Fact]
    public void Une_preparation_batterie_manquante_se_dit()
    {
        var sans = DeviceHealth.Review(null, null, null, null, unpreparedBattery: true);

        Assert.Equal(HealthSeverity.Warning, Assert.Single(sans).Severity);
        Assert.Empty(DeviceHealth.Review(null, null, null, null));
    }

    [Fact]
    public void Le_cadenas_passe_devant_tout_le_reste()
    {
        // The windows are open and do not show the game: nothing else
        // matters while that lasts, not even a battery on its last
        // legs.
        var findings = DeviceHealth.Review(
            new ThermalReading(3, 44.0),
            Battery(5),
            Storage(1),
            Link(2437),
            lockedWindows: true);

        Assert.Equal(HealthSeverity.Serious, findings[0].Severity);
        Assert.Equal(findings[0].Message, DeviceHealth.Worst(findings));
        Assert.NotEqual(findings[0].Message, findings[1].Message);
    }

    [Fact]
    public void Sans_cadenas_rien_n_est_dit_du_verrouillage()
    {
        var avec = DeviceHealth.Review(null, null, null, null, lockedWindows: true);
        var sans = DeviceHealth.Review(null, null, null, null);

        Assert.Single(avec);
        Assert.Empty(sans);
    }

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
        // Critical battery and moderate heat: it is the battery that
        // will cut the session short, so it is the one that must
        // speak up.
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
        // It breaks nothing and is already compensated for by the
        // video buffer: it is reported so that "ça saccade" ("it's
        // stuttering") has an answer, not to raise an alarm.
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
        // Two texts for the same fact would eventually drift apart.
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

    [Fact]
    public void Le_cas_reel_du_second_appareil_ne_dit_que_la_bande()
    {
        // Mi 9T Pro captured in the field: 20 percent but charging, 16
        // gigabytes free, no heat, and a link on 2.4 GHz. Only one
        // finding should come out, and it is the mildest one.
        var findings = DeviceHealth.Review(
            new ThermalReading(0, null),
            new BatteryReading(20, Charging: true, Celsius: 36.2),
            new StorageReading(17159944L * 1024),
            new WifiLink(144, 2462, "4", -61, 0.0));

        var finding = Assert.Single(findings);

        Assert.Equal(HealthSeverity.Notice, finding.Severity);
    }
}
