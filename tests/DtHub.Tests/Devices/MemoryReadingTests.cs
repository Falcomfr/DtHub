using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class MemoryReadingTests
{
    /// <summary>
    /// Real outputs, captured on 2026-09-27 on the Xiaomi 13T Pro of the
    /// development machine (Android 16), two game windows open, a few
    /// minutes after both games were restarted. Copied character for
    /// character, carriage returns included: that is how adb hands them.
    /// </summary>
    private const string FacteurReel = "NORMAL\r\n";

    private const string MeminfoReel =
        "MemTotal:       11616040 kB\r\nMemFree:          161396 kB\r\nMemAvailable:    4913428 kB\r\n";

    [Fact]
    public void Le_releve_reel_se_lit_et_ne_dit_rien()
    {
        var memory = MemoryReading.Parse(FacteurReel, MeminfoReel);

        Assert.NotNull(memory);
        Assert.Equal(MemoryPressure.Normal, memory.Level);
        Assert.Equal(4913428L * 1024, memory.AvailableBytes);
        Assert.Equal(11616040L * 1024, memory.TotalBytes);
        Assert.False(memory.IsLow);
        Assert.Null(memory.Describe());
    }

    [Theory]
    [InlineData("MODERATE", MemoryPressure.Moderate)]
    [InlineData("LOW", MemoryPressure.Low)]
    [InlineData("CRITICAL", MemoryPressure.Critical)]
    public void Le_verdict_d_android_est_pris_tel_quel(string factor, MemoryPressure expected)
    {
        // Constructed from the real line: Android's own verdict, not a
        // threshold of ours, since it is the one it trims apps by.
        var memory = MemoryReading.Parse(factor + "\r\n", MeminfoReel);

        Assert.Equal(expected, memory!.Level);
    }

    [Fact]
    public void Moderee_ne_merite_pas_un_mot()
    {
        // Android sits at moderate often and for long, and nothing lags
        // for it: naming it would cry wolf.
        var memory = MemoryReading.Parse("MODERATE", MeminfoReel);

        Assert.False(memory!.IsLow);
        Assert.Null(memory.Describe());
    }

    [Fact]
    public void Saturee_et_critique_se_disent_avec_la_memoire_disponible()
    {
        var low = MemoryReading.Parse("LOW", MeminfoReel)!;
        var critical = MemoryReading.Parse("CRITICAL", MeminfoReel)!;

        Assert.True(low.IsLow);
        Assert.Contains(low.AvailableGigabytes.ToString(System.Globalization.CultureInfo.CurrentCulture), low.Describe(), StringComparison.Ordinal);
        Assert.NotEqual(low.Describe(), critical.Describe());
    }

    [Theory]
    [InlineData(20, MemoryPressure.Normal)]
    [InlineData(8, MemoryPressure.Low)]
    [InlineData(3, MemoryPressure.Critical)]
    public void Sans_verdict_d_android_la_part_disponible_tranche(int percentFree, MemoryPressure expected)
    {
        // Android 11 has no "am memory-factor": it answers with an error
        // line. Constructed, not captured, no phone at hand runs it.
        var total = 8_000_000L;
        var meminfo = $"MemTotal: {total} kB\nMemAvailable: {total * percentFree / 100} kB\n";

        var memory = MemoryReading.Parse("Unknown command: memory-factor", meminfo);

        Assert.Equal(expected, memory!.Level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MemFree: 161396 kB")]
    public void Sans_la_memoire_totale_et_disponible_il_n_y_a_pas_de_releve(string? meminfo)
    {
        Assert.Null(MemoryReading.Parse(FacteurReel, meminfo));
    }

    [Fact]
    public void La_revue_de_sante_nomme_la_memoire_saturee()
    {
        var low = MemoryReading.Parse("LOW", MeminfoReel);
        var critical = MemoryReading.Parse("CRITICAL", MeminfoReel);

        var warning = Assert.Single(DeviceHealth.Review(null, null, null, null, memory: low));
        var serious = Assert.Single(DeviceHealth.Review(null, null, null, null, memory: critical));

        Assert.Equal(HealthSeverity.Warning, warning.Severity);
        Assert.Equal(HealthSeverity.Serious, serious.Severity);
    }

    [Fact]
    public void Une_memoire_normale_ne_fait_aucun_constat()
    {
        Assert.Empty(DeviceHealth.Review(null, null, null, null, memory: MemoryReading.Parse(FacteurReel, MeminfoReel)));
    }
}
