using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class VirtualDisplayTrustTests
{
    /// <summary>
    /// Relevé au caractère près sur un Mi 9T Pro sous Android 11, scrcpy 4.1,
    /// afficheur virtuel bien créé. La fenêtre montrait l'écran de
    /// verrouillage au lieu du jeu.
    /// </summary>
    private const string Ancien =
        "    mSupportedColorModes=[0, 7, 9]  DisplayDeviceInfo{\"scrcpy\": "
        + "uniqueId=\"virtual:com.android.shell,2000,scrcpy,0\", 1920 x 1080, density 240, "
        + "touch VIRTUAL, rotation 0, type VIRTUAL, state ON, owner com.android.shell (uid 2000), "
        + "FLAG_ROTATES_WITH_CONTENT, FLAG_PRESENTATION, FLAG_OWN_CONTENT_ONLY}";

    /// <summary>Même relevé sur un 13T Pro sous Android 16, où le jeu s'ouvre.</summary>
    private const string Recent =
        "    DisplayDeviceInfo{\"scrcpy\": uniqueId=\"virtual:com.android.shell,2000,scrcpy,0\", "
        + "1920 x 1080, type VIRTUAL, FLAG_ROTATES_WITH_CONTENT, FLAG_PRESENTATION, "
        + "FLAG_OWN_CONTENT_ONLY, FLAG_TRUSTED, FLAG_ALWAYS_UNLOCKED, FLAG_OWN_FOCUS, "
        + "FLAG_OWN_DISPLAY_GROUP, FLAG_DESTROY_CONTENT_ON_REMOVAL, FLAG_TOUCH_FEEDBACK_DISABLED}";

    [Fact]
    public void Un_afficheur_deverrouille_se_reconnait()
    {
        Assert.True(VirtualDisplayTrust.IsUnlocked(Recent));
    }

    [Fact]
    public void Un_afficheur_qui_suit_le_verrouillage_se_reconnait()
    {
        // C'est le cas qui donnait une horloge et un cadenas à la place du jeu,
        // sans que rien ne le signale.
        Assert.False(VirtualDisplayTrust.IsUnlocked(Ancien));
    }

    [Fact]
    public void L_ecran_integre_ne_se_prend_pas_pour_l_afficheur_virtuel()
    {
        // L'écran intégré porte FLAG_TRUSTED lui aussi : le confondre avec
        // celui de scrcpy dirait que tout va bien alors que non.
        const string integre =
            "    mBaseDisplayInfo=DisplayInfo{\"Écran intégré\", displayId 0, FLAG_SECURE, "
            + "FLAG_SUPPORTS_PROTECTED_BUFFERS, FLAG_TRUSTED, real 1080 x 2340, type INTERNAL}";

        Assert.Null(VirtualDisplayTrust.IsUnlocked(integre));
    }

    [Fact]
    public void Le_bon_afficheur_est_lu_meme_entoure_des_autres()
    {
        var melange = string.Join('\n', ["    mDisplayId=0", Ancien, "    mFlags=459"]);

        Assert.False(VirtualDisplayTrust.IsUnlocked(melange));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dumpsys: service display does not exist")]
    public void Ne_pas_savoir_n_est_pas_savoir_que_c_est_mauvais(string? dump)
    {
        Assert.Null(VirtualDisplayTrust.IsUnlocked(dump));
    }
}
