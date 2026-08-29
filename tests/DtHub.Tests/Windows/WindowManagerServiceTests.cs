using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Windows;
using DtHub.Tests.Fakes;

namespace DtHub.Tests.Windows;

public class WindowManagerServiceTests
{
    private const string NewDisplayLine = "[server] INFO: New display: 1080x1920/320 (id=7)";

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static LaunchTarget Target(int userId) => new()
    {
        DeviceId = "MATERIEL123",
        Serial = "USB0001",
        UserId = userId,
        PackageName = "com.ankama.dofustouch",
        LaunchComponent = "com.ankama.dofustouch/.MainActivity",
        DisplayName = userId == 0 ? "Principal" : $"Profil {userId}",
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
                Target(i * 10), ScrcpyOptions.Default, null, CancellationToken.None);

            sessions.Add(session);
            desktop.AddWindow(1000 + i, session.ProcessId, $"Instance - DtHub [{session.Id}]");
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

        desktop.AddWindow(9999, 55555, $"Instance - DtHub [{sessions[0].Id}]");

        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal(1000, await service.ResolveWindowAsync(sessions[0], CancellationToken.None));
    }

    [Fact]
    public async Task Une_fenetre_qui_n_apparait_pas_finit_par_etre_abandonnee()
    {
        var (manager, sessions, _) = await OpenSessionsAsync(1);
        await using var __ = manager;

        var service = new WindowManagerService(new FakeWindowController(), NoDelay)
        {
            WindowAppearanceTimeout = TimeSpan.FromMilliseconds(50),
            WindowPollInterval = TimeSpan.Zero,
        };

        Assert.Equal(0, await service.ResolveWindowAsync(sessions[0], CancellationToken.None));
    }

    [Fact]
    public async Task Toutes_les_fenetres_se_superposent_exactement()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(4);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal(4, await service.ArrangeAsync(sessions, CancellationToken.None));
        Assert.Single(sessions.Select(s => desktop.GetWindowRect(s.WindowHandle)).Distinct());
    }

    [Fact]
    public async Task La_superposition_vaut_pour_deux_comme_pour_sept_instances()
    {
        foreach (var count in new[] { 2, 3, 4, 7 })
        {
            var (manager, sessions, desktop) = await OpenSessionsAsync(count);
            await using var _ = manager;

            var service = new WindowManagerService(desktop, NoDelay);

            Assert.Equal(count, await service.ArrangeAsync(sessions, CancellationToken.None));
            Assert.Single(sessions.Select(s => desktop.GetWindowRect(s.WindowHandle)).Distinct());
        }
    }

    [Fact]
    public async Task Chaque_taille_est_une_part_de_l_ecran_et_croit_avec_l_indice()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        List<int> heights = [];

        for (var index = 0; index < service.Presets.Percentages.Count; index++)
        {
            await service.ApplySizeAsync(sessions, index, CancellationToken.None);
            heights.Add(desktop.GetWindowRect(sessions[0].WindowHandle)!.Value.Height);
        }

        Assert.Equal(heights.OrderBy(h => h), heights);
        Assert.Equal(heights.Count, heights.Distinct().Count());
    }

    [Fact]
    public async Task Le_plein_ecran_couvre_l_ecran_entier_et_retire_la_bordure()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ApplySizeAsync(sessions, service.Presets.FullscreenIndex, CancellationToken.None);

        foreach (var session in sessions)
        {
            Assert.Equal(FakeWindowController.PrimaryMonitor.Bounds, desktop.GetWindowRect(session.WindowHandle));
            Assert.Contains(session.WindowHandle, desktop.Borderless);
        }

        // En sortir rétablit la bordure.
        await service.ApplySizeAsync(sessions, 0, CancellationToken.None);
        Assert.Empty(desktop.Borderless);
    }

    [Fact]
    public async Task Un_indice_de_taille_hors_bornes_est_ramene_dans_les_limites()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ApplySizeAsync(sessions, -5, CancellationToken.None);
        Assert.Equal(0, service.SizeIndex);

        await service.ApplySizeAsync(sessions, 99, CancellationToken.None);
        Assert.Equal(service.Presets.FullscreenIndex, service.SizeIndex);
    }

    [Fact]
    public async Task La_position_choisie_est_appliquee()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay) { Anchor = WindowAnchor.BottomRight };
        await service.ArrangeAsync(sessions, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var work = FakeWindowController.PrimaryMonitor.WorkArea;

        Assert.Equal(work.Right, rect.Right);
        Assert.Equal(work.Bottom, rect.Bottom);
    }

    [Fact]
    public async Task Le_rapport_d_affichage_de_l_ecran_virtuel_est_respecte()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        Assert.Equal(1080.0 / 1920.0, desktop.GetWindowRect(sessions[0].WindowHandle)!.Value.AspectRatio, 2);
    }

    [Fact]
    public async Task Remettre_en_place_rassemble_des_fenetres_deplacees_a_la_main()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        var expected = desktop.GetWindowRect(sessions[0].WindowHandle);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(10, 10, 300, 300));
        desktop.MoveWindow(sessions[1].WindowHandle, new ScreenRect(900, 500, 200, 800));

        await service.ArrangeAsync(sessions, CancellationToken.None);

        Assert.All(sessions, s => Assert.Equal(expected, desktop.GetWindowRect(s.WindowHandle)));
    }

    [Fact]
    public async Task Le_parcours_avant_est_circulaire()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        var visited = Enumerable.Range(0, 4).Select(_ => service.FocusNext(sessions)!.WindowHandle).ToList();

        Assert.Equal([1000, 1001, 1002, 1000], visited);
    }

    [Fact]
    public async Task Le_parcours_arriere_remonte_la_liste()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        desktop.Foreground = 1000;

        Assert.Equal(1002, service.FocusPrevious(sessions)!.WindowHandle);
        Assert.Equal(1001, service.FocusPrevious(sessions)!.WindowHandle);
    }

    [Fact]
    public async Task Le_parcours_repart_de_la_fenetre_reellement_active()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

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
        await service.ArrangeAsync(sessions, CancellationToken.None);

        await manager.StopAsync(sessions[1].Id, CancellationToken.None);
        desktop.RemoveWindow(sessions[1].WindowHandle);

        var visited = Enumerable.Range(0, 3).Select(_ => service.FocusNext(sessions)!.WindowHandle).ToList();

        Assert.DoesNotContain((nint)1001, visited);
    }

    [Fact]
    public void Sans_aucune_session_le_parcours_ne_designe_rien()
    {
        var service = new WindowManagerService(new FakeWindowController(), NoDelay);

        Assert.Null(service.FocusNext([]));
        Assert.Null(service.FocusPrevious([]));
    }

    [Fact]
    public async Task Les_raccourcis_ne_s_appliquent_que_si_une_fenetre_geree_est_active()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        desktop.Foreground = 1001;
        Assert.True(service.IsManagedWindowFocused(sessions));

        // L'utilisateur bascule sur son navigateur : Ctrl+Tab doit lui revenir.
        desktop.Foreground = 424242;
        Assert.False(service.IsManagedWindowFocused(sessions));
    }

    [Fact]
    public void La_zone_de_jeu_est_previsible_sans_deplacer_aucune_fenetre()
    {
        var desktop = new FakeWindowController();
        var service = new WindowManagerService(desktop, NoDelay) { Anchor = WindowAnchor.MiddleLeft };

        var area = service.PreviewGameArea(1080.0 / 1920.0);

        Assert.NotNull(area);
        Assert.Equal(FakeWindowController.PrimaryMonitor.WorkArea.X, area.Value.X);
    }
}
