using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class ThermalReadingTests
{
    /// <summary>
    /// Captured as-is on the reference phone, a Xiaomi 13T Pro
    /// running Android 16. The status number is the only thing that
    /// changes from one case to another.
    /// </summary>
    private static string Dumpsys(int statut) => $$"""
IsStatusOverride: false
ThermalEventListeners:
	callbacks: 4
	killed: false
	broadcasts count: -1
ThermalStatusListeners:
	callbacks: 7
	killed: false
	broadcasts count: -1
Thermal Status: {{statut}}
Cached temperatures:
	Temperature{mValue=84.208, mType=0, mName=CPU, mStatus=0}
	Temperature{mValue=84.208, mType=1, mName=GPU, mStatus=0}
	Temperature{mValue=83.769, mType=9, mName=NPU, mStatus=0}
	Temperature{mValue=48.517, mType=3, mName=SKIN, mStatus=0}
	Temperature{mValue=42.2, mType=2, mName=BATTERY, mStatus=0}
HAL Ready: true
Current temperatures from HAL:
	Temperature{mValue=57.9, mType=0, mName=CPU, mStatus=0}
	Temperature{mValue=34.351, mType=3, mName=SKIN, mStatus=0}
Temperature static thresholds from HAL:
	TemperatureThreshold{mType=3, mName=SKIN, mHotThrottlingThresholds=[NaN, NaN, NaN, 50.0, 70.0, 80.0, 90.0]}
""";

    [Fact]
    public void L_etat_et_la_temperature_de_surface_sont_lus()
    {
        var reading = ThermalReading.Parse(Dumpsys(0));

        Assert.NotNull(reading);
        Assert.Equal(0, reading.Status);
        Assert.Equal(34.351, reading.SkinCelsius);
    }

    [Fact]
    public void La_temperature_lue_est_celle_de_l_instant_et_non_celle_du_cache()
    {
        // Captured on the reference phone: the cache reported 48.5°
        // at the moment when the current reading gave 34.4. Taking
        // the first of the two would have put a wrong figure in the
        // log.
        var reading = ThermalReading.Parse(Dumpsys(0));

        Assert.NotNull(reading);
        Assert.NotEqual(48.517, reading.SkinCelsius);
    }

    [Fact]
    public void La_temperature_lue_est_celle_de_la_surface_et_non_du_processeur()
    {
        // The processor showed 84.2° while the device was not
        // throttling anything at all: it is the surface temperature
        // that says what the hand feels and what the system
        // watches.
        var reading = ThermalReading.Parse(Dumpsys(0));

        Assert.NotNull(reading);
        Assert.NotEqual(84.208, reading.SkinCelsius);
        Assert.NotEqual(57.9, reading.SkinCelsius);
    }

    [Fact]
    public void Une_sortie_sans_section_courante_retombe_sur_la_lecture_unique()
    {
        var reading = ThermalReading.Parse(
            "Thermal Status: 1\nCached temperatures:\n\tTemperature{mValue=41.5, mType=3, mName=SKIN}");

        Assert.Equal(41.5, reading?.SkinCelsius);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    public void Le_bridage_commence_au_palier_modere(int statut, bool bride)
    {
        var reading = ThermalReading.Parse(Dumpsys(statut));

        Assert.NotNull(reading);
        Assert.Equal(bride, reading.IsThrottling);
    }

    [Fact]
    public void Rien_n_est_dit_tant_que_rien_n_est_bride()
    {
        Assert.Null(ThermalReading.Parse(Dumpsys(0))!.Describe());
        Assert.Null(ThermalReading.Parse(Dumpsys(1))!.Describe());
    }

    [Fact]
    public void Le_message_durcit_au_bridage_lourd()
    {
        var modere = ThermalReading.Parse(Dumpsys(2))!.Describe();
        var lourd = ThermalReading.Parse(Dumpsys(4))!.Describe();

        Assert.False(string.IsNullOrWhiteSpace(modere));
        Assert.False(string.IsNullOrWhiteSpace(lourd));
        Assert.NotEqual(modere, lourd);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Thermal Status: inconnu")]
    [InlineData("dumpsys: service thermalservice does not exist")]
    public void Ce_qui_ne_dit_pas_l_etat_ne_rend_rien(string? sortie)
    {
        // Knowing nothing about the heat is an ordinary case, not a
        // fault.
        Assert.Null(ThermalReading.Parse(sortie));
    }

    [Fact]
    public void Un_etat_sans_temperature_reste_lisible()
    {
        var reading = ThermalReading.Parse("Thermal Status: 3");

        Assert.NotNull(reading);
        Assert.Equal(3, reading.Status);
        Assert.Null(reading.SkinCelsius);
    }

    /// <summary>
    /// Captured on the reference phone on 2026-10-03 at 15:43, two game
    /// windows open, plugged in at 100 %, while it lagged: the global
    /// status says 0, the current skin sensor says 3 at 50.8 °, and the
    /// big cores sat at 1.1 and 1.3 GHz out of 3.0 and 3.35.
    /// </summary>
    private const string BrideParLaSurface = """
IsStatusOverride: false
ThermalEventListeners:
	callbacks: 6
	killed: false
	broadcasts count: -1
ThermalStatusListeners:
	callbacks: 9
	killed: false
	broadcasts count: -1
Thermal Status: 0
Cached temperatures:
	Temperature{mValue=87.556, mType=0, mName=CPU, mStatus=3}
	Temperature{mValue=86.999, mType=1, mName=GPU, mStatus=3}
	Temperature{mValue=86.999, mType=9, mName=NPU, mStatus=3}
	Temperature{mValue=49.202, mType=3, mName=SKIN, mStatus=0}
	Temperature{mValue=42.3, mType=2, mName=BATTERY, mStatus=0}
	Temperature{mValue=46.489, mType=5, mName=POWER_AMPLIFIER, mStatus=0}
HAL Ready: true
Current temperatures from HAL:
	Temperature{mValue=62.842, mType=0, mName=CPU, mStatus=0}
	Temperature{mValue=62.842, mType=1, mName=GPU, mStatus=0}
	Temperature{mValue=44.3, mType=2, mName=BATTERY, mStatus=0}
	Temperature{mValue=50.77, mType=3, mName=SKIN, mStatus=3}
	Temperature{mValue=49.696, mType=5, mName=POWER_AMPLIFIER, mStatus=0}
	Temperature{mValue=62.77, mType=9, mName=NPU, mStatus=0}
Current cooling devices from HAL:
""";

    [Fact]
    public void Une_surface_au_palier_lourd_se_dit_meme_quand_l_etat_global_se_tait()
    {
        // Xiaomi throttles on the skin sensor and leaves the global status
        // at zero: read alone, it said nothing for the half hour the phone
        // ran at a third of its speed.
        var reading = ThermalReading.Parse(BrideParLaSurface);

        Assert.NotNull(reading);
        Assert.Equal(ThermalReading.Severe, reading.Status);
        Assert.Equal(50.77, reading.SkinCelsius);
    }

    [Fact]
    public void Le_palier_du_processeur_en_cache_ne_compte_pas()
    {
        // The cached CPU at 87.6 ° says 3 as well, but the processor's
        // figures were already seen high with nothing throttled: only the
        // current skin counts. Lowered to 0 here, nothing is left to say.
        var reading = ThermalReading.Parse(
            BrideParLaSurface.Replace("mName=SKIN, mStatus=3", "mName=SKIN, mStatus=0", StringComparison.Ordinal));

        Assert.Equal(0, reading!.Status);
    }
}
