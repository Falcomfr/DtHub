using DtHub.Core.Adb;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Sessions;
using DtHub.Core.Users;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Sessions;

/// <summary>
/// Launching the game on an Android profile: what the product does, and which
/// had no test at all. The scrcpy tests went through a simulated launcher, so
/// this one was never exercised.
///
/// Everything runs on the simulated ADB client, which answers with text: so
/// this also tests the parsing of that text, which is where the decisions are
/// made.
/// </summary>
public class AndroidAppLauncherTests
{
    private const string Serial = "192.168.1.25:5555";
    private const string Package = "com.ankama.dofustouch";
    private const string Component = "com.ankama.dofustouch/.MainActivity";

    /// <summary>
    /// Output recorded on the reference phone, a Xiaomi 13T. The flags matter:
    /// "10" would make it a secondary profile, which cannot carry a window,
    /// and the launch would be refused before being attempted.
    /// </summary>
    private static readonly string DeuxProfils = string.Join(
        '\n',
        "Users:",
        "\tUserInfo{0:Alice Martin:4c13} running",
        "\tUserInfo{999:XSpace:801010} running");

    private static AndroidAppLauncher Build(FakeAdbClient adb)
    {
        var users = new AndroidUserService(adb);
        return new AndroidAppLauncher(adb, users, new DofusInstanceService(adb, users));
    }

    [Fact]
    public void Les_arguments_ouvrent_l_adresse_sur_l_ecran_virtuel() =>
        Assert.Equal(
            ["am", "start", "--user", "0", "--display", "62", "--windowingMode", "1", "--activity-exclude-from-recents",
             "-a", "android.intent.action.VIEW", "-d", "market://details?id=com.makeintouch.dtstuff"],
            AndroidAppLauncher.UriArguments(0, "market://details?id=com.makeintouch.dtstuff", 62));

    [Fact]
    public void Sans_ecran_virtuel_pas_de_display() =>
        Assert.Equal(
            ["am", "start", "--user", "0",
             "-a", "android.intent.action.VIEW", "-d", "market://details?id=com.makeintouch.ocrapp"],
            AndroidAppLauncher.UriArguments(0, "market://details?id=com.makeintouch.ocrapp", null));

    [Fact]
    public async Task Un_intent_sans_destinataire_dit_que_le_store_manque()
    {
        // Recorded on the 13T Pro with a link no installed app handles.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Starting: Intent { act=android.intent.action.VIEW dat=market://details/... }\n"
                + "Error: Activity not started, unable to resolve Intent { act=android.intent.action.VIEW dat=market://details/... flg=0x10000000 }");

        var result = await Build(adb).OpenUriAsync(Serial, 0, "market://details?id=com.makeintouch.dtstuff", 62);

