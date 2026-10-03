using DtHub.Core.Devices;
using DtHub.Core.Localization;

namespace DtHub.Tests.Devices;

public class DeviceHealthTests
{
    private static BatteryReading Battery(int percent, bool charging = false) =>
        new(percent, charging, Celsius: 30);

    private static StorageReading Storage(double gigabytes) =>
        new((long)(gigabytes * 1024 * 1024 * 1024));

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
            lockedWindows: true,
            unpreparedBattery: true);

        var tout = DeviceHealth.Every(findings);

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Contains(f.Message, tout, StringComparison.Ordinal));

        // One line per finding, and nothing more.
        Assert.Equal(
            findings.Count,
            tout!.Split(Environment.NewLine, StringSplitOptions.None).Length);
    }

    [Fact]
    public void Sans_constat_la_bulle_ne_dit_rien()
    {
        Assert.Null(DeviceHealth.Every(DeviceHealth.Review(null, null, null)));
    }

    [Fact]
    public void Un_appareil_qui_refuse_les_clics_le_dit()
    {
        var mort = DeviceHealth.Review(null, null, null, deadInput: true);

        Assert.Equal(HealthSeverity.Serious, Assert.Single(mort).Severity);
        Assert.Empty(DeviceHealth.Review(null, null, null));
    }

    [Fact]
    public void Le_cadenas_passe_devant_les_clics_morts()
    {
        // Both are serious, but as long as the lock is there we cannot
        // even see the game: there is nowhere to click.
        var deux = DeviceHealth.Review(null, null, null, lockedWindows: true, deadInput: true);
        var cadenas = DeviceHealth.Review(null, null, null, lockedWindows: true);

        Assert.Equal(2, deux.Count);
        Assert.Equal(Assert.Single(cadenas).Message, deux[0].Message);
    }

    [Fact]
    public void Une_preparation_batterie_manquante_se_dit()
    {
        var sans = DeviceHealth.Review(null, null, null, unpreparedBattery: true);

        Assert.Equal(HealthSeverity.Warning, Assert.Single(sans).Severity);
        Assert.Empty(DeviceHealth.Review(null, null, null));
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
            lockedWindows: true);

        Assert.Equal(HealthSeverity.Serious, findings[0].Severity);
        Assert.NotEqual(findings[0].Message, findings[1].Message);
    }

    [Fact]
    public void Sans_cadenas_rien_n_est_dit_du_verrouillage()
    {
        var avec = DeviceHealth.Review(null, null, null, lockedWindows: true);
        var sans = DeviceHealth.Review(null, null, null);

        Assert.Single(avec);
        Assert.Empty(sans);
    }

    [Fact]
    public void Un_appareil_en_forme_n_a_rien_a_dire()
    {
        var findings = DeviceHealth.Review(
            new ThermalReading(0, 34.4),
            Battery(80),
            Storage(299));

        Assert.Empty(findings);
    }

    [Fact]
    public void Un_appareil_muet_sur_tout_n_invente_rien()
    {
        Assert.Empty(DeviceHealth.Review(null, null, null));
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
            Storage(299));

        Assert.Equal(HealthSeverity.Serious, findings[0].Severity);
        Assert.Contains("5", findings[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_telephone_branche_ne_figure_pas_au_bilan()
    {
        Assert.Empty(DeviceHealth.Review(null, Battery(4, charging: true), null));
    }

    [Theory]
    [InlineData(5, HealthSeverity.Serious)]
    [InlineData(10, HealthSeverity.Serious)]
    [InlineData(11, HealthSeverity.Warning)]
    [InlineData(20, HealthSeverity.Warning)]
    public void La_batterie_durcit_sous_dix_pour_cent(int percent, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(null, Battery(percent), null);

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(0.3, HealthSeverity.Serious)]
    [InlineData(1.5, HealthSeverity.Warning)]
    public void La_place_libre_durcit_au_bord_du_vide(double gigabytes, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(null, null, Storage(gigabytes));

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Theory]
    [InlineData(ThermalReading.Severe, HealthSeverity.Serious)]
    [InlineData(ThermalReading.Throttling, HealthSeverity.Warning)]
    public void La_chaleur_durcit_au_bridage_lourd(int status, HealthSeverity expected)
    {
        var findings = DeviceHealth.Review(new ThermalReading(status, 50), null, null);

        Assert.Equal(expected, Assert.Single(findings).Severity);
    }

    [Fact]
    public void Les_messages_viennent_des_lectures_et_ne_sont_pas_reecrits()
    {
        // Two texts for the same fact would eventually drift apart.
        var battery = Battery(7);

        var findings = DeviceHealth.Review(null, battery, null);

        Assert.Equal(battery.Describe(), Assert.Single(findings).Message);
    }

    [Fact]
    public void Trois_constats_graves_se_suivent_sans_se_perdre()
    {
        var findings = DeviceHealth.Review(
            new ThermalReading(ThermalReading.Severe, 60),
            Battery(3),
            Storage(0.2));

        Assert.Equal(3, findings.Count);
        Assert.All(findings, f => Assert.Equal(HealthSeverity.Serious, f.Severity));
    }

    [Fact]
    public void Le_cas_reel_du_second_appareil_ne_dit_rien()
    {
        // Mi 9T Pro captured in the field: 20 percent but charging, 16
        // gigabytes free, no heat. Its 2.4 GHz link used to make a line,
        // and now only colours the link's chip.
        Assert.Empty(DeviceHealth.Review(
            new ThermalReading(0, null),
            new BatteryReading(20, Charging: true, Celsius: 36.2),
            new StorageReading(17159944L * 1024)));
    }

    private static PhoneStrain Strain(double busy = 0.40, double game = 0.20, double reclaim = 0, double days = 0.1) =>
        new(busy, game, reclaim, TimeSpan.FromDays(days));

    [Fact]
    public void Un_telephone_qui_court_apres_sa_memoire_le_dit_avec_ses_jours_sans_redemarrage()
    {
        // 2026-10-03: kswapd0 at 38 % of a core, four days without a
        // restart, and 35 % of memory available, so Android's verdict
        // said nothing.
        var finding = Assert.Single(DeviceHealth.Review(null, null, null, strain: Strain(reclaim: 0.38, days: 4.1)));

        Assert.Equal(HealthSeverity.Warning, finding.Severity);
        Assert.Equal(Strings.Format("PhoneMemoryStrain", Strings.Format("PhoneUptime", 4)), finding.Message);
    }

    [Fact]
    public void Redemarre_recemment_le_message_ne_parle_pas_de_jours()
    {
        var finding = Assert.Single(DeviceHealth.Review(null, null, null, strain: Strain(reclaim: 0.38, days: 1)));

        Assert.Equal(Strings.Format("PhoneMemoryStrain", string.Empty), finding.Message);
    }

    [Fact]
    public void Un_processeur_pris_par_le_jeu_conseille_de_baisser_les_graphismes()
    {
        var finding = Assert.Single(DeviceHealth.Review(null, null, null, strain: Strain(busy: 0.90, game: 0.60)));

        Assert.Equal(Strings.Format("PhoneGameHeavy", 60), finding.Message);
    }

    [Fact]
    public void Un_processeur_pris_par_autre_chose_conseille_de_fermer_les_applications()
    {
        var finding = Assert.Single(DeviceHealth.Review(null, null, null, strain: Strain(busy: 0.90, game: 0.20, days: 4)));

        Assert.Equal(Strings.Format("PhoneBusy", Strings.Format("PhoneUptime", 4)), finding.Message);
    }

    [Fact]
    public void Un_telephone_au_calme_ne_dit_rien()
    {
        Assert.Empty(DeviceHealth.Review(null, null, null, strain: Strain(), pcBusyPercent: 40));
    }

    [Fact]
    public void Un_pc_charge_le_dit()
    {
        var finding = Assert.Single(DeviceHealth.Review(null, null, null, pcBusyPercent: 92));

        Assert.Equal(Strings.Format("PcBusy", 92), finding.Message);
    }

    [Fact]
    public void Un_seul_constat_de_lag_et_c_est_le_telephone_qui_passe_devant()
    {
        // Asked for: do not bury the panel under findings. Two causes at
        // once give one line, the one that is easiest to fix.
        var findings = DeviceHealth.Review(
            null, null, null,
            strain: Strain(reclaim: 0.38), pcBusyPercent: 95);

        Assert.Equal(Strings.Format("PhoneMemoryStrain", string.Empty), Assert.Single(findings).Message);
    }

    [Fact]
    public void La_memoire_deja_signalee_n_est_pas_redite()
    {
        // Android's verdict already advised a restart: the same advice
        // under another name would be one line too many.
        var serree = MemoryReading.Parse("LOW", "MemTotal: 11616040 kB\r\nMemAvailable: 1161604 kB\r\n");

        var findings = DeviceHealth.Review(null, null, null, memory: serree, strain: Strain(reclaim: 0.38));

        Assert.Equal(serree!.Describe(), Assert.Single(findings).Message);
    }
}
