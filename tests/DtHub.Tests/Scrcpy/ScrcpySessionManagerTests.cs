using DtHub.Core;
using DtHub.Core.Processes;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
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
        // This is the central requirement: any identifier at all, not just
        // 999.
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

        // No technical identifier in the title: this is what the user will
        // read in their taskbar.
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

        // A scrcpy process from other software: never entrusted to the
        // manager.
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

    [Fact]
    public async Task Les_sessions_actives_suivent_l_ordre_configure()
    {
        var launcher = new FakeProcessLauncher()
            .Prepare(new FakeProcessSession(100).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(101).Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var first = await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        var second = await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        // The user put the cloned profile first.
        manager.OrderKey = s => s.Target.UserId == 999 ? 0 : 1;

        Assert.Equal([second.Id, first.Id], [.. manager.ActiveSessions.Select(s => s.Id)]);
    }

    [Fact]
    public async Task Sans_ordre_configure_les_sessions_restent_dans_leur_ordre_de_demarrage()
    {
        var launcher = new FakeProcessLauncher()
            .Prepare(new FakeProcessSession(100).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(101).Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var first = await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        var second = await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        Assert.Equal([first.Id, second.Id], [.. manager.ActiveSessions.Select(s => s.Id)]);
    }

    [Fact]
    public async Task Une_instance_hors_du_classement_passe_en_dernier()
    {
        var launcher = new FakeProcessLauncher()
            .Prepare(new FakeProcessSession(100).Emit(NewDisplayLine))
            .Prepare(new FakeProcessSession(101).Emit(NewDisplayLine));

        await using var manager = Manager(launcher, new FakeAppLauncher());

        var known = await manager.StartAsync(Target(0), ScrcpyOptions.Default, null, CancellationToken.None);
        var unknown = await manager.StartAsync(Target(999), ScrcpyOptions.Default, null, CancellationToken.None);

        manager.OrderKey = s => s.Target.UserId == 0 ? 3 : int.MaxValue;

        Assert.Equal([known.Id, unknown.Id], [.. manager.ActiveSessions.Select(s => s.Id)]);
    }

    /// <summary>
    /// Waits for a condition to become true, without exceeding a timeout.
    ///
    /// A window's death is detected by the read loop, which runs in the
    /// background: there is nothing to await directly, and a fixed wait would
    /// either be too short on a loaded machine, or wasted time.
    /// </summary>
    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }

    [Fact]
    public async Task Fermer_une_fenetre_arrete_le_jeu_sur_le_telephone()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10), ScrcpyOptions.Default, null, CancellationToken.None);

        // Opening already stops the game before relaunching it on the right
        // display: it's the closing we're observing, not this leftover.
        appLauncher.ForceStops.Clear();

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal("USB0001|10|com.ankama.dofustouch", Assert.Single(appLauncher.ForceStops));
    }

    /// <summary>The address the phone had when the session opened.</summary>
    private const string AncienPort = "192.168.1.23:41207";

    /// <summary>
    /// The one it carries at closing, since the port has changed.
    /// </summary>
    private const string PortDuMoment = "192.168.1.23:40787";

    [Fact]
    public async Task Le_jeu_s_arrete_a_l_adresse_du_moment_et_non_a_celle_du_lancement()
    {
        // **The bug this test pins down.** Recorded in the log, on a phone
        // whose wireless debugging changes port on every resume: "am
        // force-stop … pour 192.168.1.23:41207 : adb.exe: device offline". The
        // session targeted the address noted when it opened, the phone was
        // carrying a different one, and the game outlived its window.
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10, serial: AncienPort), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();

        // The old address really is dead: if it were still chosen, the stop
        // would fail instead of aiming elsewhere.
        appLauncher.Unreachable.Add(AncienPort);
        manager.CurrentSerial = _ => PortDuMoment;

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal(
            PortDuMoment + "|10|com.ankama.dofustouch",
            Assert.Single(appLauncher.ForceStops));
    }

    [Fact]
    public async Task Une_adresse_du_moment_perimee_laisse_essayer_celle_du_lancement()
    {
        // The scan happens every two seconds: it can narrowly miss a port
        // change and return an address older than the one the session carries.
        // Giving up there would mean giving up over our own mistake.
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();
        appLauncher.Unreachable.Add(PortDuMoment);
        manager.CurrentSerial = _ => PortDuMoment;

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal(
            [PortDuMoment + "|10|com.ankama.dofustouch", "USB0001|10|com.ankama.dofustouch"],
            appLauncher.ForceStops);
    }

    /// <summary>The mDNS name under which the phone stays reachable.</summary>
    private const string NomMdns = "adb-96ca0f7b-rq6A0u._adb-tls-connect._tcp";

    [Fact]
    public async Task Quand_les_adresses_connues_echouent_on_redemande_ou_est_l_appareil()
    {
        // **Measured on the phone, and this is what made the first fix miss.**
        // By expiring the address by hand, the stop went out at 14:52:26
        // toward the dead address, and the application knew the right one by
        // 14:52:28. Both known addresses come from the past: the scanned one
        // lags by up to two seconds, and that is exactly the time it takes a
        // port to change.
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10, serial: AncienPort), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();

        // Both memories are dead: the one from launch and the one from the
        // last scan, which has not yet seen the port change.
        appLauncher.Unreachable.Add(AncienPort);
        manager.CurrentSerial = _ => AncienPort;
        manager.LookUpSerial = (_, _) => Task.FromResult<string?>(NomMdns);

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal(
            [AncienPort + "|10|com.ankama.dofustouch", NomMdns + "|10|com.ankama.dofustouch"],
            appLauncher.ForceStops);
    }

    [Fact]
    public async Task On_ne_redemande_rien_quand_l_arret_a_abouti()
    {
        // The lookup costs a scan. On the path that works, which is the vast
        // majority, it must not happen.
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10), ScrcpyOptions.Default, null, CancellationToken.None);

        var recherches = 0;
        manager.LookUpSerial = (_, _) =>
        {
            recherches++;
            return Task.FromResult<string?>(NomMdns);
        };

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal(0, recherches);
    }

    [Fact]
    public async Task Un_jeu_qu_on_n_a_pas_pu_arreter_est_annonce()
    {
        // The window is gone: nothing on screen can show that the character is
        // still online anymore. Staying silent would leave it to be discovered
        // on the next launch.
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();
        appLauncher.Unreachable.Add("USB0001");

        ScrcpySession? annonce = null;
        manager.AppStopFailed += (_, s) => annonce = s;

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Same(session, annonce);
    }

    [Fact]
    public async Task Un_arret_qui_aboutit_n_annonce_rien()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(userId: 10), ScrcpyOptions.Default, null, CancellationToken.None);

        var annonces = 0;
        manager.AppStopFailed += (_, _) => annonces++;

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Equal(0, annonces);
    }

    [Fact]
    public async Task Le_reglage_decoche_laisse_le_jeu_tourner()
    {
        var launcher = new FakeProcessLauncher().Prepare(new FakeProcessSession().Emit(NewDisplayLine));
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.Empty(appLauncher.ForceStops);
    }

    [Fact]
    public async Task Une_fin_demandee_se_declare_comme_telle()
    {
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);
        await using var manager = Manager(launcher, new FakeAppLauncher());

        var session = await manager
            .StartAsync(Target(), ScrcpyOptions.Default, null, CancellationToken.None)
            .ConfigureAwait(true);

        await manager.StopAsync(session.Id, CancellationToken.None).ConfigureAwait(true);

        Assert.True(session.End.Requested);
        Assert.True(session.End.EverRan);
    }

    [Fact]
    public async Task Une_fin_subie_ne_se_declare_pas_demandee()
    {
        // Nothing distinguished them: both end up in the stopped state.
        // Without this marker, there was no way to decide whether to reopen.
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);
        await using var manager = Manager(launcher, new FakeAppLauncher());

        var session = await manager
            .StartAsync(Target(), ScrcpyOptions.Default, null, CancellationToken.None)
            .ConfigureAwait(true);

        process.Exit(1);

        Assert.True(await Eventually(() => !session.IsAlive).ConfigureAwait(true), "la session est restée vivante.");
        Assert.False(session.End.Requested);
        Assert.True(session.End.EverRan);
    }

    [Fact]
    public async Task Une_session_qui_n_a_jamais_ouvert_ne_pretend_pas_avoir_tourne()
    {
        // No display line: startup times out without the session ever having
        // opened.
        var process = new FakeProcessSession();
        var launcher = new FakeProcessLauncher().Prepare(process);
        await using var manager = Manager(launcher, new FakeAppLauncher(), TimeSpan.FromMilliseconds(200));

        var session = await manager
            .StartAsync(Target(), ScrcpyOptions.Default, null, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.False(session.End.EverRan);
    }

    [Fact]
    public async Task Une_fenetre_fermee_a_la_main_arrete_aussi_le_jeu()
    {
        // The hard path shared by closing the scrcpy window and by unplugging
        // the phone: none of our code gets called, only the process's exit
        // goes quiet.
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        await manager.StartAsync(Target(userId: 42), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();
        process.Exit(0);

        Assert.True(
            await Eventually(() => appLauncher.ForceStops.Count > 0),
            "Le jeu n'a pas été arrêté après la mort de la fenêtre.");

        Assert.Equal("USB0001|42|com.ankama.dofustouch", appLauncher.ForceStops[0]);
    }

    [Fact]
    public async Task Le_jeu_n_est_arrete_qu_une_fois_par_session()
    {
        // The voluntary close and the end of the output read both happen for
        // the same session. One extra round trip would cost against the tight
        // budget of closing the application.
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();

        await manager.StopAsync(session.Id, CancellationToken.None);
        await Eventually(() => appLauncher.ForceStops.Count > 1);

        Assert.Single(appLauncher.ForceStops);
    }

    [Fact]
    public async Task Chaque_compte_ferme_n_arrete_que_son_propre_profil()
    {
        var first = new FakeProcessSession(100).Emit(NewDisplayLine);
        var second = new FakeProcessSession(101).Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(first).Prepare(second);
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var principal = await manager.StartAsync(
            Target(userId: 0), ScrcpyOptions.Default, null, CancellationToken.None);
        await manager.StartAsync(
            Target(userId: 999), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();

        await manager.StopAsync(principal.Id, CancellationToken.None);

        Assert.Equal("USB0001|0|com.ankama.dofustouch", Assert.Single(appLauncher.ForceStops));
    }

    [Fact]
    public async Task Le_jeu_est_arrete_apres_le_depart_de_scrcpy_et_non_avant()
    {
        // The order is not cosmetic: it is scrcpy that notifies its server,
        // and the server that releases the virtual display. Killing the game
        // first would amount to undoing this chain.
        var process = new FakeProcessSession().Emit(NewDisplayLine);
        var launcher = new FakeProcessLauncher().Prepare(process);

        bool? scrcpyPartiAuMomentDeLArret = null;
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher);
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        appLauncher.ForceStops.Clear();
        appLauncher.OnForceStop = () => scrcpyPartiAuMomentDeLArret ??= process.HasExited;

        await manager.StopAsync(session.Id, CancellationToken.None);

        Assert.True(
            await Eventually(() => appLauncher.ForceStops.Count > 0),
            "Le jeu n'a pas été arrêté.");

        Assert.Single(appLauncher.ForceStops);
        Assert.True(scrcpyPartiAuMomentDeLArret, "Le jeu a été arrêté avant que scrcpy ne soit parti.");
    }

    [Fact]
    public async Task Un_jeu_que_nous_n_avons_pas_ouvert_n_est_pas_arrete()
    {
        // scrcpy dies before creating its display: we never launched the game.
        // If it is running anyway, someone is playing it on the phone, and it
        // is not our place to close it.
        var process = new FakeProcessSession();
        var launcher = new FakeProcessLauncher().Prepare(process);
        var appLauncher = new FakeAppLauncher();

        await using var manager = Manager(launcher, appLauncher, TimeSpan.FromMilliseconds(200));
        manager.StopAppOnClose = true;

        var session = await manager.StartAsync(
            Target(), ScrcpyOptions.Default, null, CancellationToken.None);

        await manager.StopAsync(session.Id, CancellationToken.None);
        await Eventually(() => appLauncher.ForceStops.Count > 0);

        Assert.Empty(appLauncher.ForceStops);
    }
}
