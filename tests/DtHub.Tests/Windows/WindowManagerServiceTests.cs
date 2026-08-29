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
            1, ScrcpyOptions.Default with { FlexDisplay = false });

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
    public async Task En_mode_flexible_la_fenetre_occupe_toute_la_part_demandee()
    {
        // L'afficheur virtuel épouse la fenêtre, donc aucun rapport ne
        // contraint celle-ci. Ce mode n'est plus celui par défaut : le jeu ne
        // se remet pas toujours en page quand son afficheur change de forme
        // sous lui, et laisse alors une bande noire.
        var (manager, sessions, desktop) = await OpenSessionsAsync(
            1, ScrcpyOptions.Default with { FlexDisplay = true });
        await using var _ = manager;

        desktop.Chrome = (16, 48);

        var service = new WindowManagerService(desktop, NoDelay);
        await service.ApplySizeAsync([], 1, CancellationToken.None);
        await service.ArrangeAsync(sessions, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;
        var work = FakeWindowController.PrimaryMonitor.WorkArea;
        var fraction = WindowSizePresets.Default.PercentageAt(1) / 100.0;

        Assert.Equal((int)Math.Round(work.Width * fraction), rect.Width);
        Assert.Equal((int)Math.Round(work.Height * fraction), rect.Height);
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
    public async Task Changer_la_taille_ne_deplace_pas_les_fenetres()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(400, 250, 1000, 600));

        await service.ScaleInPlaceAsync(sessions, 0.8, CancellationToken.None);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(400, rect.X);
        Assert.Equal(250, rect.Y);
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
    public async Task Elargir_une_fenetre_ne_la_contraint_jamais()
    {
        // Le jeu se remet en page en largeur sans faute : rien à corriger.
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var wide = new ScreenRect(0, 0, 2600, sessions[0].MaxClientHeight);

        desktop.MoveWindow(sessions[0].WindowHandle, wide);

        service.EnforceAspect(sessions);
        service.EnforceAspect(sessions);

        Assert.Equal(wide, desktop.GetWindowRect(sessions[0].WindowHandle));
    }

    [Fact]
    public async Task La_hauteur_ne_depasse_pas_celle_de_la_naissance_du_jeu()
    {
        // Au-delà, le jeu laisserait une bande noire de la hauteur ajoutée.
        // La fenêtre s'arrête là plutôt que de recharger le jeu.
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var ceiling = sessions[0].MaxClientHeight;

        desktop.MoveWindow(sessions[0].WindowHandle, new ScreenRect(0, 0, 2600, ceiling + 400));

        // Le premier passage constate, le second corrige : on ne lutte pas
        // contre un geste en cours.
        service.EnforceAspect(sessions);
        var corrected = service.EnforceAspect(sessions);

        var rect = desktop.GetWindowRect(sessions[0].WindowHandle)!.Value;

        Assert.Equal(1, corrected);
        Assert.Equal(2600, rect.Width);
        Assert.Equal(ceiling, rect.Height);
    }

    [Fact]
    public async Task Une_fenetre_plus_basse_que_le_plafond_est_laissee_telle_quelle()
    {
        var (manager, sessions, desktop) = await OpenSessionsAsync(1);
        await using var _ = manager;

        var service = new WindowManagerService(desktop, NoDelay);
        await service.RestoreAsync(sessions, new Dictionary<string, StoredWindowRect>(), CancellationToken.None);

        var small = new ScreenRect(10, 10, 900, sessions[0].MaxClientHeight - 200);

        desktop.MoveWindow(sessions[0].WindowHandle, small);

        service.EnforceAspect(sessions);

        Assert.Equal(0, service.EnforceAspect(sessions));
        Assert.Equal(small, desktop.GetWindowRect(sessions[0].WindowHandle));
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
                FlexDisplay = false,
                VirtualDisplayWidth = 2604,
                VirtualDisplayHeight = 1416,
            });

        await using var _ = manager;

        Assert.Equal(0, sessions[0].MaxClientHeight);

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
}
