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
}
