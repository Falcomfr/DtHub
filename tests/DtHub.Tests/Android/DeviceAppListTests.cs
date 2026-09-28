using DtHub.Core.Android;

namespace DtHub.Tests.Android;

public class DeviceAppListTests
{
    // Laid out as scrcpy v4.1 writes it: LogUtils.buildAppListMessage pads
    // the name to thirty columns, and a longer name sends the package to
    // the next line.
    private const string Output =
        "[server] INFO: List of apps:\n"
        + " * Paramètres                     com.android.settings\n"
        + " * Téléphone  Pro                 com.android.dialer\n"
        + " - DOFUS Touch                    com.ankama.dofustouch\n"
        + " - DOFUS Touch 2                  com.ankama.dofustoucg\n"
        + " - Un nom d'application vraiment très long\n"
        + "                                  com.exemple.long\n";

    [Fact]
    public void Chaque_application_donne_son_nom_et_son_paquet()
    {
        var apps = DeviceAppList.Parse(Output);

        Assert.Contains(new DeviceApp("DOFUS Touch", "com.ankama.dofustouch", false), apps);
        Assert.Contains(new DeviceApp("DOFUS Touch 2", "com.ankama.dofustoucg", false), apps);
    }

    [Fact]
    public void Les_applications_du_systeme_sont_reconnues_a_leur_etoile()
    {
        var apps = DeviceAppList.Parse(Output);

        Assert.True(apps.Single(a => a.PackageName == "com.android.settings").IsSystem);
        Assert.False(apps.Single(a => a.PackageName == "com.ankama.dofustouch").IsSystem);
    }

    [Fact]
    public void Un_double_espace_dans_le_nom_ne_le_coupe_pas()
    {
        var apps = DeviceAppList.Parse(Output);

        Assert.Equal("Téléphone  Pro", apps.Single(a => a.PackageName == "com.android.dialer").Label);
    }

    [Fact]
    public void Un_nom_trop_long_retrouve_son_paquet_a_la_ligne_suivante()
    {
        var apps = DeviceAppList.Parse(Output);

        Assert.Equal(
            "Un nom d'application vraiment très long",
            apps.Single(a => a.PackageName == "com.exemple.long").Label);
    }

    [Fact]
    public void Les_lignes_qui_ne_sont_pas_des_applications_sont_ignorees()
    {
        var apps = DeviceAppList.Parse(Output);

        Assert.Equal(5, apps.Count);
        Assert.Empty(DeviceAppList.Parse("[server] ERROR: Could not list apps\n"));
        Assert.Empty(DeviceAppList.Parse(null));
    }

    [Fact]
    public void Les_fins_de_ligne_windows_sont_acceptees()
    {
        var apps = DeviceAppList.Parse(Output.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(5, apps.Count);
    }
}