        Assert.False(result.Succeeded);
        Assert.Equal(Strings.Get("PlayStoreMissing"), result.UserMessage);
    }

    [Fact]
    public async Task L_adresse_ouverte_sans_erreur_reussit()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Starting: Intent { act=android.intent.action.VIEW dat=market://details/... }");

        var result = await Build(adb).OpenUriAsync(Serial, 0, "market://details?id=com.makeintouch.dtstuff", 62);

        Assert.True(result.Succeeded);
        Assert.Contains(
            "am start --user 0 --display 62 --windowingMode 1 --activity-exclude-from-recents -a android.intent.action.VIEW -d market://details?id=com.makeintouch.dtstuff",
            adb.ShellCalls);
    }

    [Fact]
    public async Task Le_lancement_porte_le_profil_et_l_afficheur()
    {
        // The product's central requirement: one instance per profile, each on
        // its own virtual display.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Starting: Intent { cmp=" + Component + " }\nStatus: ok");

        var result = await Build(adb).LaunchAsync(Serial, 999, Package, Component, 42);

        Assert.True(result.Succeeded);
        Assert.Contains(
            "am start --user 999 --display 42 --windowingMode 1 --activity-exclude-from-recents -n " + Component,
            adb.ShellCalls);
    }

    [Fact]
    public async Task Sans_afficheur_la_commande_n_en_nomme_aucun()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Status: ok");

        await Build(adb).LaunchAsync(Serial, 0, Package, Component, displayId: null);

        Assert.Contains("am start --user 0 -n " + Component, adb.ShellCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(11)]
    [InlineData(1234)]
    public async Task N_importe_quel_identifiant_de_profil_passe_tel_quel(int userId)
    {
        // A cloned profile is not always 999, and the README makes that a
        // promise. The number is written in invariant culture: on a machine
        // whose region uses a separator, a four-digit identifier would become
        // "1 234".
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("am start", "Status: ok");

        await Build(adb).LaunchAsync(Serial, userId, Package, Component, null);

        Assert.Contains(
            adb.ShellCalls,
            c => c.Contains($"--user {userId}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Une_sortie_qui_porte_Error_est_un_echec_malgre_le_code_zero()
    {
        // "am start" returns zero even when it fails: the output is what's
        // authoritative, and it is this parsing that decides whether to open a
        // window or refuse it.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm resolve-activity", string.Empty)
            .WithShell("am start", "Error: Activity class does not exist.");

        var result = await Build(adb).LaunchAsync(Serial, 0, Package, Component, null);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.UserMessage));
    }

    [Fact]
    public async Task Une_permission_refusee_ne_se_confond_pas_avec_un_jeu_absent()
    {
        // Assuming "app missing" on any failure would send the user to
        // reinstall a game that was actually there.
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm path", "package:/data/app/base.apk")
            .WithShell("pm resolve-activity", string.Empty)
            .WithShell("am start", "java.lang.SecurityException: Permission Denial: starting Intent");

        var result = await Build(adb).LaunchAsync(Serial, 0, Package, Component, null);

        Assert.False(result.Succeeded);
        Assert.Contains("Permission Denial", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sans_composant_connu_le_jeu_est_declare_absent_du_profil()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list users", DeuxProfils)
            .WithShell("pm resolve-activity", string.Empty);

        var result = await Build(adb).LaunchAsync(Serial, 999, Package, knownComponent: null, null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Sur_un_afficheur_virtuel_le_jeu_est_tenu_hors_des_recents()
    {
        // **Without this flag, closing a window left an empty thumbnail at the
        // top of the phone's application list.** Recorded on the Mi 9T Pro,
        // after a close:
        //
        //     No process found for: com.ankama.dofustouch
        //     Recent #0: Task{#63 … sz=0}
        //
        // The game really was closed, but tapping the card relaunched it, and
        // the user concluded, going by what they saw, that closing did not
        // work. Cleaning up after the fact was tried and fails: once the
        // display is released, "am stack remove" returns 0 without doing
        // anything.
        var adb = new FakeAdbClient().WithShell("am start", "Status: ok");

        _ = await Build(adb).LaunchAsync(Serial, 0, Package, Component, displayId: 7);

        Assert.Contains(
            adb.ShellCalls,
            call => call.Contains("--activity-exclude-from-recents", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sur_un_afficheur_virtuel_le_jeu_s_ouvre_en_plein_ecran()
    {
        // **Without it, a Xiaomi Pad 6 under HyperOS (Android 14) opened the
        // game as a floating window inside the virtual display**: rounded
        // corners, a drag handle on top, the game a fraction of the window.
        // Seen on a player's screenshots on 2026-10-11. Enlarging that window
        // afterwards did not help: the game keeps the layout of its first
        // size, and the chat stayed out of view. Phones already open it full
        // screen, so the flag changes nothing for them.
        var adb = new FakeAdbClient().WithShell("am start", "Status: ok");

        _ = await Build(adb).LaunchAsync(Serial, 0, Package, Component, displayId: 7);
        _ = await Build(adb).OpenUriAsync(Serial, 0, "market://details?id=com.makeintouch.dtstuff", 7);

        Assert.Equal(2, adb.ShellCalls.Count(call => call.Contains("--windowingMode 1", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Sans_afficheur_virtuel_le_jeu_reste_dans_les_recents()
    {
        // A window that mirrors the phone's screen shows the game where the
        // user expects to find it in their list: hiding it from there would
        // take something away from them, not spare them anything.
        var adb = new FakeAdbClient().WithShell("am start", "Status: ok");

        _ = await Build(adb).LaunchAsync(Serial, 0, Package, Component, displayId: null);

        Assert.DoesNotContain(
            adb.ShellCalls,
            call => call.Contains("--activity-exclude-from-recents", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_arret_force_nomme_le_profil_et_le_paquet()
    {
        var adb = new FakeAdbClient().WithShell("am force-stop", string.Empty);

        Assert.True(await Build(adb).ForceStopAsync(Serial, 10, Package));

        Assert.Contains("am force-stop --user 10 " + Package, adb.ShellCalls);
    }

    [Fact]
    public async Task Un_arret_qui_n_atteint_pas_l_appareil_se_declare_manque()
    {
        // **What the previous test wrongly asserted.** It read a failure as
        // "the application might not have been open", and so passed off as a
        // success an order that had never gone through.
        //
        // Measured on both phones, platform-tools 37.0.1: "am force-stop"
        // returns 0 on a stopped package, and even on a package that does not
        // exist. It only returns 1 on "device offline" or "device not found",
        // meaning when the phone received nothing at all.
        var adb = new FakeAdbClient().FailShell("am force-stop", AdbErrorKind.DeviceOffline);

        Assert.False(await Build(adb).ForceStopAsync(Serial, 10, Package));
    }

    [Fact]
    public async Task Un_serial_vide_est_refuse_avant_toute_commande()
    {
        var adb = new FakeAdbClient();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Build(adb).LaunchAsync(" ", 0, Package, Component, null));

        Assert.Empty(adb.ShellCalls);
    }

    /// <summary>
    /// "dumpsys activity activities" recorded on the 13T Pro on 14/10/2026,
    /// game opened by DT Hub on a 2576x1080 virtual display, trimmed to the
    /// lines that matter. The phone's own screen holds another game task:
    /// only the virtual display's must be read.
    /// </summary>
    private static string Activites(string boundsOnDisplay) => string.Join(
        '\n',
        "ACTIVITY MANAGER ACTIVITIES (dumpsys activity activities)",
        "Display #183 (activities from top to bottom):",
        "  * Task{6e050f8 #828 type=standard A=10475:com.ankama.dofustouch U=0 visible=true visibleRequested=true mode=fullscreen translucent=false sz=1}",
        "    mLastNonFullscreenBounds=Rect(1107, 41 - 1469, 968)",
        "    * Hist  #0: ActivityRecord{262467391 u0 com.ankama.dofustouch/.MainActivity t828}",
        "      packageName=com.ankama.dofustouch processName=com.ankama.dofustouch",
        "      mLastReportedConfigurations:",
        "        mGlobalConfig={1.0 208mcc1mnc [fr_FR] ldltr sw720dp w1717dp h720dp 240dpi xlrg long land night -touch -keyb/v/h -nav/h winConfig={ mBounds=Rect(0, 0 - 2576, 1080) mAppBounds=Rect(0, 0 - 2576, 1080) mWindowingMode=fullscreen}}",
        "      CurrentConfiguration={1.0 208mcc1mnc [fr_FR] ldltr sw720dp w1717dp h720dp 240dpi xlrg long land night -touch -keyb/v/h -nav/h winConfig={ mBounds=" + boundsOnDisplay + " mAppBounds=Rect(0, 0 - 2576, 1080) mWindowingMode=fullscreen}}",
        "      supportsSizeChanges=SIZE_CHANGES_UNSUPPORTED_METADATA",
        "Display #0 (activities from top to bottom):",
        "  * Task{1a2b3c4 #800 type=standard A=10475:com.ankama.dofustouch U=999 visible=true visibleRequested=true mode=fullscreen translucent=false sz=1}",
        "    * Hist  #0: ActivityRecord{11111111 u999 com.ankama.dofustouch/.MainActivity t800}",
        "      CurrentConfiguration={1.0 winConfig={ mBounds=Rect(0, 0 - 1220, 2712) mAppBounds=Rect(0, 0 - 1220, 2712)}}");

    [Fact]
    public void Le_cadre_du_jeu_se_lit_sur_son_ecran_virtuel() =>
        Assert.Equal(
            (0, 0, 2576, 1080),
            AndroidAppLauncher.GameBounds(Activites("Rect(0, 0 - 2576, 1080)"), 183, Package));

    [Fact]
    public void Un_cadre_decale_est_lu_tel_quel()
    {
        // What Android does to a game that does not support size changes
        // when its display changes after it opened: it keeps the old size,
        // moved inside the display. The player's screenshot of 14/10/2026.
        Assert.Equal(
            (472, 0, 2104, 1080),
            AndroidAppLauncher.GameBounds(Activites("Rect(472, 0 - 2104, 1080)"), 183, Package));
    }

    [Fact]
    public void Le_jeu_d_un_autre_ecran_n_est_pas_lu() =>
        Assert.Equal(
            (0, 0, 1220, 2712),
            AndroidAppLauncher.GameBounds(Activites("Rect(0, 0 - 2576, 1080)"), 0, Package));

    [Theory]
    [InlineData(184)]
    [InlineData(-1)]
    public void Sans_le_jeu_sur_cet_ecran_rien_n_est_lu(int display) =>
        Assert.Null(AndroidAppLauncher.GameBounds(Activites("Rect(0, 0 - 2576, 1080)"), display, Package));

    [Fact]
    public void Une_sortie_inconnue_ne_donne_aucun_cadre() =>
        Assert.Null(AndroidAppLauncher.GameBounds("Can't find service: activity", 183, Package));

    /// <summary>
    /// "dumpsys window windows" and "dumpsys SurfaceFlinger" recorded on the
    /// 13T Pro on 14/10/2026, game opened small in a floating window then
    /// enlarged: the window claims the whole display, and only the layer's
    /// scale says the game is drawn at 59 %.
    /// </summary>
    private const string Fenetres = """
        WINDOW MANAGER WINDOWS (dumpsys window windows)
          Window #0 Window{15f852a u0 NavigationBar185}:
            mDisplayId=185 mSession=Session{b25c1ff 2749:1000} mClient=android.os.BinderProxy@ea40ea3
            Frames: parent=[0,0][2576,1080] display=[0,0][2576,1080] frame=[0,1009][2576,1080] last=[0,1009][2576,1080] insetsChanged=false
              mSurface=Surface(name=NavigationBar185#55731)/@0x55e0bf2
          Window #1 Window{7a37633 u0 com.ankama.dofustouch/com.ankama.dofustouch.MainActivity}:
            mDisplayId=185 taskId=835 mSession=Session{82caecf 24362:u0a10475} mClient=android.os.BinderProxy@25119a2
            Frames: parent=[0,0][2576,1080] display=[0,0][2576,1080] frame=[0,0][2576,1080] last=[0,0][2576,1080] insetsChanged=false
              mSurface=Surface(name=com.ankama.dofustouch/com.ankama.dofustouch.MainActivity#55752)/@0xd8c6626
        """;

    private const string Couches = """
          Layer [55731] NavigationBar185#55731
            bounds={0,1009,2576,1080} toDisplayTransform={ scale x=1.0000 y=1.0000 }
          Layer [55752] com.ankama.dofustouch/com.ankama.dofustouch.MainActivity#55752
            visible reason= buffer=104633993265161 frame=3272
            bounds={0,0,637.092,1519.58} toDisplayTransform={ scale x=0.5899 y=0.5899 }
        """;

    [Fact]
    public void L_etat_reel_reunit_la_tache_le_cadre_la_fenetre_et_la_couche() =>
        Assert.Equal(
            "mode=fullscreen, cadre (472, 0, 2104, 1080), fenêtre frame=[0,0][2576,1080], "
            + "couche bounds={0,0,637.092,1519.58} toDisplayTransform={ scale x=0.5899 y=0.5899 }",
            AndroidAppLauncher.GameWindowState(
                Activites("Rect(472, 0 - 2104, 1080)").Replace("#183", "#185", StringComparison.Ordinal),
                Fenetres,
                Couches,
                185,
                Package));

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es")]
    public void La_langue_du_diagnostic_ne_change_pas_la_lecture_de_l_agrandissement(string language)
    {
        var layers = Couches.Replace("0.5899", "1.3333", StringComparison.Ordinal);
        var state = AndroidAppLauncher.GameWindowState("", Fenetres, layers, 185, Package,
            System.Globalization.CultureInfo.GetCultureInfo(language));

        Assert.Contains("scale x=1.3333 y=1.3333", state);
        Assert.Equal(1.3333, AndroidAppLauncher.Upscale(state));
    }

    [Fact]
    public void Une_couche_sans_transformation_donne_ses_seules_bornes()
    {
        // Recorded on the 13T Pro, game full screen on a 1920x1080 display:
        // with nothing to transform, the line stops at the bounds.
        const string couche = """
              Layer [55752] com.ankama.dofustouch/com.ankama.dofustouch.MainActivity#55752
                visible reason= buffer=104921756073985 frame=455
                bounds={0,0,1080,1920}
                input{(0x0) canOccludePresentation touchCropId=55781 touchableRegion={0,0,1080,1920}}
            """;

        Assert.EndsWith(
            "couche bounds={0,0,1080,1920}",
            AndroidAppLauncher.GameWindowState("", Fenetres, couche, 185, Package),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Un_jeu_agrandi_par_le_telephone_est_rendu_en_resolution_reduite()
    {
        // A player's Galaxy S25 Ultra on 2026-10-17: one copy of the game blurry
        // under DT Hub as under TabDesk, its display the same as the others'.
        const string joueur = "mode=fullscreen, cadre (0, 0, 1920, 1080), fenêtre frame=[0,0][1920,1080], "
            + "couche bounds={0,0,1080,1920} toDisplayTransform={ scale x=1.3333 y=1.3333 }";

        Assert.Equal(1.3333, AndroidAppLauncher.Upscale(joueur));
    }

    [Fact]
    public async Task Le_mode_choisi_est_demande_pour_le_profil_de_la_copie()
    {
        // Answer of the 13T Pro on 2026-10-18, profile 10.
        var adb = new FakeAdbClient()
            .WithShell("cmd game mode", "Set game mode to `STANDARD` for user `10` in game `com.ankama.dofustouch`");

        Assert.True(await Build(adb).SetGameModeAsync(Serial, 10, Package, "standard"));
        Assert.Contains($"cmd game mode --user 10 standard {Package}", adb.ShellCalls);
    }

    [Fact]
    public async Task Un_mode_refuse_par_le_telephone_est_un_echec()
    {
        // Same phone: exit code 255 with the reason on the output.
        var adb = new FakeAdbClient().FailShell("cmd game mode", AdbErrorKind.Unknown);

        Assert.False(await Build(adb).SetGameModeAsync(Serial, 0, Package, "performance"));
    }

    [Fact]
    public void Les_modes_du_telephone_sans_le_mode_des_commandes()
    {
        // A player's Galaxy S25 Ultra on 2026-10-17.
        var modes = AndroidAppLauncher.ParseGameModes(
            "com.ankama.dofustouch current mode: performance, available game modes: [standard,performance,battery,custom]");

        Assert.Equal("performance", modes?.Current);
        Assert.Equal(["standard", "performance", "battery"], modes?.Available);
    }

    [Fact]
    public void Un_telephone_sans_mode_de_jeu_n_en_propose_aucun() =>
        Assert.Null(AndroidAppLauncher.ParseGameModes("Can't find service: game"));

    [Fact]
    public async Task Le_mode_en_cours_d_un_autre_profil_n_est_pas_connu()
    {
        // "cmd game list-modes" has no --user: it answers for the main profile.
        var adb = new FakeAdbClient().WithShell(
            "cmd game list-modes",
            "com.ankama.dofustouch current mode: performance, available game modes: [standard,performance,battery,custom]");

        var modes = await Build(adb).ReadGameModesAsync(Serial, 10, Package);

        Assert.Null(modes?.Current);
        Assert.Equal(["standard", "performance", "battery"], modes?.Available);
    }

    [Fact]
    public void Une_fenetre_reduite_ou_sans_transformation_n_est_pas_agrandie()
    {
        // The Xiaomi floating window shrunk by scaling, and a plain layer.
        Assert.Null(AndroidAppLauncher.Upscale("couche bounds={0,0,637.092,1519.58} toDisplayTransform={ scale x=0.5899 y=0.5899 }"));
        Assert.Null(AndroidAppLauncher.Upscale("couche bounds={0,0,1080,1920}"));
    }

    [Fact]
    public void Ce_qui_manque_est_marque_d_un_point_d_interrogation() =>
        Assert.Equal(
            "mode=?, cadre ?, fenêtre ?, couche ?",
            AndroidAppLauncher.GameWindowState("", "", "", 185, Package));

    [Fact]
    public async Task L_etat_est_demande_au_telephone()
    {
        var adb = new FakeAdbClient()
            .WithShell("dumpsys activity activities", Activites("Rect(0, 0 - 2576, 1080)"))
            .WithShell("dumpsys window windows", Fenetres)
            .WithShell("dumpsys SurfaceFlinger", Couches);

        var state = await Build(adb).ReadGameWindowAsync(Serial, 183, Package);

        Assert.StartsWith("mode=fullscreen, cadre (0, 0, 2576, 1080)", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_dump_en_echec_n_empeche_pas_de_lire_les_autres()
    {
        // A multi-megabyte dump that times out, or that the error classifier
        // mistakes for a refusal, must not take the rest of the line with it.
        var adb = new FakeAdbClient()
            .WithShell("dumpsys activity activities", Activites("Rect(472, 0 - 2104, 1080)"))
            .WithShell("dumpsys window windows", Fenetres)
            .WithShell("dumpsys SurfaceFlinger", () => throw new AdbException(AdbErrorKind.Unknown, "timeout"));

        var state = await Build(adb).ReadGameWindowAsync(Serial, 183, Package);

        Assert.StartsWith("mode=fullscreen, cadre (472, 0, 2104, 1080)", state, StringComparison.Ordinal);
        Assert.EndsWith("couche ?", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_telephone_qui_ne_repond_pas_donne_un_etat_illisible()
    {
        var adb = new FakeAdbClient()
            .WithShell("dumpsys", () => throw new AdbException(AdbErrorKind.Unknown, "device offline"));

        var state = await Build(adb).ReadGameWindowAsync(Serial, 183, Package);

        Assert.Contains("device offline", state, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_mode_standard_vient_en_tete_quel_que_soit_l_ordre_du_telephone()
    {
        // The settings list the modes in this order, and standard is the
        // one recommended (D203).
        var modes = AndroidAppLauncher.ParseGameModes(
            "com.ankama.dofustouch current mode: performance, available game modes: [performance,standard,battery]");

        Assert.Equal(["standard", "performance", "battery"], modes?.Available);
    }
}
