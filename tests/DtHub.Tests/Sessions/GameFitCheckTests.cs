using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Sessions;

/// <summary>
/// A game that does not fill its virtual display is relaunched once on the
/// same display. Seen on 14/10/2026 on a Galaxy Tab S10 Ultra (Android 16):
/// the game drawn off to the right, a black band on the left, its panel cut.
/// </summary>
public sealed class GameFitCheckTests
{
    private const string NewDisplayLine = "[server] INFO: New display: 2576x1080/240 (id=42)";

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static LaunchTarget Target() => new()
    {
        DeviceId = "MATERIEL123",
        Serial = "USB0001",
        UserId = 0,
        PackageName = "com.ankama.dofustouch",
        LaunchComponent = "com.ankama.dofustouch/.MainActivity",
        DisplayName = "Principal",
    };

    private static async Task<(ScrcpySessionManager Manager, ScrcpySession Session, FakeAppLauncher Apps)>
        OpenAsync()
    {
        var processes = new FakeProcessLauncher();
        processes.Prepare(new FakeProcessSession(100).Emit(NewDisplayLine));

        var apps = new FakeAppLauncher();
        var manager = new ScrcpySessionManager(
            new FakeScrcpyLocator(), new FakeAdbLocator(), processes, apps);

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        apps.Calls.Clear();
        apps.ForceStops.Clear();

        return (manager, session, apps);
    }

    private static GameFitCheck Check(FakeAppLauncher apps) =>
        new(apps, new AppRestartService(apps, NoDelay), NoDelay);

    [Fact]
    public async Task Un_jeu_qui_remplit_son_ecran_n_est_pas_relance()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        apps.Bounds.Enqueue((0, 0, 2576, 1080));

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Fills, result.Outcome);
        Assert.Equal("lecture 1", result.State);
        Assert.Empty(apps.Calls);
    }

    [Fact]
    public async Task Un_jeu_decale_est_relance_sur_le_meme_ecran_puis_relu()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        apps.Bounds.Enqueue((472, 0, 2104, 1080));
        apps.Bounds.Enqueue((0, 0, 2576, 1080));

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Relaunched, result.Outcome);
        Assert.Equal((472, 0, 2104, 1080), result.Before);
        Assert.Equal((0, 0, 2576, 1080), result.After);

        // The misfit is what the report is for: kept even once repaired.
        Assert.Equal("lecture 1", result.State);
        Assert.Equal("lecture 2", result.StateAfter);
        Assert.Equal(["USB0001|0|com.ankama.dofustouch"], apps.ForceStops);
        Assert.Equal(42, Assert.Single(apps.Calls).DisplayId);
    }

    [Fact]
    public async Task Un_jeu_tourne_d_un_quart_remplit_aussi_son_ecran()
    {
        // A display that turns with its content gives the game its two sides
        // swapped. Taking that for a misfit would relaunch at every opening.
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        apps.Bounds.Enqueue((0, 0, 1080, 2576));

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Fills, result.Outcome);
        Assert.Empty(apps.Calls);
    }

    [Fact]
    public async Task Un_ecart_qu_une_relance_ne_repare_pas_n_est_plus_relance()
    {
        // A letterbox on a very wide screen, or a manufacturer that ignores
        // the full screen request: the relaunch gives the same bounds, and
        // repeating it at every opening would cost the player for nothing.
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        var check = Check(apps);
        apps.Bounds.Enqueue((472, 0, 2104, 1080));
        apps.Bounds.Enqueue((472, 0, 2104, 1080));
        await check.CheckAsync(session, CancellationToken.None);
        apps.Calls.Clear();
        apps.ForceStops.Clear();
        apps.Bounds.Enqueue((472, 0, 2104, 1080));

        var result = await check.CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Misfit, result.Outcome);
        Assert.Empty(apps.ForceStops);
        Assert.Empty(apps.Calls);
    }

    [Fact]
    public async Task Une_relance_refusee_est_dite_comme_telle()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        apps.Bounds.Enqueue((472, 0, 2104, 1080));
        apps.Outcome = AppLaunchResult.Failure("Le profil Android est arrêté.");

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.RelaunchRefused, result.Outcome);
        Assert.Equal("lecture 1", result.State);
    }

    [Fact]
    public async Task Un_cadre_illisible_ne_declenche_rien()
    {
        // A manufacturer's dump we cannot read must not cost the player a
        // relaunch: it is only reported.
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Unknown, result.Outcome);
        Assert.Empty(apps.ForceStops);
    }

    [Fact]
    public async Task Une_fenetre_fermee_entre_temps_n_est_pas_relancee()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;
        apps.Bounds.Enqueue((472, 0, 2104, 1080));

        await manager.StopAsync(session.Id, CancellationToken.None);
        apps.ForceStops.Clear();

        var result = await Check(apps).CheckAsync(session, CancellationToken.None);

        Assert.Equal(GameFitOutcome.Unknown, result.Outcome);
        Assert.Empty(apps.ForceStops);
        Assert.Empty(apps.Calls);
    }
}
