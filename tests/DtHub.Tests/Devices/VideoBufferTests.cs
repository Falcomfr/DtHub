using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class VideoBufferTests
{
    private static WifiLink Lien(int rssi, int frequence = 2412, double reemissions = 0.12) =>
        new(144, frequence, frequence < 3000 ? "11n" : "11ac", rssi, reemissions);

    [Fact]
    public void Une_liaison_filaire_n_a_rien_a_compenser()
    {
        // L'USB n'a ni voisin ni interférence. Y ajouter un retard reviendrait
        // à gâcher la seule liaison qui n'en demande pas.
        Assert.Equal(VideoBuffer.None, VideoBuffer.MillisecondsFor(null));
    }

    [Fact]
    public void La_liaison_du_poste_recoit_un_tampon_utile()
    {
        // Le cas mesuré : 2,4 GHz, -66 dBm, 12 % de réémissions, latence
        // sautant de 4 à 223 ms. Il faut de quoi absorber les pointes
        // ordinaires sans rendre le clic mou.
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
        // Au-delà, on couvrirait les pires pointes au prix d'un clic mou, ce
        // qui est précisément la gêne qu'on cherche à supprimer.
        var pire = VideoBuffer.MillisecondsFor(Lien(-90, reemissions: 0.60));

        Assert.Equal(VideoBuffer.Ceiling, pire);
    }

    [Fact]
    public void La_bande_encombree_pese_a_signal_egal()
    {
        // À puissance reçue identique, la 2,4 GHz partage son temps d'antenne
        // avec tout le voisinage, ce que la seule puissance ne dit pas.
        var deuxQuatre = VideoBuffer.MillisecondsFor(Lien(-60));
        var cinq = VideoBuffer.MillisecondsFor(Lien(-60, frequence: 5520));

        Assert.True(cinq < deuxQuatre, $"5 GHz {cinq} devrait être sous 2,4 GHz {deuxQuatre}.");
    }
}
