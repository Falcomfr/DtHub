using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class DeviceLockTests
{
    /// <summary>
    /// Relevé au caractère près sur le Mi 9T Pro, Android 11,
    /// <c>adb shell dumpsys trust</c>, téléphone verrouillé. La seconde ligne
    /// est le profil professionnel : il n'a pas d'état de verrouillage, et
    /// c'est lui qui piège une analyse qui prendrait la première ligne venue.
    /// </summary>
    private const string Verrouille = """
        Trust manager state:
         User "Propriétaire" (id=0, flags=0xc13) (current): trusted=0, trustManaged=0, deviceLocked=1, strongAuthRequired=0x0
             bound=1, connected=1, managingTrust=0, trusted=0
         User "Compte 2" (id=10, flags=0x1030)(managed profile)
        """;

    [Fact]
    public void Un_telephone_verrouille_se_lit()
    {
        Assert.True(DeviceLock.IsLocked(Verrouille));
    }

    [Fact]
    public void Un_telephone_deverrouille_se_lit()
    {
        // Même appareil, après déverrouillage à la main : c'est l'état où la
        // fenêtre montre le jeu au lieu du cadenas.
        var ouvert = Verrouille.Replace("deviceLocked=1", "deviceLocked=0", StringComparison.Ordinal);

        Assert.False(DeviceLock.IsLocked(ouvert));
    }

    [Fact]
    public void L_utilisateur_courant_l_emporte()
    {
        // Un appareil peut décrire plusieurs utilisateurs complets. Celui qui
        // compte est celui qui est devant, et lui seul porte « (current) ».
        const string plusieurs = """
            Trust manager state:
             User "Second" (id=11, flags=0x410): trusted=0, trustManaged=0, deviceLocked=1, strongAuthRequired=0x0
             User "Propriétaire" (id=0, flags=0xc13) (current): trusted=0, trustManaged=0, deviceLocked=0, strongAuthRequired=0x0
            """;

        Assert.False(DeviceLock.IsLocked(plusieurs));
    }

    [Fact]
    public void Sans_utilisateur_courant_la_premiere_reponse_sert()
    {
        // Une surcouche qui n'écrirait pas « (current) » ne doit pas rendre le
        // contrôle muet : une réponse partielle vaut mieux qu'aucune.
        const string sansMarque = """
            Trust manager state:
             User "Propriétaire" (id=0, flags=0xc13): trusted=0, deviceLocked=1, strongAuthRequired=0x0
            """;

        Assert.True(DeviceLock.IsLocked(sansMarque));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Trust manager state:")]
    [InlineData("can't find service trust")]
    [InlineData(" User \"Compte 2\" (id=10, flags=0x1030)(managed profile)")]
    public void Une_reponse_qui_ne_dit_rien_ne_rend_rien(string? dumpsys)
    {
        // Ne pas savoir n'est pas une raison d'alarmer : l'avertissement du
        // cadenas ne se lève que sur un verrouillage constaté.
        Assert.Null(DeviceLock.IsLocked(dumpsys));
    }
}
