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

    [Fact]
    public void Calcofus_rouvre_sur_le_dernier_calculateur() =>
        Assert.Equal("https://calcofus.fr/page_bombes", Calcofus.StartAddress("https://calcofus.fr/page_bombes"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://discord.gg/dfDV8Zvwqp")]
    [InlineData("https://calcofus.fr.example.com/page_invo")]
    [InlineData("http://calcofus.fr/page_invo")]
    [InlineData("pas une adresse")]
    public void Calcofus_ne_rouvre_que_ses_propres_pages(string? remembered)
    {
        // A link followed out of the site, to Discord or a stuff library,
        // is not a calculator to come back to.
        Assert.Equal("https://calcofus.fr/", Calcofus.StartAddress(remembered));
    }

    [Theory]
    [InlineData("https://www.highperformanceformat.com/abc/invoke.js", true)]
    [InlineData("https://highperformanceformat.com/x", true)]
    [InlineData("https://www.topcreativeformat.com/watch.1234", true)]
    [InlineData("https://www.dofus-touch-map.com/img/icons/favicon.png", false)]
    [InlineData("https://api.dofus-touch-map.com/?ref=highperformanceformat.com", false)]
    [InlineData("https://nothighperformanceformat.com/x", false)]
    [InlineData("pas une adresse", false)]
    public void Seule_la_regie_adsterra_de_la_carte_est_bloquee(string url, bool blocked)
    {
        // The two hosts its ads came from on 2026-10-11, which set off the
        // project owner's antivirus. The site itself, and an address that
        // merely mentions one of them, still load.
        Assert.Equal(blocked, DofusTouchMap.IsBlockedAd(url));
    }

    [Fact]
    public void Une_fenetre_d_outil_n_a_jamais_la_cle_d_un_compte_du_meme_paquet()
    {
        // A player may have added DTStuff with "+" before the tools existed:
        // sharing its key, the tool window took its place, its tab and its
        // running state.
        var account = new DofusInstance { DeviceId = "tel", DeviceName = "Tel", UserId = 0, UserName = "Main", PackageName = DtStuff };
        var tool = account with { IsTransient = true };
        var target = new DtHub.Core.Sessions.LaunchTarget
        {
            DeviceId = "tel",
            Serial = "s",
            UserId = 0,
            PackageName = DtStuff,
            DisplayName = "DTStuff",
            IsTransient = true,
        };

        Assert.NotEqual(account.Key, tool.Key);
        Assert.Equal(tool.Key, target.Key);
    }

    [Theory]
    [InlineData("https://calcofus.fr/page_invo", true)]
    [InlineData("https://www.calcofus.fr/page_invo", true)]
    [InlineData("https://calcofus.fr/page%5Finvo", true)]
    [InlineData("https://discord.gg/dfDV8Zvwqp", false)]
    [InlineData("http://calcofus.fr/", false)]
    [InlineData("https://calcofus.fr.example.com/", false)]
    public void Une_page_de_calcofus_se_reconnait_a_son_hote(string url, bool own)
    {
        // Compared as text, an escaped character or the www form sent an
        // in-site page to the browser.
        Assert.Equal(own, Calcofus.IsOwnPage(url));
    }

    [Theory]
    [InlineData("https://www.dofusroom.com/insightroom", true)]
    [InlineData("https://dofusroom.com/insightroom", true)]
    [InlineData("https://www.dofusroom.com/insightroom/", true)]
    [InlineData("https://www.dofusroom.com/insightroom?lang=en", true)]
    [InlineData("https://www.dofusroom.com/INSIGHTROOM", true)]
    [InlineData("https://www.dofusroom.com/user/page/show/37312", false)]
    [InlineData("https://www.dofusroom.com/buildroom", false)]
    [InlineData("https://www.dofusroom.com/insightroomx", false)]
    [InlineData("http://www.dofusroom.com/insightroom", false)]
    [InlineData("https://www.dofusroom.com.example.com/insightroom", false)]
    [InlineData("https://discord.com/invite/kfNS5h7", false)]
    [InlineData("pas une adresse", false)]
    public void Seule_l_insightroom_de_dofusroom_reste_dans_sa_fenetre(string url, bool own)
    {
        // The window shows the Touch maps only: a profile, another room of
        // the site or Discord goes to the browser.
        Assert.Equal(own, DofusRoom.IsOwnPage(url));
    }

    [Theory]
    [InlineData("https://lomitall.fr/", true)]
    [InlineData("https://www.lomitall.fr/guides", true)]
    [InlineData("https://LOMITALL.fr/", true)]
    [InlineData("http://lomitall.fr/", false)]
    [InlineData("https://lomitall.fr.example.com/", false)]
    [InlineData("https://papycha.fr/", false)]
    [InlineData("pas une adresse", false)]
    public void Une_page_de_lomitall_se_reconnait_a_son_hote(string url, bool own)
    {
        // The portal links to every other tool: those go to the browser.
        Assert.Equal(own, Lomitall.IsOwnPage(url));
    }

    [Theory]
    [InlineData(DtStuff, false, 1080, 1920)]
    [InlineData(DtStuff, true, 1920, 1080)]
    [InlineData(OcrApp, true, 1080, 1920)]
    [InlineData("com.android.vending", true, 1080, 1920)]
    public void Seul_dtstuff_passe_en_paysage(string package, bool landscape, int width, int height)
    {
        // OcrApp turns its display back to portrait, the store breaks on a
        // landscape one: both stay as they were.
        Assert.Equal((width, height), PartnerApps.ToolDisplay(package, landscape));
    }
}
