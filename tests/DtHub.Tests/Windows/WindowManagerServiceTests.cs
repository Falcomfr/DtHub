using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;
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
        OpenSessionsAsync(int count, ScrcpyOptions? options = null)
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
                Target(i * 10),
                options ?? ScrcpyOptions.Default,
                null,
                CancellationToken.None);

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
    public async Task Le_rapport_s_applique_a_la_zone_client_et_non_au_cadre()
    {
        // C'est la zone client que scrcpy remplit. Appliquer le rapport au
        // rectangle extérieur laisse des bandes noires sur les côtés, de la
        // largeur exacte de la barre de titre.
        var (manager, sessions, desktop) = await OpenSessionsAsync(
            1, ScrcpyOptions.Default);

        await using var _ = manager;

        desktop.Chrome = (16, 48);

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync([], 1, CancellationToken.None);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var client = new ScreenRect(0, 0, rect.Width - 16, rect.Height - 48);

        Assert.Equal(16.0 / 9.0, client.AspectRatio, 2);
    }

    [Fact]
    public async Task La_fenetre_tient_dans_la_part_d_ecran_demandee_cadre_compris()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        desktop.Chrome = (16, 48);

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, 1, CancellationToken.None);

        var work = FakeWindowController.PrimaryMonitor.WorkArea;
        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.True(rect.Width <= (int)Math.Round(work.Width * 0.70));
        Assert.True(rect.Height <= (int)Math.Round(work.Height * 0.70));
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

    private static StoredWindowRect Remembered(ScreenRect rect) =>
        StoredWindowRect.From(rect, FakeWindowController.PrimaryMonitor);

    [Fact]
    public async Task Une_instance_deja_placee_retrouve_exactement_son_rectangle()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.RestoreAsync(
            sessions,
            new Dictionary<string, StoredWindowRect>
            {
                [sessions[0].Target.Key] = Remembered(new ScreenRect(300, 200, 900, 900)),
            },
            CancellationToken.None);

        Assert.Equal(
            new ScreenRect(300, 200, 900, 900),
            desktop.GetWindowRect(sessions[0].WindowHandle));
    }

    [Fact]
    public async Task Une_instance_jamais_placee_prend_le_rectangle_calcule_depuis_l_ancrage()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay) { Anchor = WindowAnchor.TopLeft };

        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var work = FakeWindowController.PrimaryMonitor.WorkArea;
        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(work.X, rect.X);
        Assert.Equal(work.Y, rect.Y);
    }

    [Fact]
    public async Task Deux_instances_peuvent_avoir_des_geometries_differentes()
    {
        // C'est la rupture avec la superposition systématique : chaque fenêtre
        // retrouve l'endroit où elle a été laissée.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.RestoreAsync(
            sessions,
            new Dictionary<string, StoredWindowRect>
            {
                [sessions[0].Target.Key] = Remembered(new ScreenRect(0, 0, 800, 600)),
                [sessions[1].Target.Key] = Remembered(new ScreenRect(900, 100, 640, 480)),
            },
            CancellationToken.None);

        Assert.Equal(new ScreenRect(0, 0, 800, 600), desktop.GetWindowRect(sessions[0].WindowHandle));
        Assert.Equal(new ScreenRect(900, 100, 640, 480), desktop.GetWindowRect(sessions[1].WindowHandle));
    }

    [Fact]
    public async Task Ouvrir_une_instance_ne_deplace_pas_les_fenetres_deja_ouvertes()
    {
        // Relancer un compte ne doit pas arracher les autres fenêtres à
        // l'endroit où l'utilisateur les a mises.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var untouched = new ScreenRect(1234, 567, 700, 500);
        desktop.MoveWindow(sessions[1].WindowHandle, untouched);

        await service.RestoreAsync(
            [sessions[0]], new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        Assert.Equal(untouched, desktop.GetWindowRect(sessions[1].WindowHandle));
    }

    [Fact]
    public async Task Le_plein_ecran_l_emporte_sur_la_geometrie_memorisee()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync([], service.Presets.FullscreenIndex, CancellationToken.None);

        await service.RestoreAsync(
            sessions,
            new Dictionary<string, StoredWindowRect>
            {
                [sessions[0].Target.Key] = Remembered(new ScreenRect(300, 200, 400, 300)),
            },
            CancellationToken.None);

        Assert.Equal(
            FakeWindowController.PrimaryMonitor.Bounds,
            desktop.GetWindowRect(sessions[0].WindowHandle));
    }

    [Fact]
    public async Task La_geometrie_capturee_est_le_rectangle_exterieur_de_la_fenetre()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(120, 340, 560, 420));

        var captured = service.CaptureGeometries(sessions);

        Assert.Equal(sessions[0].Target.Key, captured[0].Key);
        Assert.Equal(new ScreenRect(120, 340, 560, 420), captured[0].Rect.Bounds);
    }

    [Fact]
    public async Task Rien_n_est_capture_en_plein_ecran()
    {
        // Le rectangle vaudrait l'écran entier, et la fenêtre y est sans
        // bordure : le restaurer en taille normale donnerait une fenêtre
        // bordée débordant sous la barre des tâches.
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync(sessions, service.Presets.FullscreenIndex, CancellationToken.None);

        Assert.Empty(service.CaptureGeometries(sessions));
    }


    [Fact]
    public async Task Le_titre_des_fenetres_ouvertes_peut_etre_reecrit()
    {
        // scrcpy ne fixe son titre qu'au démarrage : sans réécriture, le
        // rappel du raccourci resterait périmé jusqu'à la prochaine ouverture.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var renamed = service.Retitle(sessions, s => $"DT Hub {s.Target.UserId} (Ctrl + N)");

        Assert.Equal(2, renamed);
        Assert.Equal("DT Hub 0 (Ctrl + N)", desktop.Titles[sessions[0].WindowHandle]);
        Assert.Equal("DT Hub 10 (Ctrl + N)", desktop.Titles[sessions[1].WindowHandle]);
    }

    [Fact]
    public async Task Changer_la_taille_conserve_les_ecarts_entre_fenetres()
    {
        // Une fenêtre volontairement plus petite qu'une autre doit le rester :
        // leur donner la même taille effacerait un choix de l'utilisateur.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(100, 100, 1200, 700));
        desktop.MoveWindow(sessions[1].WindowHandle, new ScreenRect(100, 100, 600, 350));

        await service.ScaleInPlaceAsync(sessions, 0.5, CancellationToken.None);

        var big = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var small = desktop.GetWindowRect(sessions[1].WindowHandle)!.Value;

        Assert.Equal(600, big.Width);
        Assert.Equal(300, small.Width);
        Assert.Equal(2.0, (double)big.Width / small.Width, 2);
    }

    [Fact]
    public async Task Le_replacement_empile_les_autres_sur_la_fenetre_active()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        // La deuxième est placée à part, puis rendue active.
        var chosen = new ScreenRect(300, 200, 1600, 900);
        desktop.MoveWindow(sessions[1].WindowHandle, chosen);
        desktop.Focus(sessions[1].WindowHandle);

        var moved = await service.StackOnActiveAsync(sessions, CancellationToken.None);

        Assert.Equal(2, moved);
        Assert.Equal(chosen, desktop.GetWindowRect(sessions[0].WindowHandle));
        Assert.Equal(chosen, desktop.GetWindowRect(sessions[1].WindowHandle));
        Assert.Equal(chosen, desktop.GetWindowRect(sessions[2].WindowHandle));
    }

    [Fact]
    public async Task Changer_la_taille_garde_la_position_relative_dans_l_ecran()
    {
        // Une fenêtre collée en haut à gauche grandit depuis ce coin, une
        // fenêtre centrée grandit autour de son centre. Garder le coin puis
        // reprendre la fenêtre dans l'écran la poussait dès qu'elle
        // grandissait près d'un bord.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var work = FakeWindowController.PrimaryMonitor.WorkArea;

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(0, 0, 400, 300));
        desktop.MoveWindow(
            sessions[1].WindowHandle,
            new ScreenRect((work.Width - 400) / 2, (work.Height - 300) / 2, 400, 300));

        await service.ScaleInPlaceAsync(sessions, 1.5, CancellationToken.None);

        var corner = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var middle = desktop.GetWindowRect(sessions[1].WindowHandle)!.Value;

        Assert.Equal(0, corner.X);
        Assert.Equal(0, corner.Y);
        Assert.Equal(600, corner.Width);

        Assert.Equal((work.Width - 600) / 2, middle.X);
        Assert.Equal((work.Height - 450) / 2, middle.Y);
    }

    [Fact]
    public async Task Une_fenetre_au_bord_droit_reste_au_bord_droit()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var work = FakeWindowController.PrimaryMonitor.WorkArea;

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(work.Width - 400, 0, 400, 300));

        await service.ScaleInPlaceAsync(sessions, 1.5, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(work.Width - 600, rect.X);
    }

    [Fact]
    public async Task Le_retour_du_plein_ecran_rend_a_chaque_fenetre_sa_geometrie()
    {
        // Le retour partait du rectangle plein écran et empilait toutes les
        // fenêtres au même endroit.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var first = new ScreenRect(10, 20, 500, 300);
        var second = new ScreenRect(600, 400, 700, 400);

        desktop.MoveWindow(sessions[0].WindowHandle, first);
        desktop.MoveWindow(sessions[1].WindowHandle, second);

        await service.ApplySizeAsync(sessions, service.Presets.FullscreenIndex, CancellationToken.None);

        Assert.Equal(
            FakeWindowController.PrimaryMonitor.Bounds,
            desktop.GetWindowRect(sessions[0].WindowHandle));

        await service.ApplySizeAsync(sessions, 1, CancellationToken.None);

        Assert.Equal(first, desktop.GetWindowRect(sessions[0].WindowHandle));
        Assert.Equal(second, desktop.GetWindowRect(sessions[1].WindowHandle));
    }

    [Fact]
    public async Task Le_cote_a_cote_partage_l_ecran_et_met_l_active_a_droite()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        desktop.Foreground = sessions[1].WindowHandle;
        service.TrackActiveWindow(sessions);
        desktop.Foreground = 0;

        Assert.Equal(2, await service.TileAsync(sessions, CancellationToken.None));

        var work = FakeWindowController.PrimaryMonitor.WorkArea;
        var half = work.Width / 2;

        var left = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var right = desktop.GetWindowRect(sessions[1].WindowHandle)!.Value;

        Assert.Equal(work.X, left.X);
        Assert.Equal(work.X + half, right.X);
        Assert.Equal(half, left.Width);
        Assert.Equal(half, right.Width);
    }

    [Fact]
    public async Task Au_dela_de_deux_les_fenetres_se_rangent_derriere_celle_de_gauche()
    {
        // L'écran ne se partage plus utilement au-delà de deux.
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        await service.TileAsync(sessions, CancellationToken.None);

        Assert.Equal(
            desktop.GetWindowRect(sessions[1].WindowHandle),
            desktop.GetWindowRect(sessions[2].WindowHandle));
    }

    [Fact]
    public async Task Le_replacement_reprend_la_derniere_fenetre_utilisee()
    {
        // Cliquer le bouton met le configurateur au premier plan : plus aucune
        // fenêtre de jeu n'y est, et lire le premier plan à cet instant
        // ramènerait toujours à la première de la liste.
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var wanted = new ScreenRect(120, 60, 900, 520);
        desktop.MoveWindow(sessions[2].WindowHandle, wanted);

        // La troisième passe au premier plan, puis le configurateur le prend.
        desktop.Foreground = sessions[2].WindowHandle;
        service.TrackActiveWindow(sessions);
        desktop.Foreground = 0;

        await service.StackOnActiveAsync(sessions, CancellationToken.None);

        Assert.Equal(wanted, desktop.GetWindowRect(sessions[0].WindowHandle));
        Assert.Equal(wanted, desktop.GetWindowRect(sessions[1].WindowHandle));
    }

    [Fact]
    public async Task Sans_fenetre_active_le_replacement_prend_la_premiere()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var first = new ScreenRect(40, 60, 1200, 700);
        desktop.MoveWindow(sessions[0].WindowHandle, first);
        desktop.MoveWindow(sessions[1].WindowHandle, new ScreenRect(900, 500, 800, 450));
        desktop.Focus(0);

        var moved = await service.StackOnActiveAsync(sessions, CancellationToken.None);

        Assert.Equal(1, moved);
        Assert.Equal(first, desktop.GetWindowRect(sessions[1].WindowHandle));
    }

    [Fact]
    public async Task Une_geometrie_memorisee_est_rendue_telle_quelle()
    {
        // L'image étant mise à l'échelle de la fenêtre, toute taille mémorisée
        // est bonne à reprendre : rien n'est plafonné.
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        desktop.Monitors[0] = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Bounds = new ScreenRect(0, 0, 3840, 2160),
            WorkArea = new ScreenRect(0, 0, 3840, 2088),
            IsPrimary = true,
        };

        var service = new WindowManagerService(desktop, NoDelay);
        var wanted = new ScreenRect(40, 20, 2600, 1900);

        var remembered = new Dictionary<string, StoredWindowRect>(StringComparer.Ordinal)
        {
            [sessions[0].Target.Key] = StoredWindowRect.From(wanted, desktop.GetMonitors()[0]),
        };

        await service.RestoreAsync(sessions, remembered, CancellationToken.None);

        Assert.Equal(wanted, desktop.GetWindowRect(sessions[0].WindowHandle));
    }

    [Fact]
    public void L_afficheur_prend_la_forme_de_l_ecran_ou_la_fenetre_rouvre()
    {
        // Une fenêtre laissée sur un second écran de forme différente naîtrait
        // mal formée si l'afficheur suivait un écran de référence unique.
        var principal = FakeWindowController.PrimaryMonitor;
        var second = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY2",
            Bounds = new ScreenRect(1920, 0, 2560, 1600),
            WorkArea = new ScreenRect(1920, 0, 2560, 1560),
        };

        var desktop = new FakeWindowController(principal, second);
        var service = new WindowManagerService(desktop, NoDelay);

        var remembered = StoredWindowRect.From(new ScreenRect(2000, 100, 1200, 750), second);

        var bounds = service.MonitorBoundsFor(remembered);

        Assert.Equal(2560, bounds!.Value.Width);
        Assert.Equal(1600, bounds.Value.Height);
    }

    [Fact]
    public void Sans_geometrie_memorisee_l_afficheur_suit_l_ecran_principal()
    {
        var desktop = new FakeWindowController();
        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal(FakeWindowController.PrimaryMonitor.Bounds, service.MonitorBoundsFor(null));
    }

    [Fact]
    public void La_taille_transmise_a_scrcpy_est_celle_de_la_zone_client()
    {
        // C'est elle que scrcpy donne à l'afficheur, et le jeu fige la hauteur
        // de sa mise en page dessus. Compter le cadre la rendrait trop haute
        // d'une barre de titre, et l'image serait rognée d'autant.
        var desktop = new FakeWindowController { Chrome = (22, 56) };
        var service = new WindowManagerService(desktop, NoDelay);

        Assert.Equal((22, 56), service.WindowChrome());
    }

    [Fact]
    public async Task Une_fenetre_qui_deborde_apres_un_geste_est_ramenee_a_l_interieur()
    {
        // Un redimensionnement à la souris est fait par Windows, qui garde le
        // bord opposé, et par scrcpy, qui verrouille le rapport en faisant
        // grandir vers le bas : une fenêtre posée en bas de l'écran en sort.
        var (manager, sessions, desktop) = await OpenSessionsAsync(
            1,
            ScrcpyOptions.Default with { VirtualDisplayWidth = 1600, VirtualDisplayHeight = 900 });

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ResolveWindowAsync(sessions[0], CancellationToken.None);

        var work = FakeWindowController.PrimaryMonitor.WorkArea;
        var chrome = desktop.Chrome;

        // Au bon rapport, mais débordant de deux cents pixels sous l'écran.
        var height = (int)Math.Round(800d / (1600d / 900)) + chrome.Height;

        desktop.MoveWindow(
            sessions[0].WindowHandle,
            new ScreenRect(0, work.Height - height + 200, 800 + chrome.Width, height));

        service.EnforceAspect(sessions);
        service.EnforceAspect(sessions);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(work.Height - height, rect.Y);
        Assert.Equal(height, rect.Height);
    }

    [Fact]
    public async Task Une_fenetre_entierement_visible_n_est_jamais_deplacee()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(
            1,
            ScrcpyOptions.Default with { VirtualDisplayWidth = 1600, VirtualDisplayHeight = 900 });

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ResolveWindowAsync(sessions[0], CancellationToken.None);

        var chrome = desktop.Chrome;
        var height = (int)Math.Round(800d / (1600d / 900)) + chrome.Height;
        var placed = new ScreenRect(100, 80, 800 + chrome.Width, height);

        desktop.MoveWindow(sessions[0].WindowHandle, placed);

        service.EnforceAspect(sessions);

        Assert.Equal(0, service.EnforceAspect(sessions));
        Assert.Equal(placed, desktop.GetWindowRect(sessions[0].WindowHandle));
    }

    [Fact]
    public async Task Au_rapport_verrouille_la_hauteur_suit_la_forme_de_l_afficheur()
    {
        // Dans ce mode l'image est mise à l'échelle : elle ne remplit la
        // fenêtre qu'à la forme de l'afficheur, et s'en écarter laisse une
        // bande. Le plafond de hauteur, lui, ne s'applique pas.
        var (manager, sessions, desktop) = await OpenSessionsAsync(
            1,
            ScrcpyOptions.Default with
            {
                VirtualDisplayWidth = 2604,
                VirtualDisplayHeight = 1416,
            });

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ResolveWindowAsync(sessions[0], CancellationToken.None);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(0, 0, 2604, 2000));

        service.EnforceAspect(sessions);
        var corrected = service.EnforceAspect(sessions);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(1, corrected);
        Assert.Equal(2604, rect.Width);
        Assert.Equal(1416, rect.Height);
    }

    [Fact]
    public async Task L_ordre_de_la_liste_devient_l_ordre_des_fenetres()
    {
        // Les fenêtres sont remontées de la dernière à la première : chacune
        // passe au-dessus des précédentes, donc la première de la liste finit
        // au sommet, et c'est elle qu'Alt+Tab propose en premier.
        var (manager, sessions, desktop) = await OpenSessionsAsync(3);

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        var ordered = await service.ApplyOrderAsync(sessions, CancellationToken.None);

        Assert.Equal(3, ordered);
        Assert.Equal(
            [sessions[2].WindowHandle, sessions[1].WindowHandle, sessions[0].WindowHandle],
            desktop.RaiseCalls);
    }

    [Fact]
    public async Task Remettre_les_fenetres_dans_l_ordre_ne_vole_le_clavier_a_personne()
    {
        // Le rangement se fait pendant qu'on joue : donner le focus au passage
        // arracherait la fenêtre active sous les doigts de l'utilisateur.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ApplyOrderAsync(sessions, CancellationToken.None);

        Assert.Empty(desktop.FocusCalls);
    }

    [Fact]
    public async Task Le_cote_a_cote_rend_le_clavier_a_la_fenetre_de_gauche()
    {
        // Celle de droite est celle qu'on venait de quitter : la ranger pour
        // aussitôt y rester ne servirait à rien.
        var (manager, sessions, desktop) = await OpenSessionsAsync(2);

        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);

        foreach (var session in sessions)
        {
            await service.ResolveWindowAsync(session, CancellationToken.None);
        }

        desktop.Foreground = sessions[0].WindowHandle;
        service.TrackActiveWindow(sessions);

        var placed = await service.TileAsync(sessions, CancellationToken.None);

        Assert.Equal(2, placed);
        Assert.Equal(sessions[1].WindowHandle, desktop.FocusCalls[^1]);

        var gauche = desktop.GetWindowRect(sessions[1].WindowHandle)!.Value;
        var droite = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.True(gauche.X < droite.X);
    }

    [Fact]
    public async Task L_arret_demande_la_fermeture_avant_de_tuer()
    {
        // Un client tué net sur une liaison Wi-Fi laisse son serveur en vie sur
        // le téléphone, avec l'afficheur virtuel qu'il a créé.
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);

        var service = new WindowManagerService(desktop, NoDelay);

        await service.ResolveWindowAsync(sessions[0], CancellationToken.None);

        manager.RequestClose = session => service.RequestClose(session);

        await manager.StopAsync(sessions[0].Id, CancellationToken.None);
        await manager.DisposeAsync();

        Assert.Equal([sessions[0].WindowHandle], desktop.CloseRequests);
    }
}
