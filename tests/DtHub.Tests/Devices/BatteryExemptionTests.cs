using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class BatteryExemptionTests
{
    private const string Package = "com.ankama.dofustouch";

    /// <summary>
    /// Excerpt from the capture on the Xiaomi 13T Pro, Android 16,
    /// <c>adb shell dumpsys deviceidle whitelist</c>. The last line is
    /// the one the user creates by removing the game from
    /// restrictions; the others come from the system and are present
    /// on every device.
    /// </summary>
    private const string Prepare = """
        system-excidle,com.microsoft.appmanager,10154
        system-excidle,com.android.providers.calendar,10096
        system-excidle,com.android.updater,6102
        system-excidle,com.android.providers.downloads,10100
        user,com.ankama.dofustouch,10475
        """;

    /// <summary>
    /// Excerpt from the capture on the Mi 9T Pro, Android 11, the
    /// same day: 44 system entries and not one for the game. This is
    /// the device that disconnects.
    /// </summary>
    private const string Neglige = """
        system-excidle,com.google.android.youtube,10195
        system-excidle,com.android.providers.calendar,10093
        system-excidle,com.android.updater,9802
        system-excidle,com.android.providers.downloads,10079
        system-excidle,com.qualcomm.qti.telephonyservice,1001
        """;

    [Fact]
    public void Un_appareil_prepare_se_reconnait()
    {
        Assert.True(BatteryExemption.Covers(Prepare, Package));
    }

    [Fact]
    public void Un_appareil_qui_n_a_jamais_ete_prepare_se_reconnait_aussi()
    {
        Assert.False(BatteryExemption.Covers(Neglige, Package));
    }

    [Fact]
    public void L_origine_de_l_exemption_ne_change_rien()
    {
        // System or user, the exemption protects the same way. What
        // matters is that the game is in there.
        var systeme = Prepare.Replace(
            "user,com.ankama.dofustouch",
            "system-excidle,com.ankama.dofustouch",
            StringComparison.Ordinal);

        Assert.True(BatteryExemption.Covers(systeme, Package));
    }

    [Fact]
    public void Un_paquet_qui_ressemble_ne_compte_pas()
    {
        // The whole name or nothing: a prefix comparison would let
        // another Ankama application pass for the game.
        var voisin = Neglige + "\nuser,com.ankama.dofustouch.beta,10476";

        Assert.False(BatteryExemption.Covers(voisin, Package));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Error: unknown command 'whitelist'")]
    public void Une_reponse_qui_ne_dit_rien_ne_rend_rien(string? whitelist)
    {
        // Every device seen carries dozens of system entries: an
        // empty list says the command failed, not that nothing is
        // exempted. Raising an alarm over that would be raising an
        // alarm over not knowing.
        Assert.Null(BatteryExemption.Covers(whitelist, Package));
    }

    [Fact]
    public void Sans_paquet_il_n_y_a_rien_a_chercher()
    {
        Assert.Null(BatteryExemption.Covers(Prepare, null));
    }
}
