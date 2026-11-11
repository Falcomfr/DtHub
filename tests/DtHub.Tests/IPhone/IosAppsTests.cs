using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

/// <summary>Finding the game among the apps the device lists.</summary>
public class IosAppsTests
{
    [Fact]
    public void L_identifiant_de_DOFUS_Touch_est_lu_dans_la_liste_des_applis()
    {
        const string apps = """
            [{"name":"Safari","bundle":"com.apple.mobilesafari"},{"name":"DOFUS Touch","bundle":"com.ankama.dofustouch"}]
            """;

        Assert.Equal("com.ankama.dofustouch", IosApps.FindDofus(apps));
    }

    [Fact]
    public void Sans_DOFUS_Touch_l_identifiant_est_nul()
    {
        Assert.Null(IosApps.FindDofus("""[{"name":"Safari","bundle":"com.apple.mobilesafari"}]"""));
    }
}
