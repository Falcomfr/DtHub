using DtHub.Core.Dofus;
using DtHub.Core.IPhone;

namespace DtHub.Tests.IPhone;

public class IosLaunchSplitTests
{
    private static DofusInstance Android(string device, int user) => new()
    {
        DeviceId = device,
        DeviceName = device,
        UserId = user,
        UserName = $"{device}-{user}",
        PackageName = "com.ankamagames.dofustouch",
    };

    private static DofusInstance Ios(string udid) => new()
    {
        DeviceId = udid,
        DeviceName = udid,
        UserId = 0,
        UserName = udid,
        PackageName = DofusInstance.IosPackage,
        Platform = DevicePlatform.Ios,
    };

    [Fact]
    public void Une_team_melangee_ouvre_les_iOS_a_part()
    {
        var a1 = Android("P1", 0);
        var i1 = Ios("U1");
        var a2 = Android("P1", 10);
        var i2 = Ios("U2");

        var (android, ios) = IosLaunchSplit.Split([a1, i1, a2, i2]);

        Assert.Equal([a1, a2], android);
        Assert.Equal([i1, i2], ios);
    }

    [Fact]
    public void Une_liste_toute_Android_reste_telle_quelle()
    {
        var a1 = Android("P2", 0);
        var a2 = Android("P1", 0);
        var a3 = Android("P1", 10);

        var (android, ios) = IosLaunchSplit.Split([a1, a2, a3]);

        Assert.Equal([a1, a2, a3], android);
        Assert.Empty(ios);
    }

    [Fact]
    public void Un_iPhone_absent_est_signale_et_ne_bloque_pas_les_autres()
    {
        var listed = Ios("U1");
        var absent = Ios("U2");

        var missing = IosLaunchSplit.Missing([listed, absent], new HashSet<string>(["U1", "U3"]));

        Assert.Equal([absent.Key], missing);
    }

    [Fact]
    public void Un_iPhone_absent_ou_une_recherche_ratee_demande_une_nouvelle_recherche()
    {
        var u1 = Ios("U1");
        var u2 = Ios("U2");
        var listed = new HashSet<string>(["U1"]);

        Assert.False(IosLaunchSplit.NeedsListing([u1], listed, listFailed: false));
        Assert.True(IosLaunchSplit.NeedsListing([u1, u2], listed, listFailed: false));
        Assert.True(IosLaunchSplit.NeedsListing([u1], listed, listFailed: true));
    }
}
