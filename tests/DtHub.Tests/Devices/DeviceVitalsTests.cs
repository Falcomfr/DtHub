using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class DeviceVitalsTests
{
    private static DeviceVitals Releve(int percent = 64) => new(
        new BatteryReading(percent, Charging: false, Celsius: 31.2),
        new ThermalReading(Status: 0, SkinCelsius: 34.4),
        new StorageReading(313_535_476L * 1024),
        new WifiLink(LinkSpeedMbps: 866, FrequencyMhz: 5220, Standard: "11ac", Rssi: -59, RetryShare: 0.05));

    /// <summary>
    /// The reason this is a record rather than a class. The device
    /// sweep runs every two to six seconds and hands the same four
    /// readings over and over, since each one is cached for a minute or
    /// more upstream. Value equality is what lets the view model drop
    /// an identical sweep without raising a single property.
    /// </summary>
    [Fact]
    public void Deux_releves_identiques_sont_egaux()
    {
        Assert.Equal(Releve(), Releve());
    }

    [Fact]
    public void Un_releve_qui_change_n_est_plus_egal()
    {
        Assert.NotEqual(Releve(), Releve(percent: 63));
    }

    [Fact]
    public void Un_appareil_dont_on_ne_sait_rien_est_vide()
    {
        Assert.True(new DeviceVitals(null, null, null, null).IsEmpty);
    }

    [Fact]
    public void Une_seule_lecture_suffit_a_ne_plus_etre_vide()
    {
        Assert.False(new DeviceVitals(null, new ThermalReading(0, null), null, null).IsEmpty);
    }
}
