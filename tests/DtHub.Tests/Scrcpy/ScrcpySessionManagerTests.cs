using DtHub.Core.Processes;
using DtHub.Core.Sessions;
using DtHub.Core;
using DtHub.Core.Scrcpy;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Scrcpy;

public class ScrcpySessionManagerTests
{
    private const string NewDisplayLine = "[server] INFO: New display: 1080x1920/320 (id=7)";

    private static LaunchTarget Target(int userId = 999, string serial = "USB0001") => new()
    {
        DeviceId = "MATERIEL123",
        Serial = serial,
        UserId = userId,
        PackageName = "com.ankama.dofustouch",
        LaunchComponent = "com.ankama.dofustouch/.MainActivity",
        DisplayName = userId == 0 ? "Principal" : "XSpace",
    };

    private static ScrcpySessionManager Manager(
        FakeProcessLauncher launcher,
        FakeAppLauncher appLauncher,
        TimeSpan? startupTimeout = null) =>
        new(new FakeScrcpyLocator(), new FakeAdbLocator(), launcher, appLauncher)
        {
            StartupTimeout = startupTimeout ?? TimeSpan.FromSeconds(5),
        };

    [Fact]
    public async Task Une_session_s_ouvre_et_lance_l_application_sur_l_afficheur_cree()
    {
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Running, session.State);
        Assert.Equal(7, session.VirtualDisplayId);

        var call = Assert.Single(appLauncher.Calls);
        Assert.Equal("USB0001", call.Serial);
        Assert.Equal(999, call.UserId);
        Assert.Equal(7, call.DisplayId);
        Assert.Equal("com.ankama.dofustouch/.MainActivity", call.Component);
    }

    [Fact]
    public async Task Le_profil_android_demande_est_bien_celui_transmis_au_lancement()
    {
        // C'est l'exigence centrale : n'importe quel identifiant, pas seulement 999.
        foreach (var userId in new[] { 0, 10, 42, 999, 1234 })
        {
            var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
            var appLauncher = new FakeAppLauncher();

            await using var manager = Manager(launcher, appLauncher);

            await manager.StartAsync(
                Target(userId), ScrcpyOptions.Default, null, CancellationToken.None);

            Assert.Equal(userId, Assert.Single(appLauncher.Calls).UserId);
        }
    }

    [Fact]
    public async Task Le_chemin_d_adb_est_impose_a_scrcpy_par_l_environnement()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        await manager.StartAsync(Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(@"C:\Dev\DTHub\adb\adb.exe", request.Environment["ADB"]);
        Assert.Equal(@"C:\Dev\DTHub\scrcpy\scrcpy.exe", request.FileName);
    }

    [Fact]
    public async Task Le_titre_de_fenetre_est_le_nom_de_l_instance()
    {
        var launcher = new FakeProcessLauncher()
            .Prepare(new FakeProcessSession(1).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(2).Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var first = await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        var second = await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        // Aucun identifiant technique dans le titre : c'est ce que
        // l'utilisateur lira dans sa barre des tâches.
        Assert.Equal($"{ProductInfo.Name} Principal", first.WindowTitle);
        Assert.Equal($"{ProductInfo.Name} XSpace", second.WindowTitle);
        Assert.DoesNotContain(first.Id, first.WindowTitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sans_afficheur_virtuel_aucune_application_n_est_lancee()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession());
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);

        var session = await manager.StartAsync(
            Target(),
            ScrcpyOptions.Default with { UseVirtualDisplay = false },
            null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Running, session.State);
        Assert.Empty(appLauncher.Calls);
    }

    [Fact]
    public async Task Une_erreur_de_scrcpy_est_traduite_et_la_session_echoue()
    {
        var process = new FakeProcessSession();
        process.Emit("ERROR: Could not find any ADB device", isError: true);
        process.Exit(1);

        var launcher = new FakeProcessLauncher().Prepare(process);

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Failed, session.State);
        Assert.Contains("câble", session.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_scrcpy_qui_ne_repond_pas_finit_par_abandonner_sans_bloquer()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession());

        await using var manager = Manager(launcher, new FakeAppLauncher(), TimeSpan.FromMilliseconds(200));

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Failed, session.State);
        Assert.Contains("à temps", session.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(launcher.Started[0].WasKilled);
    }

    [Fact]
    public async Task Une_application_qui_refuse_de_s_ouvrir_ferme_la_fenetre_vide()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher
        {
            Outcome = AppLaunchResult.Failure("L'application n'est plus installée pour ce profil Android."),
        };

        await using var manager = Manager(launcher, appLauncher);

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Failed, session.State);
        Assert.Contains("plus installée", session.FailureMessage, StringComparison.Ordinal);
        Assert.True(launcher.Started[0].WasKilled);
    }

    [Fact]
    public async Task Un_scrcpy_qui_ne_demarre_pas_donne_une_session_en_echec_et_non_une_exception()
    {
        var launcher = new FakeProcessLauncher
        {
            LaunchError = new ProcessLaunchException("scrcpy.exe", "Fichier introuvable."),
        };

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(ScrcpySessionState.Failed, session.State);
        Assert.Contains("journaux", session.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Plusieurs_sessions_coexistent_et_sont_listees_dans_l_ordre()
    {
        var launcher = new FakeProcessLauncher()
            .Prepare(new FakeProcessSession(1).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(2).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(3).Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);
        await manager.StartAsync(Target(0, "USB0002"), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal(3, manager.ActiveSessions.Count);
    }

    [Fact]
    public async Task Fermer_tout_ne_touche_que_les_sessions_ouvertes_par_dt_hub()
    {
        var mine = new FakeProcessSession(1).Emit(NewDisplayLine);
        var alsoMine = new FakeProcessSession(2).Emit(NewDisplayLine);

        // Un processus scrcpy d'un autre logiciel : jamais confié au gestionnaire.
        var somebodyElses = new FakeProcessSession(99);

        var launcher = new FakeProcessLauncher().Prepare(mine).Prepare(alsoMine);

        await using var manager = Manager(launcher, new FakeAppLauncher());

        await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        await manager.StopAllAsync(CancellationToken.None);

        Assert.True(mine.WasKilled);
        Assert.True(alsoMine.WasKilled);
        Assert.False(somebodyElses.WasKilled);
        Assert.Empty(manager.ActiveSessions);
    }

    [Fact]
    public async Task Une_session_fermee_isolement_laisse_les_autres_ouvertes()
    {
        var first = new FakeProcessSession(1).Emit(NewDisplayLine);
        var second = new FakeProcessSession(2).Emit(NewDisplayLine);

        var launcher = new FakeProcessLauncher().Prepare(first).Prepare(second);

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var a = await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        await manager.StopAsync(a.Id, CancellationToken.None);

        Assert.True(first.WasKilled);
        Assert.False(second.WasKilled);
        Assert.Single(manager.ActiveSessions);
    }

    [Fact]
    public async Task Les_sessions_terminees_peuvent_etre_retirees_de_la_liste()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        await manager.StopAsync(session.Id, CancellationToken.None);
        manager.PruneFinished();

        Assert.Empty(manager.Sessions);
    }

    [Fact]
    public async Task Un_changement_d_etat_est_signale()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var states = new List<ScrcpySessionState>();
        manager.SessionChanged += (_, session) => states.Add(session.State);

        var started = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        await manager.StopAsync(started.Id, CancellationToken.None);

        Assert.Contains(ScrcpySessionState.Running, states);
        Assert.Contains(ScrcpySessionState.Stopped, states);
    }
}
