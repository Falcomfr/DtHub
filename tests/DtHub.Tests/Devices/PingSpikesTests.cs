using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class PingSpikesTests
{
    private static readonly DateTimeOffset Debut = new(2026, 10, 3, 12, 53, 0, TimeSpan.Zero);

    [Fact]
    public void Seuls_les_pics_de_la_derniere_minute_comptent()
    {
        // The 2026-10-03 run: answers at 3 or 4 ms, and spikes at 210 ms.
        var spikes = new PingSpikes();

        spikes.Add(Debut, 210);
        spikes.Add(Debut.AddSeconds(30), 4);
        spikes.Add(Debut.AddSeconds(40), 210);
        spikes.Add(Debut.AddSeconds(50), 150);

        Assert.Equal((3, 210L), spikes.Since(Debut.AddSeconds(55)));

        // The first one is now more than a minute old.
        Assert.Equal((2, 210L), spikes.Since(Debut.AddSeconds(61)));
    }

    [Fact]
    public void Une_seule_coupure_ne_compte_qu_une_fois()
    {
        // Three pings lost in a row are one hiccup, not a radio that keeps
        // dozing: only spikes that come back count, one per ten seconds.
        var spikes = new PingSpikes();

        spikes.Add(Debut, null);
        spikes.Add(Debut.AddSeconds(1), null);
        spikes.Add(Debut.AddSeconds(2), 300);

        Assert.Equal((1, (long)PingSpikes.Timeout.TotalMilliseconds), spikes.Since(Debut.AddSeconds(3)));
    }

    [Fact]
    public void Une_reponse_perdue_est_un_pic()
    {
        var spikes = new PingSpikes();

        spikes.Add(Debut, null);

        Assert.Equal((1, (long)PingSpikes.Timeout.TotalMilliseconds), spikes.Since(Debut));
    }

    [Fact]
    public void Sans_pic_rien_ne_compte()
    {
        var spikes = new PingSpikes();

        spikes.Add(Debut, 3);
        spikes.Add(Debut.AddSeconds(1), 99);

        Assert.Equal((0, 0L), spikes.Since(Debut.AddSeconds(2)));
    }

    [Theory]
    [InlineData("192.168.1.16:34169", "192.168.1.16")]
    [InlineData("adb-CMBU79RCINVSFYUO-1V3FXQ._adb-tls-connect._tcp", null)]
    [InlineData("12345678", null)]
    [InlineData("1a2b3c4d", null)]
    public void Seul_un_appareil_branche_par_son_adresse_se_ping(string serial, string? expected)
    {
        // A bare number would read as an IPv4 address, and a USB serial
        // can be one: only "a.b.c.d:port", the adb TCP form, counts.
        Assert.Equal(expected, PingSpikes.HostOf(serial));
    }
}
