using DtHub.Core.Adb;
using DtHub.Core.Dofus;
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
            "am start --user 999 --display 42 --activity-exclude-from-recents -n " + Component,
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
}
