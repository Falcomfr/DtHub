using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class BitrateAdviceTests
{
    [Theory]
    // Les références publiées par YouTube pour du H.264 de bonne facture.
    [InlineData(1920, 1080, 60, 12000)]
    [InlineData(2560, 1440, 60, 24000)]
    [InlineData(3840, 2160, 60, 53000)]
    public void Les_references_du_metier_sont_jugees_confortables(int w, int h, int fps, int kbps)
    {
        Assert.Equal(BitrateVerdict.Comfortable, BitrateAdvice.Read(w, h, fps, kbps).Verdict);
    }

    [Fact]
    public void L_inversion_historique_est_rattrapee()
    {
        // Le défaut réel de ce projet : les anciens paliers donnaient au mode
        // « maximale » cinq fois et demie moins de bits par pixel qu'au mode
        // « basse ». Les deux débits pris isolément semblaient pourtant
        // raisonnables, et c'est bien là ce que le verdict doit dénoncer.
        var basse = BitrateAdvice.Read(1280, 720, 30, 2500);
        var maximale = BitrateAdvice.Read(3840, 2160, 120, 16000);

        Assert.Equal(BitrateVerdict.Comfortable, basse.Verdict);
        Assert.Equal(BitrateVerdict.Insufficient, maximale.Verdict);
        Assert.True(maximale.BitsPerPixel < basse.BitsPerPixel);
    }

    [Fact]
    public void Le_bon_codec_n_est_pas_puni()
    {
        // À débit égal, H.265 rend mieux. Un utilisateur qui y passe ne doit
        // pas voir son verdict se dégrader.
        const int w = 1920, h = 1080, fps = 60, kbps = 7000;

        var avc = BitrateAdvice.Read(w, h, fps, kbps, "h264");
        var hevc = BitrateAdvice.Read(w, h, fps, kbps, "h265");

        Assert.Equal(BitrateVerdict.Tight, avc.Verdict);
        Assert.Equal(BitrateVerdict.Comfortable, hevc.Verdict);

        // La mesure brute, elle, ne bouge pas : c'est le même débit.
        Assert.Equal(avc.BitsPerPixel, hevc.BitsPerPixel, 6);
    }

    [Fact]
    public void Un_debit_demesure_est_dit_pour_ce_qu_il_est()
    {
        // Cinquante mégabits en 720p : rien ne s'y verra de plus, et sur deux
        // comptes en Wi-Fi la liaison le paiera.
        Assert.Equal(
            BitrateVerdict.Generous,
            BitrateAdvice.Read(1280, 720, 30, 50000).Verdict);
    }

    [Theory]
    [InlineData(0, 1080, 60, 12000)]
    [InlineData(1920, 0, 60, 12000)]
    [InlineData(1920, 1080, 0, 12000)]
    [InlineData(1920, 1080, 60, 0)]
    [InlineData(-1920, -1080, -60, -12000)]
    public void Un_reglage_incomplet_ne_leve_pas(int w, int h, int fps, int kbps)
    {
        // Le panneau appelle à chaque frappe, y compris sur un champ à demi
        // effacé. La phrase est alors vide plutôt que fausse.
        var reading = BitrateAdvice.Read(w, h, fps, kbps);

        Assert.Empty(reading.Summary);
        Assert.Equal(0, reading.BitsPerPixel);
    }

    [Fact]
    public void La_phrase_s_ecrit_en_francais()
    {
        // Virgule décimale, quelle que soit la culture de la machine : le test
        // passerait sinon ici et échouerait sur une machine anglaise.
        var reading = BitrateAdvice.Read(1920, 1080, 60, 12000);

        Assert.Contains("0,096", reading.Summary, StringComparison.Ordinal);
        Assert.Contains("confortable", reading.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Chaque_verdict_a_un_mot()
    {
        foreach (var verdict in Enum.GetValues<BitrateVerdict>())
        {
            Assert.NotEmpty(BitrateAdvice.Label(verdict));
        }
    }
}
