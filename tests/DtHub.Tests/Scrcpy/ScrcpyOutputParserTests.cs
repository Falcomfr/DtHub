using DtHub.Core.Scrcpy;

namespace DtHub.Tests.Scrcpy;

public class ScrcpyOutputParserTests
{
    [Fact]
    public void L_identifiant_d_afficheur_virtuel_est_extrait_de_la_ligne_du_serveur()
    {
        // Format produit par NewDisplayCapture.java, relayé au client.
        const string line = "[server] INFO: New display: 1080x1920/320 (id=2)";

        Assert.Equal(2, ScrcpyOutputParser.TryParseVirtualDisplayId(line));
    }

    [Fact]
    public void Un_identifiant_a_plusieurs_chiffres_est_lu_entierement()
    {
        Assert.Equal(
            147,
            ScrcpyOutputParser.TryParseVirtualDisplayId("[server] INFO: New display: 800x600/240 (id=147)"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[server] INFO: Device: Xiaomi 23078RKD5G (Android 14)")]
    [InlineData("INFO: Renderer: direct3d11")]
    [InlineData("New display without any identifier")]
    public void Une_ligne_sans_identifiant_ne_produit_rien(string? line)
    {
        Assert.Null(ScrcpyOutputParser.TryParseVirtualDisplayId(line));
    }

    [Fact]
    public void Une_ligne_d_erreur_est_reconnue()
    {
        Assert.True(ScrcpyOutputParser.IsError("ERROR: Could not find any ADB device"));
        Assert.True(ScrcpyOutputParser.IsError("[server] ERROR: Encoder failed"));
        Assert.False(ScrcpyOutputParser.IsError("INFO: Texture: 1080x1920"));
        Assert.False(ScrcpyOutputParser.IsError(null));
    }

    [Theory]
    [InlineData("ERROR: Could not find any ADB device", "câble")]
    [InlineData("ERROR: Device disconnected", "déconnecté")]
    [InlineData("ERROR: Device unauthorized", "débogage")]
    [InlineData("ERROR: Could not create virtual display", "Android 11")]
    [InlineData("ERROR: Encoder 'c2.android.avc.encoder' failed", "débit")]
    [InlineData("ERROR: Server connection failed", "Reconnectez")]
    public void Les_erreurs_courantes_sont_traduites_en_langage_comprehensible(string line, string expected)
    {
        var message = ScrcpyOutputParser.DescribeError(line);

        Assert.NotNull(message);
        Assert.Contains(expected, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_erreur_inconnue_ne_produit_pas_de_message_invente()
    {
        // Mieux vaut un message générique et le détail au journal qu'une
        // traduction approximative.
        Assert.Null(ScrcpyOutputParser.DescribeError("ERROR: something entirely new"));
        Assert.Null(ScrcpyOutputParser.DescribeError(null));
    }

    [Theory]
    [InlineData("ERROR: Could not find any ADB device", ScrcpyFailureKind.DeviceGone)]
    [InlineData("ERROR: Device disconnected", ScrcpyFailureKind.DeviceDisconnected)]
    [InlineData("ERROR: device unauthorized", ScrcpyFailureKind.Unauthorized)]
    [InlineData("ERROR: Could not create display", ScrcpyFailureKind.VirtualDisplayRefused)]
    [InlineData("ERROR: Encoder 'c2.android.avc.encoder' failed", ScrcpyFailureKind.Encoder)]
    [InlineData("ERROR: Server connection failed", ScrcpyFailureKind.ConnectionFailed)]
    public void Chaque_refus_connu_est_range_dans_sa_categorie(string line, ScrcpyFailureKind expected)
    {
        Assert.Equal(expected, ScrcpyOutputParser.Classify(line));
    }

    [Fact]
    public void Un_refus_non_reconnu_n_est_pas_range_dans_une_categorie_devinee()
    {
        // La sortie de scrcpy n'est pas contractuelle : ranger au jugé mènerait
        // à afficher une explication fausse avec l'aplomb d'une explication
        // vraie.
        Assert.Equal(
            ScrcpyFailureKind.Unknown,
            ScrcpyOutputParser.Classify("ERROR: something entirely new"));

        Assert.Null(ScrcpyOutputParser.Describe(ScrcpyFailureKind.Unknown));
    }

    [Fact]
    public void On_ne_retente_que_les_refus_qu_une_definition_plus_modeste_peut_reparer()
    {
        // Un encodeur saturé se répare en descendant ; un téléphone débranché
        // ne se répare pas, et chaque tentative coûte l'attente complète.
        Assert.True(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.Encoder));
        Assert.True(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.Timeout));
        Assert.True(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.Unknown));
        Assert.True(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.VirtualDisplayRefused));

        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.DeviceGone));
        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.DeviceDisconnected));
        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.Unauthorized));
        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.ConnectionFailed));
        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.Environment));
        Assert.False(ScrcpyOutputParser.CanRetrySmaller(ScrcpyFailureKind.None));
    }

    [Fact]
    public void La_coupure_de_liaison_compte_comme_une_fin_meme_sans_le_mot_erreur()
    {
        // Relevé sur l'appareil réel, scrcpy 4.1, liaison Wi-Fi coupée en
        // pleine session : c'est un avertissement, pas une erreur, et pourtant
        // le processus s'arrête là. Ne regarder que « ERROR: » revenait à
        // prendre la panne la plus fréquente pour une fermeture voulue.
        const string releve = "WARN: Device disconnected";

        Assert.False(ScrcpyOutputParser.IsError(releve));
        Assert.True(ScrcpyOutputParser.IsFatal(releve));
        Assert.Equal(ScrcpyFailureKind.DeviceDisconnected, ScrcpyOutputParser.Classify(releve));
    }

    [Theory]
    [InlineData("ERROR: Could not connect to the device")]
    [InlineData("ERROR: Device disconnected")]
    public void Une_erreur_declaree_reste_une_fin(string line)
    {
        Assert.True(ScrcpyOutputParser.IsFatal(line));
    }

    [Theory]
    [InlineData("WARN: Frame skipped")]
    [InlineData("WARN: Demuxer error")]
    [InlineData("INFO: New display: 800x600/240 (id=33)")]
    [InlineData("[server] INFO: Device: [Xiaomi] Xiaomi 23078PND5G (Android 16)")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_avertissement_anodin_n_est_pas_une_fin(string? line)
    {
        // Le contrôle reste étroit : prendre tout avertissement pour une panne
        // ferait rouvrir des fenêtres que personne n'a perdues.
        Assert.False(ScrcpyOutputParser.IsFatal(line));
    }
}
