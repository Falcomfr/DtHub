using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class VideoBufferTests
{
    private static WifiLink Lien(int rssi, int frequence = 2412, double reemissions = 0.12) =>
        new(144, frequence, frequence < 3000 ? "11n" : "11ac", rssi, reemissions, 50_000);

    [Fact]
    public void Une_liaison_filaire_n_a_rien_a_compenser()
    {
        // USB has neither neighbors nor interference. Adding a
        // delay to it would amount to spoiling the one connection
        // that does not need one.
        Assert.Equal(VideoBuffer.None, VideoBuffer.MillisecondsFor(null));
    }

    [Fact]
    public void La_liaison_du_poste_recoit_un_tampon_utile()
    {
        // The measured case: 2.4 GHz, -66 dBm, 12% retransmissions,
        // latency jumping from 4 to 223 ms. It takes enough buffer
        // to absorb ordinary spikes without making clicks feel
        // sluggish.
        var tampon = VideoBuffer.MillisecondsFor(Lien(-66));

        Assert.InRange(tampon, 40, VideoBuffer.Ceiling);
    }

    [Fact]
    public void Une_liaison_5_GHz_confortable_reste_presque_sans_retard()
    {
        var tampon = VideoBuffer.MillisecondsFor(Lien(-52, frequence: 5520, reemissions: 0.01));

        Assert.InRange(tampon, 0, 20);
    }

    [Fact]
    public void Un_signal_qui_faiblit_fait_grandir_le_tampon()
    {
        var fort = VideoBuffer.MillisecondsFor(Lien(-52));
        var moyen = VideoBuffer.MillisecondsFor(Lien(-63));
        var faible = VideoBuffer.MillisecondsFor(Lien(-75));

        Assert.True(fort < moyen, $"{fort} devrait être sous {moyen}.");
        Assert.True(moyen < faible, $"{moyen} devrait être sous {faible}.");
    }

    [Fact]
    public void Le_retard_ne_depasse_jamais_le_plafond()
    {
        // Beyond that, the worst spikes would be covered at the
        // cost of a sluggish click, which is precisely the
        // annoyance we are trying to remove.
        var pire = VideoBuffer.MillisecondsFor(Lien(-90, reemissions: 0.60));

        Assert.Equal(VideoBuffer.Ceiling, pire);
    }

    [Fact]
    public void La_bande_encombree_pese_a_signal_egal()
    {
        // At the same received power, 2.4 GHz shares its airtime
        // with the whole neighborhood, which power alone does not
        // tell.
        var deuxQuatre = VideoBuffer.MillisecondsFor(Lien(-60));
        var cinq = VideoBuffer.MillisecondsFor(Lien(-60, frequence: 5520));

        Assert.True(cinq < deuxQuatre, $"5 GHz {cinq} devrait être sous 2,4 GHz {deuxQuatre}.");
    }
}
