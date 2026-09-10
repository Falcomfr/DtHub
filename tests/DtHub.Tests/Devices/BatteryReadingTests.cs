using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class BatteryReadingTests
{
    /// <summary>
    /// Relevé au caractère près sur le Xiaomi 13T Pro, Android 16,
    /// <c>adb shell dumpsys battery</c>. Les lignes intercalaires longues sont
    /// gardées : ce sont elles qui piègent une analyse trop pressée.
    /// </summary>
    private const string Releve = """
        Current Battery Service state:
          AC powered: false
          USB powered: false
          Wireless powered: false
          Dock powered: false
          Max charging current: 0
         Time when the latest updated value of the Max charging current was sent via battery changed broadcast: +11s154ms
          Max charging voltage: 0
          Charge counter: 4386000
          status: 3
          health: 2
          present: true
          level: 64
          scale: 100
          voltage: 4320
         The last voltage value sent via the battery changed broadcast: 4320
          temperature: 328
          technology: Li-poly
        """;

    [Fact]
    public void Le_niveau_la_charge_et_la_temperature_se_lisent()
    {
        var battery = BatteryReading.Parse(Releve);

        Assert.NotNull(battery);
        Assert.Equal(64, battery!.Percent);
        Assert.False(battery.Charging);
        Assert.Equal(32.8, battery.Celsius);
    }

    [Fact]
    public void Une_prise_branchee_se_voit()
    {
        var branche = Releve.Replace("AC powered: false", "AC powered: true", StringComparison.Ordinal);

        Assert.True(BatteryReading.Parse(branche)!.Charging);
    }

    [Theory]
    [InlineData("USB powered")]
    [InlineData("Wireless powered")]
    [InlineData("Dock powered")]
    public void Toutes_les_prises_comptent(string label)
    {
        var branche = Releve.Replace(label + ": false", label + ": true", StringComparison.Ordinal);

        Assert.True(BatteryReading.Parse(branche)!.Charging);
    }

    [Fact]
    public void Un_appareil_plein_sur_secteur_est_en_charge()
    {
        // C'est l'état relevé quand le téléphone reste branché : « status: 5 »
        // et la prise mise.
        var plein = Releve
            .Replace("status: 3", "status: 5", StringComparison.Ordinal)
            .Replace("AC powered: false", "AC powered: true", StringComparison.Ordinal)
            .Replace("level: 64", "level: 100", StringComparison.Ordinal);

        var battery = BatteryReading.Parse(plein);

        Assert.Equal(100, battery!.Percent);
        Assert.True(battery.Charging);
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(21, false)]
    [InlineData(20, true)]
    [InlineData(9, true)]
    public void Le_seuil_bas_est_a_vingt_pour_cent(int level, bool low)
    {
        var releve = Releve.Replace("level: 64", "level: " + level, StringComparison.Ordinal);

        Assert.Equal(low, BatteryReading.Parse(releve)!.IsLow);
    }

    [Fact]
    public void Un_appareil_branche_ne_dit_rien_meme_a_trois_pour_cent()
    {
        // Ce qui compte n'est pas le niveau, c'est le niveau qui baisse. Un
        // bandeau qui alerte alors que la prise est mise parle pour ne rien
        // dire, et on cesse de le lire.
        var branche = Releve
            .Replace("level: 64", "level: 3", StringComparison.Ordinal)
            .Replace("AC powered: false", "AC powered: true", StringComparison.Ordinal);

        var battery = BatteryReading.Parse(branche);

        Assert.False(battery!.IsLow);
        Assert.Null(battery.Describe());
    }

    [Fact]
    public void Le_message_durcit_sous_dix_pour_cent()
    {
        var bas = Releve.Replace("level: 64", "level: 15", StringComparison.Ordinal);
        var critique = Releve.Replace("level: 64", "level: 7", StringComparison.Ordinal);

        var premier = BatteryReading.Parse(bas)!.Describe();
        var second = BatteryReading.Parse(critique)!.Describe();

        Assert.NotNull(premier);
        Assert.NotNull(second);
        Assert.NotEqual(premier, second);
    }

    [Fact]
    public void Un_niveau_confortable_ne_dit_rien()
    {
        Assert.Null(BatteryReading.Parse(Releve)!.Describe());
    }

    [Fact]
    public void Une_echelle_qui_n_est_pas_cent_est_respectee()
    {
        // Elle vaut cent partout où on l'a vue, mais elle est déclarée : s'en
        // remettre à cent serait supposer ce que l'appareil dit déjà.
        var releve = Releve
            .Replace("level: 64", "level: 128", StringComparison.Ordinal)
            .Replace("scale: 100", "scale: 255", StringComparison.Ordinal);

        Assert.Equal(50, BatteryReading.Parse(releve)!.Percent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dumpsys: service battery does not exist")]
    [InlineData("Current Battery Service state:\n  present: true")]
    public void Une_sortie_inexploitable_ne_rend_rien(string? dumpsys)
    {
        Assert.Null(BatteryReading.Parse(dumpsys));
    }

    [Fact]
    public void Une_echelle_absurde_ne_rend_rien()
    {
        var releve = Releve.Replace("scale: 100", "scale: 0", StringComparison.Ordinal);

        Assert.Null(BatteryReading.Parse(releve));
    }

    [Fact]
    public void Une_batterie_sans_temperature_se_lit_quand_meme()
    {
        var releve = Releve.Replace("  temperature: 328", "  technology: Li-poly", StringComparison.Ordinal);

        var battery = BatteryReading.Parse(releve);

        Assert.NotNull(battery);
        Assert.Null(battery!.Celsius);
    }

    [Fact]
    public void Un_second_appareil_se_lit_aussi()
    {
        // Relevé au caractère près sur un Mi 9T Pro sous Android 11, qui
        // n'écrit pas la même chose que le 13T Pro : pas de ligne « Dock
        // powered », et l'appareil est en charge à vingt pour cent.
        const string releve = """
            Current Battery Service state:
              AC powered: true
              USB powered: false
              Wireless powered: false
              status: 2
              level: 20
              scale: 100
              temperature: 362
            """;

        var battery = BatteryReading.Parse(releve);

        Assert.NotNull(battery);
        Assert.Equal(20, battery!.Percent);
        Assert.Equal(36.2, battery.Celsius);

        // Vingt pour cent est le seuil, mais l'appareil est branché : il n'a
        // rien à dire. C'est la règle qui compte, éprouvée sur un vrai
        // appareil à un vrai niveau bas.
        Assert.True(battery.Charging);
        Assert.False(battery.IsLow);
        Assert.Null(battery.Describe());
    }
}
