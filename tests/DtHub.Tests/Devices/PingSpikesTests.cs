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

    [Fact]
    public void Quinze_secondes_sans_reponse_apres_une_reponse_sont_une_liaison_perdue()
    {
        // The link dies without a word, the PC keeps waiting for frames: the
        // window stayed grey with no message (2026-10-26).
        var spikes = new PingSpikes();

        spikes.Add(Debut, 3);

        for (var s = 1; s <= 15; s++)
        {
            spikes.Add(Debut.AddSeconds(s), null);
        }

        Assert.False(spikes.LinkLost(Debut.AddSeconds(14)));
        Assert.True(spikes.LinkLost(Debut.AddSeconds(15)));
    }

    [Fact]
    public void Trois_secondes_sans_reponse_rendent_la_liaison_muette()
    {
        // Shown at once as waiting for the phone, without closing the window:
        // a Wi-Fi hiccup comes back within a few seconds.
        var spikes = new PingSpikes();

        spikes.Add(Debut, 3);

        Assert.False(spikes.Silent(Debut.AddSeconds(2)));
        Assert.True(spikes.Silent(Debut.AddSeconds(3)));
        Assert.False(spikes.LinkLost(Debut.AddSeconds(3)));
    }

    [Fact]
    public void Un_telephone_qui_n_a_jamais_repondu_n_est_pas_declare_perdu()
    {
        // A network that drops pings must not close a window that works.
        var spikes = new PingSpikes();

        spikes.Add(Debut, null);

        Assert.False(spikes.LinkLost(Debut.AddMinutes(5)));
    }

    [Fact]
    public void Une_reponse_remet_la_liaison_en_vie()
    {
        var spikes = new PingSpikes();

        spikes.Add(Debut, 3);
        spikes.Add(Debut.AddSeconds(14), 900);

        Assert.False(spikes.LinkLost(Debut.AddSeconds(20)));
    }

    [Theory]
    [InlineData("192.168.1.16:34169", "192.168.1.16")]
    [InlineData("adb-SERIAL0123456789-1V3FXQ._adb-tls-connect._tcp", null)]
    [InlineData("12345678", null)]
    [InlineData("1a2b3c4d", null)]
    public void Seul_un_appareil_branche_par_son_adresse_se_ping(string serial, string? expected)
    {
        // A bare number would read as an IPv4 address, and a USB serial
        // can be one: only "a.b.c.d:port", the adb TCP form, counts.
        Assert.Equal(expected, PingSpikes.HostOf(serial));
    }
}
