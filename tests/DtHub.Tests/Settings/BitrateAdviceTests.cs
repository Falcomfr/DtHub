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

/// <summary>
/// Le conseil ne juge pas un flux isolé : DT Hub ouvre plusieurs fenêtres sur
/// un seul téléphone et une seule liaison. C'est ce que ces tests fixent, et
/// c'est ce qui distingue ce panneau de celui d'un miroir simple.
/// </summary>
public class BitratePlanTests
{
    private const int Plafond = 25000;

    [Fact]
    public void Le_debit_se_deduit_de_la_finesse_et_de_la_taille()
    {
        var plan = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);

        // 0,09 x 1920 x 1080 x 60 / 1000 = 11197 kb/s.
        Assert.Equal(11197, plan.KbpsPerWindow);
        Assert.Equal(BitrateVerdict.Comfortable, plan.Verdict);
    }

    [Fact]
    public void La_liaison_porte_toutes_les_fenetres()
    {
        var seule = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);
        var trois = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 3, Plafond);

        // Le flux d'une fenêtre ne change pas ; ce que la liaison encaisse, si.
        Assert.Equal(seule.KbpsPerWindow, trois.KbpsPerWindow);
        Assert.Equal(seule.KbpsPerWindow * 3, trois.TotalKbps);

        Assert.DoesNotContain("comptes", seule.LinkSummary, StringComparison.Ordinal);
        Assert.Contains("3 comptes", trois.LinkSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_petite_fenetre_ne_recoit_pas_le_debit_d_une_grande()
    {
        // Le défaut qu'un débit absolu aurait réintroduit, et que le reste du
        // code avait déjà corrigé.
        var grande = BitrateAdvice.Plan(0.09, 2560, 1440, 60, "h264", 1, Plafond);
        var petite = BitrateAdvice.Plan(0.09, 1280, 720, 60, "h264", 1, Plafond);

        Assert.True(grande.KbpsPerWindow > petite.KbpsPerWindow);

        // Et pourtant les deux sont jugées pareil : c'est tout l'intérêt de
        // raisonner en bits par pixel.
        Assert.Equal(grande.Verdict, petite.Verdict);
    }

    [Fact]
    public void Le_plafond_degrade_le_verdict_plutot_que_de_mentir()
    {
        // Raboté par le plafond, le réglage ne rend plus la finesse demandée.
        // C'est la finesse servie qu'il faut juger, sans quoi le panneau
        // promettrait ce que la liaison ne laissera pas passer.
        var plan = BitrateAdvice.Plan(0.16, 3840, 2160, 60, "h264", 1, Plafond);

        // Demandé 0,16 bpp, servi 0,050 : le plafond de 25 Mb/s ne couvre pas
        // 3840 x 2160 à soixante images. Le verdict tombe donc à « juste », là
        // où juger la finesse demandée aurait dit « confortable ».
        Assert.Equal(Plafond, plan.KbpsPerWindow);
        Assert.Equal(BitrateVerdict.Tight, plan.Verdict);
        Assert.Contains("juste", plan.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_reglage_incomplet_ne_dit_rien()
    {
        var plan = BitrateAdvice.Plan(0, 1920, 1080, 60, "h264", 2, Plafond);

        Assert.Empty(plan.Summary);
        Assert.Empty(plan.LinkSummary);
        Assert.Equal(0, plan.TotalKbps);
    }

    [Fact]
    public void Aucune_fenetre_ouverte_compte_pour_une()
    {
        // À l'ouverture du panneau, rien n'est encore lancé : on annonce alors
        // ce que coûtera la première fenêtre, non zéro.
        var aucune = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 0, Plafond);
        var une = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);

        Assert.Equal(une.TotalKbps, aucune.TotalKbps);
        Assert.Equal(une.LinkSummary, aucune.LinkSummary);
    }
}
