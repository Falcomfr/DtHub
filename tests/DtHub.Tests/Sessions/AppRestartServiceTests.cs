using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Sessions;

/// <summary>
/// Relancer une instance ne doit plus rouvrir sa fenêtre : la session scrcpy
/// et son afficheur sont conservés, seul le jeu repart.
/// </summary>
public sealed class AppRestartServiceTests
{
    private const string NewDisplayLine = "[server] INFO: New display: 1920x1080/240 (id=42)";

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static LaunchTarget Target() => new()
    {
        DeviceId = "MATERIEL123",
        Serial = "USB0001",
        UserId = 999,
        PackageName = "com.exemple.jeu",
        LaunchComponent = "com.exemple.jeu/.Main",
        DisplayName = "XSpace",
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

        return (manager, session, apps);
    }

    [Fact]
    public async Task La_relance_arrete_l_application_avant_de_la_redemarrer()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;

        var service = new AppRestartService(apps, NoDelay);

        var result = await service.RestartAsync(session, CancellationToken.None);

        Assert.Equal(AppRestartOutcome.Restarted, result.Outcome);
        Assert.Equal(["USB0001|999|com.exemple.jeu"], apps.ForceStops);
        Assert.Single(apps.Calls);
    }

    [Fact]
    public async Task L_application_repart_sur_le_meme_afficheur()
    {
        // C'est tout l'intérêt de la relance courte : l'afficheur est conservé,
        // donc la fenêtre ne bouge pas et le jeu garde sa mise en page.
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;

        var service = new AppRestartService(apps, NoDelay);

        await service.RestartAsync(session, CancellationToken.None);

        Assert.Equal(session.VirtualDisplayId, apps.Calls[0].DisplayId);
        Assert.Equal("com.exemple.jeu/.Main", apps.Calls[0].Component);
    }

    [Fact]
    public async Task Un_demarrage_refuse_est_signale_avec_son_message()
    {
        var (manager, session, apps) = await OpenAsync();
        await using var _ = manager;

        apps.Outcome = AppLaunchResult.Failure("Le profil Android est arrêté.");

        var service = new AppRestartService(apps, NoDelay);

        var result = await service.RestartAsync(session, CancellationToken.None);

        Assert.Equal(AppRestartOutcome.Failed, result.Outcome);
        Assert.Equal("Le profil Android est arrêté.", result.UserMessage);
    }

    [Fact]
    public async Task Sans_afficheur_connu_la_relance_demande_le_repli()
    {
        // Rien n'est tenté : sans afficheur, redémarrer le jeu l'enverrait sur
        // l'écran du téléphone.
        var processes = new FakeProcessLauncher();
        processes.Prepare(new FakeProcessSession(100));

        var apps = new FakeAppLauncher();
        await using var manager = new ScrcpySessionManager(
            new FakeScrcpyLocator(), new FakeAdbLocator(), processes, apps);

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default with { UseVirtualDisplay = false }, null, CancellationToken.None);

        apps.ForceStops.Clear();

        var service = new AppRestartService(apps, NoDelay);

        var result = await service.RestartAsync(session, CancellationToken.None);

        Assert.Equal(AppRestartOutcome.NoDisplay, result.Outcome);
        Assert.Empty(apps.ForceStops);
    }
}
