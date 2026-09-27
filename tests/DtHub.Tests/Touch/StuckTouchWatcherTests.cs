using DtHub.Core.Touch;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Touch;

public class StuckTouchWatcherTests
{
    private const string Serie = "192.168.1.16:39791";

    private static readonly WatchedDisplay Cra = new("cra", Serie, 12, 0x1200);
    private static readonly WatchedDisplay Iop = new("iop", Serie, 13, 0x1300);

    private const string Libre = """
        Input Dispatcher State:
          TouchStatesByDisplay: <no displays touched>
        """;

    /// <summary>
    /// The live section as the phone writes it on Android 16, one window per
    /// display, reduced to the fields the reader looks at.
    /// </summary>
    private static string Pose(int display, int fingers) => $"""
        Input Dispatcher State:
          TouchStatesByDisplay:
            {display} :     Windows:
                0 : name='49c98e5 com.ankama.dofustouch/com.ankama.dofustouch.MainActivity', mDeviceStates=-1:[touchingPointers=[{string.Join(", ", Enumerable.Range(0, fingers).Select(i => $"Pointer(id={i}, FINGER)"))}], hoveringPointers=[]]
          CursorStatesByDisplay: <no displays touched by cursor>
        """;

    private DateTimeOffset _now = new(2026, 9, 27, 12, 45, 0, TimeSpan.Zero);

    private readonly FakeWindowController _windows = new();

    private readonly List<StuckTouchReport> _reports = [];

    private StuckTouchWatcher Watcher(FakeAdbClient adb)
    {
        var watcher = new StuckTouchWatcher(
            adb,
            _windows,
            () => _now,
            (duration, _) =>
            {
                _now += duration;
                return Task.CompletedTask;
            });

        watcher.Reported += (_, report) => _reports.Add(report);

        return watcher;
    }

    private void Wait(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    [Fact]
    public async Task Un_doigt_pose_moins_longtemps_que_la_patience_n_est_pas_touche()
    {
        var watcher = Watcher(new FakeAdbClient().WithShellChanging("dumpsys input", Pose(12, 2)));

        await watcher.CheckAsync([Cra]);
        Wait(2);
        await watcher.CheckAsync([Cra]);

        Assert.Empty(_windows.Releases);
        Assert.Empty(_reports);
    }

    [Fact]
    public async Task Des_doigts_restes_poses_sans_bouton_enfonce_sont_relaches_dans_la_fenetre_du_compte()
    {
        var watcher = Watcher(new FakeAdbClient()
            .WithShellChanging("dumpsys input", Pose(12, 2), Pose(12, 2), Libre));

        await watcher.CheckAsync([Cra]);
        Wait(3);
        await watcher.CheckAsync([Cra]);

        Assert.Equal([Cra.Window], _windows.Releases);
        Assert.Equal([new StuckTouchReport("cra", StuckTouchOutcome.Released, 2)], _reports);
    }

    [Fact]
    public async Task Pendant_un_glisser_bouton_tenu_rien_n_est_relache()
    {
        // A finger that stays down while the button is held is a drag, or a
        // press held on purpose. Lifting it would cut the player's gesture.
        _windows.MouseDown = true;
        var watcher = Watcher(new FakeAdbClient().WithShell("dumpsys input", Pose(12, 1)));

        for (var i = 0; i < 6; i++)
        {
            await watcher.CheckAsync([Cra]);
            Wait(2);
        }

        Assert.Empty(_windows.Releases);
        Assert.Empty(_reports);
    }

    [Fact]
    public async Task Un_bouton_enfonce_remet_la_patience_a_zero()
    {
        var watcher = Watcher(new FakeAdbClient()
            .WithShellChanging("dumpsys input", Pose(12, 1), Pose(12, 1), Pose(12, 1), Pose(12, 1), Libre));

        await watcher.CheckAsync([Cra]);
        Wait(2);
        _windows.MouseDown = true;
        await watcher.CheckAsync([Cra]);
        Wait(2);
        _windows.MouseDown = false;
        await watcher.CheckAsync([Cra]);

        Assert.Empty(_windows.Releases);

        Wait(3);
        await watcher.CheckAsync([Cra]);

        Assert.Equal([Cra.Window], _windows.Releases);
    }

    [Fact]
    public async Task Un_relachement_sans_effet_demande_de_cliquer_puis_annonce_la_liberation()
    {
        var watcher = Watcher(new FakeAdbClient()
            .WithShellChanging("dumpsys input", Pose(12, 2), Pose(12, 2), Pose(12, 2), Pose(12, 2), Libre));

        await watcher.CheckAsync([Cra]);
        Wait(3);
        await watcher.CheckAsync([Cra]);

        Assert.Equal([new StuckTouchReport("cra", StuckTouchOutcome.NeedsClicks, 2)], _reports);

        // Still stuck: no second attempt, and the row is not told twice.
        Wait(2);
        await watcher.CheckAsync([Cra]);

        Assert.Single(_windows.Releases);
        Assert.Single(_reports);

        Wait(2);
        await watcher.CheckAsync([Cra]);

        Assert.Equal(StuckTouchOutcome.Cleared, _reports[^1].Outcome);
        Assert.Equal("cra", _reports[^1].Key);
    }

    [Fact]
    public async Task Chaque_compte_est_relache_dans_sa_propre_fenetre()
    {
        var watcher = Watcher(new FakeAdbClient()
            .WithShellChanging("dumpsys input", Pose(13, 1), Pose(13, 1), Libre));

        await watcher.CheckAsync([Cra, Iop]);
        Wait(3);
        await watcher.CheckAsync([Cra, Iop]);

        Assert.Equal([Iop.Window], _windows.Releases);
        Assert.Equal("iop", Assert.Single(_reports).Key);
    }

    [Fact]
    public async Task Deux_comptes_du_meme_telephone_ne_coutent_qu_une_lecture()
    {
        var adb = new FakeAdbClient().WithShell("dumpsys input", Libre);
        var watcher = Watcher(adb);

        await watcher.CheckAsync([Cra, Iop]);

        Assert.Single(adb.ShellCalls);
    }

    [Fact]
    public async Task Rien_n_est_lu_quand_aucune_fenetre_de_jeu_n_est_ouverte()
    {
        var adb = new FakeAdbClient().WithShell("dumpsys input", Libre);

        await Watcher(adb).CheckAsync([]);

        Assert.Empty(adb.ShellCalls);
    }

    [Fact]
    public async Task Un_telephone_qui_ne_repond_pas_n_arrete_pas_la_surveillance()
    {
        var watcher = Watcher(new FakeAdbClient().FailShell("dumpsys input"));

        await watcher.CheckAsync([Cra]);
        Wait(3);
        await watcher.CheckAsync([Cra]);

        Assert.Empty(_windows.Releases);
        Assert.Empty(_reports);
    }

    [Fact]
    public async Task Un_releve_illisible_ne_fait_rien_relacher()
    {
        var watcher = Watcher(new FakeAdbClient().WithShell("dumpsys input", "Can't find service: input"));

        await watcher.CheckAsync([Cra]);
        Wait(3);
        await watcher.CheckAsync([Cra]);

        Assert.Empty(_windows.Releases);
    }

    [Fact]
    public void La_commande_filtre_sur_le_telephone()
    {
        // The whole dump weighs about 68 KB on the development phone, the
        // filtered one 3 KB: every two seconds, over a Wi-Fi link that is
        // also carrying the video, the difference is not a detail.
        Assert.Contains("|", TouchStateReader.Command);
        Assert.Equal(["dumpsys", "input"], TouchStateReader.Command.Take(2));
    }
}
