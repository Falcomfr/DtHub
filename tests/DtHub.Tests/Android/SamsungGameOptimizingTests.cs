using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Android;

/// <summary>
/// Samsung's game optimizing service, which drew the original game blurry on
/// a player's Galaxy S21 FE on 2026-11-24 (D237).
/// </summary>
public class SamsungGameOptimizingTests
{
    private const string Serial = "R5CW612EFBT";
    private const string Listed = "package:com.samsung.android.game.gos";

    [Fact]
    public async Task Un_GOS_actif_est_lu_actif()
    {
        var adb = new FakeAdbClient().WithShell("pm list packages -e", Listed);

        Assert.Equal(GameOptimizingState.On, await SamsungGameOptimizing.ReadAsync(adb, Serial));
    }

    [Fact]
    public async Task Un_GOS_coupe_est_lu_coupe()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list packages -e", "")
            .WithShell("pm list packages -d", Listed);

        Assert.Equal(GameOptimizingState.Off, await SamsungGameOptimizing.ReadAsync(adb, Serial));
    }

    [Fact]
    public async Task Sans_GOS_l_etat_est_absent()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm list packages -e", "")
            .WithShell("pm list packages -d", "");

        Assert.Equal(GameOptimizingState.Absent, await SamsungGameOptimizing.ReadAsync(adb, Serial));
    }

    [Fact]
    public async Task Un_paquet_au_nom_voisin_n_est_pas_GOS()
    {
        // "pm list packages" filters by substring.
        var adb = new FakeAdbClient()
            .WithShell("pm list packages -e", "package:com.samsung.android.game.gos.plugin")
            .WithShell("pm list packages -d", "");

        Assert.Equal(GameOptimizingState.Absent, await SamsungGameOptimizing.ReadAsync(adb, Serial));
    }

    [Fact]
    public async Task Une_lecture_qui_echoue_ne_dit_rien()
    {
        var adb = new FakeAdbClient().FailShell("pm list packages", AdbErrorKind.DeviceOffline);

        Assert.Null(await SamsungGameOptimizing.ReadAsync(adb, Serial));
    }

    [Fact]
    public async Task Couper_GOS_envoie_disable_user_puis_relit()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm disable-user", "Package com.samsung.android.game.gos new state: disabled-user")
            .WithShell("pm list packages -e", "")
            .WithShell("pm list packages -d", Listed);

        Assert.Equal(GameOptimizingState.Off, await SamsungGameOptimizing.SetAsync(adb, Serial, on: false));
        Assert.Contains("pm disable-user --user 0 com.samsung.android.game.gos", adb.ShellCalls);
        Assert.StartsWith("pm list packages", adb.ShellCalls[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reactiver_GOS_envoie_enable()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm enable", "Package com.samsung.android.game.gos new state: enabled")
            .WithShell("pm list packages -e", Listed);

        Assert.Equal(GameOptimizingState.On, await SamsungGameOptimizing.SetAsync(adb, Serial, on: true));
        Assert.Contains("pm enable com.samsung.android.game.gos", adb.ShellCalls);
    }

    [Fact]
    public async Task Un_refus_silencieux_se_voit_a_la_relecture()
    {
        var adb = new FakeAdbClient()
            .WithShell("pm disable-user", "")
            .WithShell("pm list packages -e", Listed);

        Assert.Equal(GameOptimizingState.On, await SamsungGameOptimizing.SetAsync(adb, Serial, on: false));
    }

    [Fact]
    public async Task Une_coupure_pendant_la_commande_remonte()
    {
        // A Wi-Fi drop is not a refusal: the caller says the phone is unreachable.
        var adb = new FakeAdbClient().FailShell("pm disable-user", AdbErrorKind.DeviceOffline);

        await Assert.ThrowsAsync<AdbException>(() => SamsungGameOptimizing.SetAsync(adb, Serial, on: false));
    }

    [Theory]
    [InlineData("samsung", true)]
    [InlineData("Samsung", true)]
    [InlineData("Xiaomi", false)]
    [InlineData(null, false)]
    public void Seul_un_Samsung_est_concerne(string? manufacturer, bool expected) =>
        Assert.Equal(expected, SamsungGameOptimizing.Concerns(manufacturer));
}
