using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class DeviceLockTests
{
    /// <summary>
    /// Captured character-for-character on the Mi 9T Pro, Android 11,
    /// <c>adb shell dumpsys trust</c>, phone locked. The second line
    /// is the work profile: it has no lock state, and it is the one
    /// that traps an analysis that would take the first line it
    /// finds.
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
        // Same device, after manual unlocking: this is the state
        // where the window shows the game instead of the lock icon.
        var ouvert = Verrouille.Replace("deviceLocked=1", "deviceLocked=0", StringComparison.Ordinal);

        Assert.False(DeviceLock.IsLocked(ouvert));
    }

    [Fact]
    public void L_utilisateur_courant_l_emporte()
    {
        // A device can describe several full users. The one that
        // counts is the one in front, and only it carries
        // "(current)".
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
        // An overlay that would not write "(current)" must not make
        // the check silent: a partial answer is better than none.
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
        // Not knowing is not a reason to alarm: the lock warning only
        // rises on an observed lock.
        Assert.Null(DeviceLock.IsLocked(dumpsys));
    }
}
