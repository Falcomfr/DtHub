using DtHub.Core.Profiles;
using DtHub.Core.Scrcpy;
using DtHub.Core.Windows;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Windows;

public class WindowManagerServiceTests
{
    private const string NewDisplayLine = "[server] INFO: New display: 1080x1920/320 (id=7)";

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static LaunchTarget Target(int userId, string package = "com.exemple.app") => new()
    {
        DeviceId = "MATERIEL123",
        UserId = userId,
        PackageName = package,
        LaunchComponent = $"{package}/.Main",
    };

    /// <summary>
    /// Ouvre le nombre demandé de sessions, chacune avec son processus et sa
    /// fenêtre déclarée dans le bureau simulé.
    /// </summary>
    private static async Task<(ScrcpySessionManager Manager, List<ScrcpySession> Sessions, FakeWindowController Desktop)>
        OpenSessionsAsync(int count)
    {
        var launcher = new FakeProcessLauncher();
        var desktop = new FakeWindowController();

        for (var i = 0; i < count; i++)
        {
            launcher.Prepare(new FakeProcessSession(100 + i).Emit(NewDisplayLine));
        }

        var manager = new ScrcpySessionManager(
            new FakeScrcpyLocator(), new FakeAdbLocator(), launcher, new FakeAppLauncher());

        var sessions = new List<ScrcpySession>();

        for (var i = 0; i < count; i++)
        {
            var session = await manager.StartAsync(
                Target(i * 10), "USB0001", ScrcpyOptions.Default, null, CancellationToken.None);

            sessions.Add(session);
            desktop.AddWindow(1000 + i, session.ProcessId, $"App - DtHub [{session.Id}]");
        }

        return (manager, sessions, desktop);
    }

