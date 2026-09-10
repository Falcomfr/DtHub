using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class ThermalReadingTests
{
    /// <summary>
    /// Relevé tel quel sur le téléphone de référence, un Xiaomi 13T Pro sous
    /// Android 16. Le nombre d'état est le seul qui change d'un cas à l'autre.
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
        // Relevé sur le téléphone de référence : le cache annonçait 48,5 ° au
        // moment où la lecture courante en donnait 34,4. Prendre la première
        // des deux aurait mis un chiffre faux dans le journal.
        var reading = ThermalReading.Parse(Dumpsys(0));

        Assert.NotNull(reading);
        Assert.NotEqual(48.517, reading.SkinCelsius);
    }

    [Fact]
    public void La_temperature_lue_est_celle_de_la_surface_et_non_du_processeur()
    {
        // Le processeur affichait 84,2 ° pendant que l'appareil ne bridait
        // rien : c'est la surface qui dit ce que la main sent et ce que le
        // système surveille.
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
        // Ne rien savoir de la chaleur est un cas ordinaire, pas une faute.
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
}
