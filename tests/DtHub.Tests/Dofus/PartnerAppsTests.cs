using DtHub.Core.Dofus;

namespace DtHub.Tests.Dofus;

public sealed class PartnerAppsTests
{
    private const string DtStuff = "com.makeintouch.dtstuff";
    private const string OcrApp = "com.makeintouch.ocrapp";

    [Fact]
    public void Sans_appareil_aucune_app_n_est_disponible()
    {
        var availability = PartnerApps.Availability([]);

        Assert.Equal([DtStuff, OcrApp], availability.Select(a => a.App.PackageName));
        Assert.All(availability, a => Assert.Empty(a.WithApp));
        Assert.All(availability, a => Assert.Empty(a.WithoutApp));
    }

    [Fact]
    public void Repartit_les_appareils_selon_qu_ils_ont_l_app()
    {
        var availability = PartnerApps.Availability(
        [
            ("tel", [DtStuff, "com.ankama.dofustouch"]),
            ("tablette", ["com.ankama.dofustouch"]),
        ]);

        var dtStuff = availability.Single(a => a.App.PackageName == DtStuff);
        Assert.Equal(["tel"], dtStuff.WithApp);
        Assert.Equal(["tablette"], dtStuff.WithoutApp);

        var ocrApp = availability.Single(a => a.App.PackageName == OcrApp);
        Assert.Empty(ocrApp.WithApp);
        Assert.Equal(["tel", "tablette"], ocrApp.WithoutApp);
    }

    [Fact]
    public void L_ordre_des_appareils_est_garde()
    {
        var availability = PartnerApps.Availability(
        [
            ("b", [OcrApp]),
            ("a", [OcrApp]),
        ]);

        Assert.Equal(["b", "a"], availability.Single(a => a.App.PackageName == OcrApp).WithApp);
    }

    [Fact]
    public void Ignore_les_autres_paquets()
    {
        Assert.False(PartnerApps.IsPartner("com.ankama.dofustouch"));
        Assert.False(PartnerApps.IsPartner("com.makeintouch.dtstuff2"));
        Assert.True(PartnerApps.IsPartner(DtStuff));
        Assert.True(PartnerApps.IsPartner(OcrApp));
    }

    [Fact]
    public void Ne_compte_que_les_paquets_fournis()
    {
        // Clones are left out by the caller, which passes user 0 only: a
        // package seen elsewhere is simply not in the list it gives.
        var availability = PartnerApps.Availability([("tel", [])]);

        Assert.All(availability, a => Assert.Equal(["tel"], a.WithoutApp));
    }

    [Fact]
    public void Une_installation_fait_passer_l_appareil_du_cote_de_l_app()
    {
        var before = PartnerApps.Availability([("tel", []), ("tablette", [])]);

        var after = PartnerApps.WithInstalled(before, DtStuff, "tel");

        var dtStuff = after.Single(a => a.App.PackageName == DtStuff);
        Assert.Equal(["tel"], dtStuff.WithApp);
        Assert.Equal(["tablette"], dtStuff.WithoutApp);
        Assert.Equal(["tel", "tablette"], after.Single(a => a.App.PackageName == OcrApp).WithoutApp);
    }

    [Fact]
    public void Une_installation_deja_connue_ne_change_rien()
    {
        var before = PartnerApps.Availability([("tel", [DtStuff])]);

        var after = PartnerApps.WithInstalled(before, DtStuff, "tel");

        Assert.Equal(["tel"], after.Single(a => a.App.PackageName == DtStuff).WithApp);
    }

    [Fact]
    public void L_adresse_du_store_vise_le_paquet() =>
        Assert.Equal("market://details?id=com.makeintouch.ocrapp", PartnerApps.StoreUri(OcrApp));
}
