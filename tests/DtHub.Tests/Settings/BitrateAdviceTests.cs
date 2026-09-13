using DtHub.Core.Settings;

namespace DtHub.Tests.Settings;

public class BitrateAdviceTests
{
    [Theory]
    // The reference figures published by YouTube for well-encoded H.264.
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
        // The real defect of this project: the old steps gave the
        // "maximale" (maximum) mode five and a half times fewer bits per
        // pixel than the "basse" (low) mode. Yet the two bitrates,
        // taken in isolation, seemed reasonable, and that is exactly
        // what the verdict must call out.
        var basse = BitrateAdvice.Read(1280, 720, 30, 2500);
        var maximale = BitrateAdvice.Read(3840, 2160, 120, 16000);

        Assert.Equal(BitrateVerdict.Comfortable, basse.Verdict);
        Assert.Equal(BitrateVerdict.Insufficient, maximale.Verdict);
        Assert.True(maximale.BitsPerPixel < basse.BitsPerPixel);
    }

    [Fact]
    public void Le_bon_codec_n_est_pas_puni()
    {
        // At equal bitrate, H.265 renders better. A user who switches to
        // it must not see their verdict get worse.
        const int w = 1920, h = 1080, fps = 60, kbps = 7000;

        var avc = BitrateAdvice.Read(w, h, fps, kbps, "h264");
        var hevc = BitrateAdvice.Read(w, h, fps, kbps, "h265");

        Assert.Equal(BitrateVerdict.Tight, avc.Verdict);
        Assert.Equal(BitrateVerdict.Comfortable, hevc.Verdict);

        // The raw measurement itself does not move: it is the same
        // bitrate.
        Assert.Equal(avc.BitsPerPixel, hevc.BitsPerPixel, 6);
    }

    [Fact]
    public void Un_debit_demesure_est_dit_pour_ce_qu_il_est()
    {
        // Fifty megabits at 720p: nothing more will show for it, and
        // with two accounts on Wi-Fi the link will pay for it.
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
        // The panel calls this on every keystroke, including on a
        // half-erased field. The sentence is then empty rather than
        // wrong.
        var reading = BitrateAdvice.Read(w, h, fps, kbps);

        Assert.Empty(reading.Summary);
        Assert.Equal(0, reading.BitsPerPixel);
    }

    [Fact]
    public void La_phrase_s_ecrit_en_francais()
    {
        // Decimal comma, whatever the machine's culture: otherwise the
        // test would pass here and fail on an English-language machine.
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
/// The advice does not judge a single stream in isolation: DT Hub opens
/// several windows on one phone and one link. That is what these tests
/// pin down, and it is what sets this panel apart from that of a plain
/// mirroring tool.
/// </summary>
public class BitratePlanTests
{
    private const int Plafond = 25000;

    [Fact]
    public void Le_debit_se_deduit_de_la_finesse_et_de_la_taille()
    {
        var plan = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);

        // 0.09 x 1920 x 1080 x 60 / 1000 = 11197 kb/s.
        Assert.Equal(11197, plan.KbpsPerWindow);
        Assert.Equal(BitrateVerdict.Comfortable, plan.Verdict);
    }

    [Fact]
    public void La_liaison_porte_toutes_les_fenetres()
    {
        var seule = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);
        var trois = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 3, Plafond);

        // A single window's stream does not change; what the link
        // absorbs does.
        Assert.Equal(seule.KbpsPerWindow, trois.KbpsPerWindow);
        Assert.Equal(seule.KbpsPerWindow * 3, trois.TotalKbps);

        Assert.DoesNotContain("comptes", seule.LinkSummary, StringComparison.Ordinal);
        Assert.Contains("3 comptes", trois.LinkSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_petite_fenetre_ne_recoit_pas_le_debit_d_une_grande()
    {
        // The defect an absolute bitrate would have reintroduced, and
        // that the rest of the code had already fixed.
        var grande = BitrateAdvice.Plan(0.09, 2560, 1440, 60, "h264", 1, Plafond);
        var petite = BitrateAdvice.Plan(0.09, 1280, 720, 60, "h264", 1, Plafond);

        Assert.True(grande.KbpsPerWindow > petite.KbpsPerWindow);

        // And yet both are judged the same: that is the whole point of
        // reasoning in bits per pixel.
        Assert.Equal(grande.Verdict, petite.Verdict);
    }

    [Fact]
    public void Le_plafond_degrade_le_verdict_plutot_que_de_mentir()
    {
        // Trimmed down by the ceiling, the setting no longer delivers
        // the detail requested. It is the detail actually served
        // that must be judged, or else the panel would promise what the
        // link will not let through.
        var plan = BitrateAdvice.Plan(0.16, 3840, 2160, 60, "h264", 1, Plafond);

        // Requested 0.16 bpp, served 0.050: the 25 Mb/s ceiling does not
        // cover 3840 x 2160 at sixty frames per second. The verdict
        // therefore falls to "tight", where judging the detail
        // requested would have said "comfortable".
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
        // When the panel opens, nothing has launched yet: what is
        // announced then is the cost of the first window, not zero.
        var aucune = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 0, Plafond);
        var une = BitrateAdvice.Plan(0.09, 1920, 1080, 60, "h264", 1, Plafond);

        Assert.Equal(une.TotalKbps, aucune.TotalKbps);
        Assert.Equal(une.LinkSummary, aucune.LinkSummary);
    }
}