    [Fact]
    public async Task La_fenetre_d_une_session_est_retrouvee_par_son_identifiant_et_son_processus()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal(1000, await service.ResolveWindowAsync(sessions[0], CancellationToken.None));
    }

    [Fact]
    public async Task Une_fenetre_d_un_autre_processus_n_est_jamais_prise_pour_la_notre()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        // Même titre, autre processus : c'est le cas d'un second DT Hub lancé
        // par erreur, ou d'un logiciel qui imite le titre.
        desktop.AddWindow(9999, 55555, $"App - DtHub [{sessions[0].Id}]");

        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal(1000, await service.ResolveWindowAsync(sessions[0], CancellationToken.None));
    }

    [Fact]
    public async Task Une_fenetre_qui_n_apparait_pas_finit_par_etre_abandonnee()
    {
        var (manager, sessions, _) = await OpenSessionsAsync(1);
        await using var __ = manager;

        var emptyDesktop = new FakeWindowController();
        var service = new WindowManagerService(emptyDesktop, NoDelay)
        {
            WindowAppearanceTimeout = TimeSpan.FromMilliseconds(50),
            WindowPollInterval = TimeSpan.Zero,
        };

        Assert.Equal(0, await service.ResolveWindowAsync(sessions[0], CancellationToken.None));
    }

    [Fact]
    public async Task Toutes_les_fenetres_recoivent_exactement_la_meme_position_et_la_meme_taille()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(4);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        var moved = await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        Assert.Equal(4, moved);

        var rects = sessions.Select(s => desktop.GetWindowRect(s.WindowHandle)).Distinct().ToList();
        Assert.Single(rects);
    }

    [Fact]
    public async Task L_empilement_fonctionne_avec_deux_comme_avec_sept_sessions()
    {
        foreach (var count in new[] { 2, 3, 4, 7 })
        {
            var (manager, sessions, desktop) = await OpenSessionsAsync(count);
            await using var _ = manager;

            var service = new WindowManagerService(desktop, NoDelay);

            Assert.Equal(count, await service.ApplySizeAsync(sessions, 1, CancellationToken.None));
            Assert.Single(sessions.Select(s => desktop.GetWindowRect(s.WindowHandle)).Distinct());
        }
    }

    [Fact]
    public async Task Les_fenetres_sont_centrees_sur_la_zone_utilisable()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 3, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(FakeWindowController.PrimaryMonitor.WorkArea.CenterX, rect.CenterX);
        Assert.Equal(FakeWindowController.PrimaryMonitor.WorkArea.CenterY, rect.CenterY);
    }

    [Fact]
    public async Task Le_rapport_d_affichage_de_l_ecran_virtuel_est_respecte()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 3, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(1080.0 / 1920.0, rect.AspectRatio, 2);
    }

    [Fact]
    public async Task Le_plein_ecran_couvre_l_ecran_et_retire_la_bordure()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, WindowSizePresets.Default.FullscreenIndex, CancellationToken.None);

        foreach (var session in sessions)
        {
            Assert.Equal(FakeWindowController.PrimaryMonitor.Bounds, desktop.GetWindowRect(session.WindowHandle));
            Assert.Contains(session.WindowHandle, desktop.Borderless);
        }
    }

    [Fact]
    public async Task Sortir_du_plein_ecran_retablit_la_bordure()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ApplySizeAsync(sessions, WindowSizePresets.Default.FullscreenIndex, CancellationToken.None);
        await service.ApplySizeAsync(sessions, 1, CancellationToken.None);

        Assert.Empty(desktop.Borderless);
    }

    [Fact]
    public async Task Recentrer_remet_ensemble_des_fenetres_deplacees_a_la_main()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        // L'utilisateur éparpille les fenêtres.
        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(10, 10, 300, 300));
        desktop.MoveWindow(sessions[1].WindowHandle, new ScreenRect(900, 500, 200, 800));

        await service.RecenterAsync(sessions, CancellationToken.None);

        Assert.Single(sessions.Select(s => desktop.GetWindowRect(s.WindowHandle)).Distinct());
    }

    [Fact]
    public async Task Recentrer_conserve_la_taille_courante()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 0, CancellationToken.None);

        var before = desktop.GetWindowRect(sessions[0].WindowHandle);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(0, 0, 100, 100));
        await service.RecenterAsync(sessions, CancellationToken.None);

        Assert.Equal(before, desktop.GetWindowRect(sessions[0].WindowHandle));
        Assert.Equal(0, service.CurrentSizeIndex);
    }

    [Fact]
    public async Task Le_parcours_des_sessions_est_circulaire()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        var visited = new List<nint>();
        for (var i = 0; i < 4; i++)
        {
            visited.Add(service.FocusNext(sessions)!.WindowHandle);
        }

        Assert.Equal([1000, 1001, 1002, 1000], visited);
    }

    [Fact]
    public async Task Le_parcours_repart_de_la_fenetre_reellement_active()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        // L'utilisateur clique sur la troisième fenêtre.
        desktop.Foreground = 1002;

        Assert.Equal(1000, service.FocusNext(sessions)!.WindowHandle);
    }

    [Fact]
    public async Task Le_parcours_ignore_les_sessions_fermees()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        await manager.StopAsync(sessions[1].Id, CancellationToken.None);
        desktop.RemoveWindow(sessions[1].WindowHandle);

        var visited = new List<nint>();
        for (var i = 0; i < 3; i++)
        {
            visited.Add(service.FocusNext(sessions)!.WindowHandle);
        }

        Assert.DoesNotContain((nint)1001, visited);
    }

    [Fact]
    public async Task Sans_aucune_session_le_parcours_ne_designe_rien()
    {
        var (manager, _, desktop) = await OpenSessionsAsync(0);
        await using var __ = manager;

        Assert.Null(new WindowManagerService(desktop, NoDelay).FocusNext([]));
    }

    [Fact]
    public async Task Les_raccourcis_ne_s_appliquent_que_si_une_fenetre_geree_est_active()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        desktop.Foreground = 1001;
        Assert.True(service.IsManagedWindowFocused(sessions));

        // L'utilisateur bascule sur son navigateur : Ctrl+Tab doit lui revenir.
        desktop.Foreground = 424242;
        Assert.False(service.IsManagedWindowFocused(sessions));

        desktop.Foreground = 0;
        Assert.False(service.IsManagedWindowFocused(sessions));
    }

    [Fact]
    public async Task L_ecran_prefere_est_utilise_quand_il_est_present()
    {
        var secondary = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            Bounds = new ScreenRect(1920, 0, 2560, 1440),
            WorkArea = new ScreenRect(1920, 0, 2560, 1400),
        };

        var (manager, sessions, _) = await OpenSessionsAsync(1);
        await using var __ = manager;

        var desktop = new FakeWindowController(FakeWindowController.PrimaryMonitor, secondary);
        desktop.AddWindow(1000, sessions[0].ProcessId, $"App - DtHub [{sessions[0].Id}]");

        var service = new WindowManagerService(desktop, NoDelay)
        {
            PreferredMonitorDeviceName = @"\\.\DISPLAY2",
        };

        await service.ApplySizeAsync(sessions, 2, CancellationToken.None);

        var rect = desktop.GetWindowRect(1000)!.Value;
        Assert.True(rect.X >= 1920, $"La fenêtre devrait être sur le second écran : {rect}");
    }
}
