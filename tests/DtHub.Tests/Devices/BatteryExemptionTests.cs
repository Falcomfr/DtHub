using DtHub.Core.Devices;

namespace DtHub.Tests.Devices;

public class BatteryExemptionTests
{
    private const string Package = "com.ankama.dofustouch";

    /// <summary>
    /// Extrait du relevé du Xiaomi 13T Pro, Android 16,
    /// <c>adb shell dumpsys deviceidle whitelist</c>. La dernière ligne est
    /// celle que pose l'utilisateur en retirant le jeu des restrictions ;
    /// les autres viennent du système et sont là sur tous les appareils.
    /// </summary>
    private const string Prepare = """
        system-excidle,com.microsoft.appmanager,10154
        system-excidle,com.android.providers.calendar,10096
        system-excidle,com.android.updater,6102
        system-excidle,com.android.providers.downloads,10100
        user,com.ankama.dofustouch,10475
        """;

    /// <summary>
    /// Extrait du relevé du Mi 9T Pro, Android 11, le même jour : quarante
    /// quatre entrées système et pas une pour le jeu. C'est l'appareil qui se
    /// déconnecte.
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
        // Système ou utilisateur, l'exemption protège de la même façon. Ce
        // qui compte est que le jeu y soit.
        var systeme = Prepare.Replace(
            "user,com.ankama.dofustouch",
            "system-excidle,com.ankama.dofustouch",
            StringComparison.Ordinal);

        Assert.True(BatteryExemption.Covers(systeme, Package));
    }

    [Fact]
    public void Un_paquet_qui_ressemble_ne_compte_pas()
    {
        // Le nom entier ou rien : une comparaison par préfixe ferait passer
        // une autre application d'Ankama pour le jeu.
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
        // Tous les appareils vus portent des dizaines d'entrées système :
        // une liste vide dit que la commande a échoué, pas que rien n'est
        // exempté. Alarmer là-dessus serait alarmer sur une ignorance.
        Assert.Null(BatteryExemption.Covers(whitelist, Package));
    }

    [Fact]
    public void Sans_paquet_il_n_y_a_rien_a_chercher()
    {
        Assert.Null(BatteryExemption.Covers(Prepare, null));
    }
}
