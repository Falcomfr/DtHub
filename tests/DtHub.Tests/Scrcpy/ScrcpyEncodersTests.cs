using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class ScrcpyEncodersTests
{
    /// <summary>
    /// Relevé au caractère près sur le Xiaomi 13T Pro, Android 16,
    /// <c>scrcpy --list-encoders</c>. Les encodeurs audio sont gardés : ce
    /// sont eux qui piègent une analyse qui compterait les colonnes.
    /// </summary>
    private const string Releve = """
        [server] INFO: List of video encoders:
            --video-codec=h264 --video-encoder=c2.mtk.avc.encoder             (hw) [vendor]
            --video-codec=h264 --video-encoder=OMX.MTK.VIDEO.ENCODER.AVC      (hw) [vendor] (alias for c2.mtk.avc.encoder)
            --video-codec=h264 --video-encoder=c2.android.avc.encoder         (sw)
            --video-codec=h264 --video-encoder=OMX.google.h264.encoder        (sw) (alias for c2.android.avc.encoder)
            --video-codec=h265 --video-encoder=c2.mtk.hevc.encoder            (hw) [vendor]
            --video-codec=h265 --video-encoder=c2.android.hevc.encoder        (sw)
            --video-codec=av1 --video-encoder=c2.android.av1.encoder          (sw)
            --video-codec=vp8 --video-encoder=c2.android.vp8.encoder          (sw)
        [server] INFO: List of audio encoders:
            --audio-codec=opus --audio-encoder=c2.android.opus.encoder        (sw)
            --audio-codec=aac --audio-encoder=c2.android.aac.encoder          (sw)
        """;

    [Fact]
    public void Les_encodeurs_video_se_lisent_et_les_audio_sont_ecartes()
    {
        var encoders = ScrcpyEncoders.Parse(Releve);

        Assert.Equal(8, encoders.Count);
        Assert.DoesNotContain(encoders, e => e.Name.Contains("opus", StringComparison.Ordinal));
    }

    [Fact]
    public void Le_materiel_le_logiciel_et_les_alias_se_distinguent()
    {
        var encoders = ScrcpyEncoders.Parse(Releve);

        var premier = encoders[0];

        Assert.Equal("h264", premier.Codec);
        Assert.Equal("c2.mtk.avc.encoder", premier.Name);
        Assert.True(premier.Hardware);
        Assert.False(premier.IsAlias);

        Assert.True(encoders[1].IsAlias);
        Assert.False(encoders[2].Hardware);
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("h265")]
    [InlineData("H264")]
    public void Rien_n_est_impose_quand_le_materiel_vient_en_tete(string codec)
    {
        // Forcer un nom n'apporterait aucune image de plus et ajouterait une
        // façon d'échouer.
        Assert.Null(ScrcpyEncoders.Force(ScrcpyEncoders.Parse(Releve), codec));
    }

    [Theory]
    [InlineData("av1")]
    [InlineData("vp8")]
    public void Rien_n_est_impose_quand_il_n_y_a_pas_d_alternative(string codec)
    {
        Assert.Null(ScrcpyEncoders.Force(ScrcpyEncoders.Parse(Releve), codec));
    }

    [Fact]
    public void Le_materiel_est_impose_quand_le_logiciel_vient_en_tete()
    {
        // Le cas qui justifie la fonction : un appareil dont l'ordre déclaré
        // met le logiciel devant. Celui de référence ne le fait pas, mais
        // l'ordre vient de l'appareil et rien ne le garantit ailleurs.
        const string inverse = """
            [server] INFO: List of video encoders:
                --video-codec=h264 --video-encoder=c2.android.avc.encoder (sw)
                --video-codec=h264 --video-encoder=c2.mtk.avc.encoder     (hw) [vendor]
            """;

        Assert.Equal("c2.mtk.avc.encoder", ScrcpyEncoders.Force(ScrcpyEncoders.Parse(inverse), "h264"));
    }

    [Fact]
    public void Un_alias_n_est_jamais_impose()
    {
        // Un alias est un autre nom d'un encodeur déjà listé : le choisir ne
        // changerait rien qu'un risque.
        const string alias = """
            [server] INFO: List of video encoders:
                --video-codec=h264 --video-encoder=c2.android.avc.encoder (sw)
                --video-codec=h264 --video-encoder=OMX.MTK.AVC            (hw) (alias for c2.mtk.avc.encoder)
                --video-codec=h264 --video-encoder=c2.mtk.avc.encoder     (hw) [vendor]
            """;

        Assert.Equal("c2.mtk.avc.encoder", ScrcpyEncoders.Force(ScrcpyEncoders.Parse(alias), "h264"));
    }

    [Theory]
    [InlineData("av1", true)]
    [InlineData("vp8", true)]
    [InlineData("h264", false)]
    [InlineData("h265", false)]
    public void Un_codec_sans_materiel_se_signale(string codec, bool softwareOnly)
    {
        Assert.Equal(softwareOnly, ScrcpyEncoders.SoftwareOnly(ScrcpyEncoders.Parse(Releve), codec));
    }

    [Fact]
    public void Ne_pas_savoir_n_est_pas_savoir_que_c_est_mauvais()
    {
        // Alarmer sur une ignorance serait pire que se taire.
        Assert.False(ScrcpyEncoders.SoftwareOnly([], "h264"));
        Assert.False(ScrcpyEncoders.SoftwareOnly(ScrcpyEncoders.Parse(Releve), "vp9"));
        Assert.Null(ScrcpyEncoders.Force([], "h264"));
    }

    [Fact]
    public void Les_codecs_materiels_se_listent_sans_doublon()
    {
        Assert.Equal(["h264", "h265"], ScrcpyEncoders.HardwareCodecs(ScrcpyEncoders.Parse(Releve)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ERROR: could not list encoders")]
    [InlineData("[server] INFO: List of video encoders:")]
    public void Une_sortie_inexploitable_ne_rend_rien(string? listing)
    {
        Assert.Empty(ScrcpyEncoders.Parse(listing));
    }

    [Fact]
    public void Un_codec_absent_de_la_liste_ne_force_rien()
    {
        Assert.Null(ScrcpyEncoders.Force(ScrcpyEncoders.Parse(Releve), "vp9"));
        Assert.Null(ScrcpyEncoders.Force(ScrcpyEncoders.Parse(Releve), null));
    }
}
