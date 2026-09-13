using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace DtHub.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dthub-settings-" + Guid.NewGuid().ToString("N"));

    private readonly JsonDocumentStore<AppSettingsDocument> _store;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _store = new JsonDocumentStore<AppSettingsDocument>(
            Path.Combine(_directory, "settings.json"), NullLogger.Instance);

        _service = new SettingsService(_store);
    }

    // Named sessions

    private static StoredInstance Compte(int user) => new()
    {
        DeviceId = "phone",
        UserId = user,
        PackageName = "com.ankama.dofustouch",
        UserName = $"Profil {user}",
    };

    [Fact]
    public async Task Une_session_enregistree_se_relit()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s => s.Instances.Add(xspace));

        Assert.True(await _service.SaveLaunchProfileAsync("Solo", [xspace.Key]));

        var profils = await _service.GetLaunchProfilesAsync();

        Assert.Single(profils);
        Assert.Equal("Solo", profils[0].Name);
        Assert.Equal([xspace.Key], profils[0].InstanceKeys);
    }

    [Fact]
    public async Task Une_session_sans_nom_est_refusee()
    {
        Assert.False(await _service.SaveLaunchProfileAsync("   ", ["x"]));
        Assert.Empty(await _service.GetLaunchProfilesAsync());
    }

    [Fact]
    public async Task Enregistrer_deux_fois_le_meme_nom_remplace()
    {
        // The natural way to fix a profile is to save it again. Refusing that
        // would force deleting it first.
        await _service.SaveLaunchProfileAsync("Duo", ["a"]);
        await _service.SaveLaunchProfileAsync("duo", ["b", "c"]);

        var profils = await _service.GetLaunchProfilesAsync();

        Assert.Single(profils);
        Assert.Equal(["b", "c"], profils[0].InstanceKeys);
    }

    [Fact]
    public async Task Appliquer_une_session_coche_les_siens_et_decoche_les_autres()
    {
        var principal = Compte(0);
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(principal);
            s.Instances.Add(xspace);
            s.Instances.ForEach(i => i.IsEnabled = true);
        });

        await _service.SaveLaunchProfileAsync("Solo", [xspace.Key]);

        Assert.Equal([xspace.Key], await _service.ApplyLaunchProfileAsync("Solo"));

        var document = await _service.GetAsync();

        Assert.False(document.Instances.Find(i => i.Key == principal.Key)!.IsEnabled);
        Assert.True(document.Instances.Find(i => i.Key == xspace.Key)!.IsEnabled);
    }

    [Fact]
    public async Task Un_profil_liste_ses_comptes_dans_l_ordre_du_rangement()
    {
        var premier = Compte(0);
        var second = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(premier);
            s.Instances.Add(second);
            premier.Order = 0;
            second.Order = 1;
        });

        // The caller passes them out of order: the rank is what counts, since
        // it is what the list shows and what the tabs follow.
        await _service.SaveLaunchProfileAsync("Duo", [second.Key, premier.Key]);

        Assert.Equal(
            [premier.Key, second.Key],
            (await _service.GetLaunchProfilesAsync()).Single().InstanceKeys);
    }

    [Fact]
    public async Task Ouvrir_un_profil_ne_derange_pas_l_ordre_des_comptes()
    {
        var premier = Compte(0);
        var second = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(premier);
            s.Instances.Add(second);
            premier.Order = 0;
            second.Order = 1;
        });

        await _service.SaveLaunchProfileAsync("Duo", [premier.Key, second.Key]);

        // Reordering with the mouse happens after the profile is saved.
        await _service.MoveInstanceAsync(second.Key, premier.Key, above: true);

        await _service.ApplyLaunchProfileAsync("Duo");

        // The profile must not undo it: order is a general setting, and since
        // the startup profile opens on its own, it would have undone it on
        // every launch.
        var rangs = await _service.GetInstanceRanksAsync();

        Assert.Equal(0, rangs[second.Key]);
        Assert.Equal(1, rangs[premier.Key]);
    }

    [Fact]
    public async Task Un_profil_retient_le_son_et_le_presse_papiers()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            s.AudioEnabled = true;
            s.ClipboardSyncEnabled = false;
        });

        await _service.SaveLaunchProfileAsync("Solo", [xspace.Key]);

        // Settings change after the save: the profile must restore the state
        // at the moment of saving, not the last one known.
        await _service.UpdateAsync(s =>
        {
            s.AudioEnabled = false;
            s.ClipboardSyncEnabled = true;
        });

        await _service.ApplyLaunchProfileAsync("Solo");

        var document = await _service.GetAsync();

        Assert.True(document.AudioEnabled);
        Assert.False(document.ClipboardSyncEnabled);
    }

    [Fact]
    public async Task Un_profil_en_onglets_retient_la_place_du_cadre()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            xspace.IsTabbed = true;
            s.WindowPlacements[SettingsService.TabsPlacementKey] =
                new WindowPlacement { Left = 40, Top = 60, Right = 1240, Bottom = 735 };
        });

        await _service.SaveLaunchProfileAsync("Duo", [xspace.Key]);

        await _service.UpdateAsync(s =>
            s.WindowPlacements[SettingsService.TabsPlacementKey] =
                new WindowPlacement { Left = 0, Top = 0, Right = 800, Bottom = 500 });

        await _service.ApplyLaunchProfileAsync("Duo");

        var cadre = (await _service.GetAsync())
            .WindowPlacements[SettingsService.TabsPlacementKey];

        Assert.Equal(40, cadre.Left);
        Assert.Equal(1240, cadre.Right);
    }

    [Fact]
    public async Task Un_profil_sans_onglets_laisse_le_cadre_ou_il_est()
    {
        // Remembering the frame's position on a profile that hosts nothing
        // would move the frame of another profile when opening it.
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            s.WindowPlacements[SettingsService.TabsPlacementKey] =
                new WindowPlacement { Left = 40, Top = 60, Right = 1240, Bottom = 735 };
        });

        await _service.SaveLaunchProfileAsync("Libre", [xspace.Key]);

        Assert.Null(
            (await _service.GetLaunchProfilesAsync())
                .Single(p => p.Name == "Libre").TabsWindow);

        await _service.UpdateAsync(s =>
            s.WindowPlacements[SettingsService.TabsPlacementKey] =
                new WindowPlacement { Left = 5, Top = 5, Right = 805, Bottom = 505 });

        await _service.ApplyLaunchProfileAsync("Libre");

        Assert.Equal(
            5,
            (await _service.GetAsync()).WindowPlacements[SettingsService.TabsPlacementKey].Left);
    }

    [Fact]
    public async Task Une_session_inconnue_ne_touche_a_rien()
    {
        var principal = Compte(0);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(principal);
            principal.IsEnabled = true;
        });

        Assert.Empty(await _service.ApplyLaunchProfileAsync("Fantôme"));

        Assert.True((await _service.GetAsync()).Instances[0].IsEnabled);
    }

    [Fact]
    public async Task Supprimer_la_session_du_demarrage_remet_a_aucun()
    {
        // Leaving a default that points to nothing would give a startup that
        // does not open what is expected, with nothing to explain why.
        await _service.SaveLaunchProfileAsync("Solo", ["a"]);
        await _service.SetDefaultLaunchProfileAsync("Solo");

        Assert.Equal("Solo", await _service.GetDefaultLaunchProfileAsync());

        await _service.DeleteLaunchProfileAsync("Solo");

        Assert.Empty(await _service.GetLaunchProfilesAsync());
        Assert.Null(await _service.GetDefaultLaunchProfileAsync());
    }

    [Fact]
    public async Task Supprimer_une_autre_session_laisse_le_demarrage_en_place()
    {
        await _service.SaveLaunchProfileAsync("Solo", ["a"]);
        await _service.SaveLaunchProfileAsync("Duo", ["a", "b"]);
        await _service.SetDefaultLaunchProfileAsync("Solo");

        await _service.DeleteLaunchProfileAsync("Duo");

        Assert.Equal("Solo", await _service.GetDefaultLaunchProfileAsync());
    }

    [Fact]
    public async Task Le_demarrage_se_remet_a_aucun()
    {
        await _service.SaveLaunchProfileAsync("Solo", ["a"]);
        await _service.SetDefaultLaunchProfileAsync("Solo");

        await _service.SetDefaultLaunchProfileAsync(null);

        Assert.Null(await _service.GetDefaultLaunchProfileAsync());
    }

    [Fact]
    public async Task Un_demarrage_qui_designe_une_session_absente_vaut_aucun()
    {
        // A file edited by hand can name anything.
        await _service.UpdateAsync(s => s.DefaultLaunchProfile = "Disparue");

        await _service.SetDefaultLaunchProfileAsync("Disparue");

        Assert.Null(await _service.GetDefaultLaunchProfileAsync());
    }

    [Fact]
    public async Task Un_profil_retient_les_positions_et_les_reglages()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            xspace.Window = new StoredWindowRect { X = 40, Y = 50, Width = 1280, Height = 720 };
            s.Quality = StreamQuality.Maximum;
            s.GameZoom = GameZoom.Close;
            s.GameAnchor = WindowAnchor.BottomRight;
            s.CustomSizePercent = 85;
        });

        await _service.SaveLaunchProfileAsync("Solo donjon", [xspace.Key]);

        var profil = (await _service.GetLaunchProfilesAsync())[0];

        Assert.Equal(1280, profil.Windows[xspace.Key].Width);
        Assert.Equal(40, profil.Windows[xspace.Key].X);
        Assert.Equal(StreamQuality.Maximum, profil.Quality);
        Assert.Equal(GameZoom.Close, profil.GameZoom);
        Assert.Equal(WindowAnchor.BottomRight, profil.GameAnchor);
        Assert.Equal(85, profil.CustomSizePercent);
    }

    [Fact]
    public async Task Ouvrir_un_profil_restitue_positions_et_reglages()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            xspace.Window = new StoredWindowRect { X = 40, Y = 50, Width = 1280, Height = 720 };
            s.Quality = StreamQuality.Maximum;
            s.GameZoom = GameZoom.Close;
        });

        await _service.SaveLaunchProfileAsync("Solo donjon", [xspace.Key]);

        // Everything is disturbed, as another game session would do.
        await _service.UpdateAsync(s =>
        {
            s.Instances[0].Window = new StoredWindowRect { X = 900, Y = 900, Width = 640, Height = 360 };
            s.Quality = StreamQuality.Low;
            s.GameZoom = GameZoom.Widest;
        });

        await _service.ApplyLaunchProfileAsync("Solo donjon");

        var document = await _service.GetAsync();

        Assert.Equal(1280, document.Instances[0].Window!.Width);
        Assert.Equal(40, document.Instances[0].Window!.X);
        Assert.Equal(StreamQuality.Maximum, document.Quality);
        Assert.Equal(GameZoom.Close, document.GameZoom);
    }

    [Fact]
    public async Task Un_profil_qui_ne_place_pas_un_compte_lui_laisse_sa_position()
    {
        // This is the case of a profile saved before profiles carried
        // positions: it must open its accounts, not send them all back to the
        // anchor.
        var xspace = Compte(999);
        var place = new StoredWindowRect { X = 7, Y = 8, Width = 640, Height = 360 };

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            xspace.Window = place;
            s.LaunchProfiles.Add(new StoredLaunchProfile
            {
                Name = "Ancien",
                InstanceKeys = [xspace.Key],
            });
        });

        await _service.ApplyLaunchProfileAsync("Ancien");

        var document = await _service.GetAsync();

        Assert.True(document.Instances[0].IsEnabled);
        Assert.Equal(7, document.Instances[0].Window!.X);
    }

    [Fact]
    public async Task Le_resume_dit_la_qualite_quand_elle_sort_de_l_ordinaire()
    {
        var xspace = Compte(999);

        await _service.UpdateAsync(s =>
        {
            s.Instances.Add(xspace);
            s.Quality = StreamQuality.Maximum;
        });

        await _service.SaveLaunchProfileAsync("Solo", [xspace.Key]);

        var document = await _service.GetAsync();
        var resume = LaunchProfiles.Describe(document.LaunchProfiles[0], document.Instances);

        Assert.Contains("qualité haute", resume, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _service.Dispose();
        _store.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DofusInstance Instance(int userId, string deviceId = "MATERIEL123") => new()
    {
        DeviceId = deviceId,
        DeviceName = "Xiaomi 13T",
        UserId = userId,
        UserName = userId == 0 ? "Alice Martin" : "XSpace",
        PackageName = DofusPackages.DofusTouch,
        LaunchComponent = "com.ankama.dofustouch/.MainActivity",
        IsDeviceConnected = true,
    };

    [Fact]
    public async Task Les_reglages_font_l_aller_retour_sans_rien_perdre()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);
        await _service.SetInstanceQualityAsync(Instance(999).Key, StreamQuality.Low, CancellationToken.None);
        await _service.SetQualityAsync(StreamQuality.Maximum, CancellationToken.None);

        var emporte = await _service.ExportAsync(CancellationToken.None);

        // Everything is wiped, as on a fresh machine.
        await _service.UpdateAsync(s => { s.Instances.Clear(); s.Quality = StreamQuality.Medium; }, CancellationToken.None);

        Assert.Equal(BackupVerdict.Usable, await _service.ImportAsync(emporte, CancellationToken.None));

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(2, settings.Instances.Count);
        Assert.Equal(StreamQuality.Maximum, settings.Quality);

        var mule = settings.Instances.Single(i => i.UserId == 999);

        Assert.Equal(StreamQuality.Low, mule.Quality);
    }

    [Fact]
    public async Task Un_fichier_qu_on_ne_sait_pas_lire_ne_touche_a_rien()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        foreach (var (contenu, attendu) in new (string, BackupVerdict)[]
                 {
                     ("pas du json", BackupVerdict.Unreadable),
                     ("""{ "quelque": "chose" }""", BackupVerdict.Foreign),
                     ($$"""{ "schemaVersion": {{AppSettingsDocument.CurrentSchemaVersion + 5}}, "instances": [] }""",
                         BackupVerdict.TooNew),
                 })
        {
            Assert.Equal(attendu, await _service.ImportAsync(contenu, CancellationToken.None));
        }

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Single(settings.Instances);
    }

    [Fact]
    public async Task Le_temps_de_jeu_s_accumule_et_se_relit()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.AddPlaytimeAsync(mule, 600, CancellationToken.None);
        await _service.AddPlaytimeAsync(mule, 900, CancellationToken.None);

        var apres = await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        Assert.Equal(1500, Assert.Single(apres).PlayedThisWeek);
    }

    [Fact]
    public async Task Une_session_d_une_poignee_de_secondes_ne_compte_pas()
    {
        // Opening and immediately closing again is not play time, and writing
        // it would cause a needless file write.
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        await _service.AddPlaytimeAsync(Instance(999).Key, 5, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Empty(Assert.Single(settings.Instances).Playtime);
    }

    [Fact]
    public async Task Un_compte_sans_palier_prend_le_commun()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);
        await _service.SetQualityAsync(StreamQuality.Maximum, CancellationToken.None);

        var qualities = await _service.GetInstanceQualitiesAsync(CancellationToken.None);

        Assert.Equal(2, qualities.Count);
        Assert.All(qualities.Values, q => Assert.Equal(QualityProfile.For(StreamQuality.Maximum), q));
    }

    [Fact]
    public async Task Le_palier_d_un_compte_l_emporte_et_se_relit()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);
        await _service.SetQualityAsync(StreamQuality.Maximum, CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceQualityAsync(mule, StreamQuality.Low, CancellationToken.None);
        _service.Invalidate();

        var qualities = await _service.GetInstanceQualitiesAsync(CancellationToken.None);

        Assert.Equal(QualityProfile.For(StreamQuality.Low), qualities[mule]);
        Assert.Equal(QualityProfile.For(StreamQuality.Maximum), qualities[Instance(0).Key]);
    }

    [Fact]
    public async Task Rendre_un_compte_au_commun_efface_son_palier()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceQualityAsync(mule, StreamQuality.Low, CancellationToken.None);
        await _service.SetInstanceQualityAsync(mule, null, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Null(Assert.Single(settings.Instances).Quality);
    }

    [Fact]
    public async Task Le_palier_d_un_compte_survit_a_un_rebalayage()
    {
        // Rediscovery only copies the device name, the profile name and the
        // component: everything else belongs to the user and must survive a
        // rescan without a scratch.
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceQualityAsync(mule, StreamQuality.Low, CancellationToken.None);

        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(StreamQuality.Low, Assert.Single(settings.Instances).Quality);
    }

    [Fact]
    public async Task La_distance_d_un_compte_l_emporte_et_se_relit()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);
        await _service.SetZoomAsync(GameZoom.Close, CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceZoomAsync(mule, GameZoom.Widest, CancellationToken.None);
        _service.Invalidate();

        var zooms = await _service.GetInstanceZoomsAsync(CancellationToken.None);

        Assert.Equal(GameZoom.Widest, zooms[mule]);
        Assert.Equal(GameZoom.Close, zooms[Instance(0).Key]);
    }

    [Fact]
    public async Task Rendre_un_compte_au_commun_efface_sa_distance()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceZoomAsync(mule, GameZoom.Widest, CancellationToken.None);
        await _service.SetInstanceZoomAsync(mule, null, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Null(Assert.Single(settings.Instances).GameZoom);
    }

    [Fact]
    public async Task La_distance_d_un_compte_survit_a_un_rebalayage()
    {
        // Same requirement as for the tier: rediscovery only copies what the
        // phone reports, and what the user chose must survive it without a
        // scratch.
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceZoomAsync(mule, GameZoom.Widest, CancellationToken.None);

        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(GameZoom.Widest, Assert.Single(settings.Instances).GameZoom);
    }

    [Fact]
    public async Task Un_compte_sans_distance_propre_suit_le_commun()
    {
        await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);
        await _service.SetZoomAsync(GameZoom.Wide, CancellationToken.None);

        var zooms = await _service.GetInstanceZoomsAsync(CancellationToken.None);

        Assert.Equal(GameZoom.Wide, zooms[Instance(0).Key]);
    }

    [Fact]
    public async Task Poser_deux_fois_le_meme_palier_n_ecrit_pas_le_fichier()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var mule = Instance(999).Key;

        await _service.SetInstanceQualityAsync(mule, StreamQuality.Low, CancellationToken.None);

        var avant = File.GetLastWriteTimeUtc(_store.FilePath);

        await Task.Delay(20, CancellationToken.None);
        await _service.SetInstanceQualityAsync(mule, StreamQuality.Low, CancellationToken.None);

        Assert.Equal(avant, File.GetLastWriteTimeUtc(_store.FilePath));
    }

    [Fact]
    public async Task Un_palier_pose_sur_un_compte_inconnu_ne_fait_rien()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        await _service.SetInstanceQualityAsync(
            "personne|1|rien", StreamQuality.Low, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Null(Assert.Single(settings.Instances).Quality);
    }

    [Fact]
    public async Task Les_valeurs_par_defaut_correspondent_a_ce_qui_est_annonce()
    {
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Empty(settings.Instances);
        Assert.Equal(WindowAnchor.MiddleLeft, settings.GameAnchor);
        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
        Assert.Equal(1, settings.SizeIndex);
        Assert.Equal(StreamQuality.Medium, settings.Quality);
        Assert.False(settings.AudioEnabled);
        Assert.True(settings.ClipboardSyncEnabled);
        Assert.Equal("com.ankama.dofustouch", settings.PackageName);
    }

    [Fact]
    public async Task L_ecran_virtuel_par_defaut_est_en_paysage()
    {
        // The game displays in landscape: a portrait screen would shrink it to
        // a band in the middle of the window.
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.True(settings.VirtualDisplayWidth > settings.VirtualDisplayHeight);
        Assert.Equal(1920, settings.VirtualDisplayWidth);
        Assert.Equal(1080, settings.VirtualDisplayHeight);
    }

    [Fact]
    public async Task Un_fichier_d_une_version_anterieure_bascule_en_paysage()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _store.FilePath,
            """{ "schemaVersion": 2, "virtualDisplayWidth": 1080, "virtualDisplayHeight": 1920, "virtualDisplayDpi": 320 }""",
            CancellationToken.None);

        _service.Invalidate();
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(1920, settings.VirtualDisplayWidth);
        Assert.Equal(1080, settings.VirtualDisplayHeight);
        Assert.Equal(AppSettingsDocument.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public async Task Une_definition_choisie_par_l_utilisateur_n_est_pas_ecrasee()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _store.FilePath,
            """{ "schemaVersion": 2, "virtualDisplayWidth": 1440, "virtualDisplayHeight": 2560, "virtualDisplayDpi": 400 }""",
            CancellationToken.None);

        _service.Invalidate();
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(1440, settings.VirtualDisplayWidth);
        Assert.Equal(2560, settings.VirtualDisplayHeight);
    }

    [Fact]
    public async Task Une_modification_est_ecrite_immediatement()
    {
        await _service.UpdateAsync(s => s.SizeIndex = 3, CancellationToken.None);
        _service.Invalidate();

        Assert.Equal(3, (await _service.GetAsync(CancellationToken.None)).SizeIndex);
    }

    [Fact]
    public async Task Chaque_ecriture_est_signalee()
    {
        var notifications = 0;
        _service.Changed += (_, _) => notifications++;

        await _service.UpdateAsync(s => s.PackageName = "com.exemple", CancellationToken.None);
        await _service.UpdateAsync(s => s.AudioEnabled = true, CancellationToken.None);

        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Les_instances_decouvertes_sont_memorisees_decochees()
    {
        var merged = await _service.MergeInstancesAsync(
            [Instance(0), Instance(999)], CancellationToken.None);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, i => Assert.False(i.IsEnabled));
        Assert.All(merged, i => Assert.True(i.IsDeviceConnected));
    }

    [Fact]
    public async Task Une_instance_cochee_le_reste_apres_redecouverte()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var key = Instance(999).Key;
        await _service.SetInstanceEnabledAsync(key, true, CancellationToken.None);

        var merged = await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        Assert.True(merged.Single(i => i.Key == key).IsEnabled);
    }

    [Fact]
    public async Task Le_nom_choisi_survit_a_une_redecouverte()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var key = Instance(999).Key;
        await _service.RenameInstanceAsync(key, "Enutrof", CancellationToken.None);

        var merged = await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        Assert.Equal("Enutrof", merged.Single().CustomName);
        Assert.Equal("Enutrof", merged.Single().DisplayName);
    }

    [Fact]
    public async Task Un_nom_vide_retablit_le_nom_du_profil_android()
    {
        await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        var key = Instance(999).Key;
        await _service.RenameInstanceAsync(key, "Enutrof", CancellationToken.None);
        await _service.RenameInstanceAsync(key, "   ", CancellationToken.None);

        var merged = await _service.MergeInstancesAsync([Instance(999)], CancellationToken.None);

        Assert.Null(merged.Single().CustomName);
        Assert.Equal("XSpace", merged.Single().DisplayName);
    }

    [Fact]
    public async Task Une_instance_dont_le_telephone_est_absent_reste_listee_hors_ligne()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        // On the next scan, the phone is no longer there.
        var merged = await _service.MergeInstancesAsync([], CancellationToken.None);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, i => Assert.False(i.IsDeviceConnected));
    }

    [Fact]
    public async Task L_ordre_de_decouverte_est_conserve()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);
        var merged = await _service.MergeInstancesAsync([Instance(999), Instance(0)], CancellationToken.None);

        Assert.Equal([0, 999], merged.Select(i => i.UserId));
    }

    [Fact]
    public async Task Oublier_un_telephone_retire_toutes_ses_instances()
    {
        await _service.MergeInstancesAsync(
            [Instance(0), Instance(999), Instance(0, "AUTRE")], CancellationToken.None);

        await _service.ForgetDeviceAsync("MATERIEL123", CancellationToken.None);

        var merged = await _service.MergeInstancesAsync([], CancellationToken.None);

        Assert.Equal("AUTRE", Assert.Single(merged).DeviceId);
    }

    [Fact]
    public async Task Les_reglages_de_mirroring_derivent_des_preferences()
    {
        await _service.UpdateAsync(s =>
        {
            s.AudioEnabled = true;
            s.ClipboardSyncEnabled = false;
        }, cancellationToken: CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.True(options.AudioEnabled);
        Assert.False(options.ClipboardSyncEnabled);
    }

    [Fact]
    public async Task Le_clavier_passe_par_l_api_android_par_defaut()
    {
        // The simulated physical mode reads keys according to the layout set
        // in Android: forcing it would make an AZERTY keyboard type as QWERTY,
        // for someone who does not even have the flaw it is meant to fix.
        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(ScrcpyKeyboardMode.Sdk, options.KeyboardMode);
    }

    [Fact]
    public async Task Le_clavier_physique_simule_se_demande_et_arrive_jusqu_a_scrcpy()
    {
        // The mode had existed in the code all along, and nothing could reach
        // it. This is the fix for the keyboard that types nothing.
        await _service.SetSimulatedPhysicalKeyboardAsync(true, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(ScrcpyKeyboardMode.Uhid, options.KeyboardMode);

        var arguments = ScrcpyCommandBuilder.BuildMirrorArguments("SERIE", "titre", options);

        Assert.Contains("--keyboard=uhid", arguments, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Les_images_par_seconde_et_le_debit_viennent_de_la_qualite()
    {
        // Two sources for the same setting would eventually have diverged:
        // quality is the only one.
        await _service.SetQualityAsync(StreamQuality.Maximum, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        // Sixty frames and not one hundred twenty: the game renders
        // thirty-eight, as measured, and the one hundred twenty only served to
        // halve the bits given to each frame that actually exists.
        Assert.Equal(60, options.MaxFps);

        // Bitrate follows the resolution: 0.11 bit per pixel per frame at
        // 1920x1080 and 60 frames per second.
        Assert.Equal(13686, options.VideoBitrateKbps);
    }

    [Fact]
    public async Task Sans_raccourci_enregistre_les_valeurs_par_defaut_sont_rendues()
    {
        var hotkeys = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + P", hotkeys.For(HotkeyAction.ToggleConfigurator)!.DisplayText);
    }

    [Fact]
    public async Task Les_raccourcis_modifies_sont_relus_a_l_identique()
    {
        var modified = HotkeySet.Default.With(HotkeyAction.Rearrange, VirtualKeys.F9, HotkeyModifiers.Control);

        await _service.SaveHotkeysAsync(modified, CancellationToken.None);
        _service.Invalidate();

        var reloaded = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + F9", reloaded.For(HotkeyAction.Rearrange)!.DisplayText);
        Assert.Equal("Ctrl + Tab", reloaded.For(HotkeyAction.NextInstance)!.DisplayText);
    }

    [Fact]
    public async Task Une_action_inconnue_dans_le_fichier_est_ignoree_sans_tout_perdre()
    {
        await _service.UpdateAsync(s => s.Hotkeys =
        [
            new StoredHotkey { Action = "ActionDUneAutreVersion", VirtualKey = 0x41, Modifiers = HotkeyModifiers.Control },
            new StoredHotkey { Action = "Rearrange", VirtualKey = VirtualKeys.F9, Modifiers = HotkeyModifiers.Control },
        ], CancellationToken.None);

        var hotkeys = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + F9", hotkeys.For(HotkeyAction.Rearrange)!.DisplayText);
        Assert.Equal("Ctrl + Tab", hotkeys.For(HotkeyAction.NextInstance)!.DisplayText);
    }

    [Fact]
    public async Task Un_fichier_de_reglages_corrompu_donne_une_configuration_valide()
    {
        await _service.UpdateAsync(s => s.PackageName = "com.exemple", CancellationToken.None);
        await File.WriteAllTextAsync(_store.FilePath, "{ pas du json", CancellationToken.None);

        _service.Invalidate();

        Assert.Equal("com.ankama.dofustouch", (await _service.GetAsync(CancellationToken.None)).PackageName);
    }

    [Fact]
    public async Task Un_fichier_partiel_complete_les_valeurs_manquantes()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _store.FilePath, """{ "virtualDisplayDpi": 320 }""", CancellationToken.None);

        _service.Invalidate();
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(320, settings.VirtualDisplayDpi);
        Assert.Equal(StreamQuality.Medium, settings.Quality);
        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
    }

    [Fact]
    public async Task Les_tailles_sont_proportionnelles_a_l_ecran_et_assainies()
    {
        await _service.UpdateAsync(s => s.SizePercentages = [95, 50, 50, 500], CancellationToken.None);

        var presets = await _service.GetSizePresetsAsync(CancellationToken.None);

        Assert.Equal([50, 95, 100], presets.Percentages);
        Assert.True(presets.IsFullscreen(presets.FullscreenIndex));
    }

    [Fact]
    public async Task Les_enumerations_sont_ecrites_en_clair_dans_le_fichier()
    {
        await _service.UpdateAsync(s => s.GameAnchor = WindowAnchor.BottomRight, CancellationToken.None);

        var json = await File.ReadAllTextAsync(_store.FilePath, CancellationToken.None);

        Assert.Contains("\"gameAnchor\": \"BottomRight\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task L_ordre_choisi_survit_a_une_redecouverte()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        Assert.True(await _service.MoveInstanceAsync(
            "MATERIEL123|0|" + DofusPackages.DofusTouch,
            "MATERIEL123|999|" + DofusPackages.DofusTouch,
            above: false,
            CancellationToken.None));

        var merged = await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        Assert.Equal([999, 0], [.. merged.Select(i => i.UserId)]);
    }

    [Fact]
    public async Task Une_instance_nouvellement_decouverte_se_place_en_fin_de_son_appareil()
    {
        await _service.MergeInstancesAsync(
            [Instance(0, "PHONE-A"), Instance(0, "PHONE-B")], CancellationToken.None);

        var merged = await _service.MergeInstancesAsync(
            [Instance(0, "PHONE-A"), Instance(999, "PHONE-A"), Instance(0, "PHONE-B")],
            CancellationToken.None);

        Assert.Equal(
            ["PHONE-A/0", "PHONE-A/999", "PHONE-B/0"],
            [.. merged.Select(i => $"{i.DeviceId}/{i.UserId}")]);
    }

    [Fact]
    public async Task Oublier_un_appareil_resserre_les_rangs_restants()
    {
        await _service.MergeInstancesAsync(
            [Instance(0, "PHONE-A"), Instance(0, "PHONE-B")], CancellationToken.None);

        await _service.ForgetDeviceAsync("PHONE-A", CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(["PHONE-B"], [.. settings.Instances.Select(i => i.DeviceId)]);
        Assert.Equal([0], [.. settings.Instances.Select(i => i.Order)]);
    }

    [Fact]
    public async Task Une_instance_deplacee_entre_deux_appareils_garde_sa_place()
    {
        // Order is global: it must survive a rediscovery, which re-merges
        // every instance.
        await _service.MergeInstancesAsync(
            [Instance(0, "PHONE-A"), Instance(999, "PHONE-A"), Instance(0, "PHONE-B")],
            CancellationToken.None);

        await _service.MoveInstanceAsync(
            "PHONE-B|0|" + DofusPackages.DofusTouch,
            "PHONE-A|999|" + DofusPackages.DofusTouch,
            above: true,
            CancellationToken.None);

        var merged = await _service.MergeInstancesAsync(
            [Instance(0, "PHONE-A"), Instance(999, "PHONE-A"), Instance(0, "PHONE-B")],
            CancellationToken.None);

        Assert.Equal(
            ["PHONE-A/0", "PHONE-B/0", "PHONE-A/999"],
            [.. merged.Select(i => $"{i.DeviceId}/{i.UserId}")]);
    }

    [Fact]
    public async Task La_qualite_basse_allege_l_image()
    {
        await _service.SetQualityAsync(StreamQuality.Low, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(30, options.MaxFps);

        // Height is capped at 720 by the tier, and bitrate follows.
        Assert.Equal(3318, options.VideoBitrateKbps);
    }

    [Fact]
    public async Task Une_fenetre_mise_de_cote_sort_des_placements_automatiques()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var principal = "MATERIEL123|0|" + DofusPackages.DofusTouch;

        await _service.SetInstanceManagedAsync(principal, managed: false, CancellationToken.None);

        var unmanaged = await _service.GetUnmanagedKeysAsync(CancellationToken.None);

        Assert.Equal([principal], unmanaged);
    }

    [Fact]
    public async Task Une_fenetre_suit_les_placements_par_defaut()
    {
        await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        Assert.Empty(await _service.GetUnmanagedKeysAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Une_geometrie_enregistree_est_relue_a_l_identique()
    {
        await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        var key = "MATERIEL123|0|" + DofusPackages.DofusTouch;
        var monitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Bounds = new ScreenRect(0, 0, 3840, 2160),
            WorkArea = new ScreenRect(0, 0, 3840, 2088),
            IsPrimary = true,
        };

        await _service.SaveWindowRectsAsync(
            new Dictionary<string, StoredWindowRect>
            {
                [key] = StoredWindowRect.From(new ScreenRect(300, 200, 900, 900), monitor),
            },
            CancellationToken.None);

        var rects = await _service.GetWindowRectsAsync(CancellationToken.None);

        Assert.Equal(new ScreenRect(300, 200, 900, 900), rects[key].Bounds);
        Assert.Equal(new ScreenRect(0, 0, 3840, 2160), rects[key].Monitor);
    }

    [Fact]
    public async Task Enregistrer_une_geometrie_n_efface_pas_celle_des_autres_instances()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var monitor = new MonitorInfo
        {
            DeviceName = @"\\.\DISPLAY1",
            Bounds = new ScreenRect(0, 0, 1920, 1080),
            WorkArea = new ScreenRect(0, 0, 1920, 1040),
            IsPrimary = true,
        };

        var principal = "MATERIEL123|0|" + DofusPackages.DofusTouch;
        var clone = "MATERIEL123|999|" + DofusPackages.DofusTouch;

        await _service.SaveWindowRectsAsync(
            new Dictionary<string, StoredWindowRect>
            {
                [principal] = StoredWindowRect.From(new ScreenRect(0, 0, 800, 600), monitor),
            },
            CancellationToken.None);

        await _service.SaveWindowRectsAsync(
            new Dictionary<string, StoredWindowRect>
            {
                [clone] = StoredWindowRect.From(new ScreenRect(10, 10, 400, 300), monitor),
            },
            CancellationToken.None);

        var rects = await _service.GetWindowRectsAsync(CancellationToken.None);

        Assert.Equal(2, rects.Count);
        Assert.Equal(new ScreenRect(0, 0, 800, 600), rects[principal].Bounds);
    }

    [Fact]
    public async Task Une_instance_lancee_est_cochee_pour_le_lancement_suivant()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var principal = "MATERIEL123|0|" + DofusPackages.DofusTouch;

        await _service.SetInstancesEnabledAsync([principal], enabled: true, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.True(settings.Instances.Find(i => i.Key == principal)!.IsEnabled);
        Assert.False(settings.Instances.Find(i => i.Key != principal)!.IsEnabled);
    }

    [Fact]
    public async Task Fermer_une_instance_la_retire_du_lancement_suivant()
    {
        // This is the only action that removes it: closing the game window by
        // hand leaves it in the set, and it will reopen.
        await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        var key = "MATERIEL123|0|" + DofusPackages.DofusTouch;

        await _service.SetInstancesEnabledAsync([key], enabled: true, CancellationToken.None);
        await _service.SetInstancesEnabledAsync([key], enabled: false, CancellationToken.None);

        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.False(settings.Instances.Find(i => i.Key == key)!.IsEnabled);
    }

    [Fact]
    public async Task Cocher_une_instance_deja_cochee_n_ecrit_pas_le_fichier()
    {
        // Every launch reaffirms the state: without this safeguard, the file
        // would be rewritten and everyone notified for nothing.
        await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        var key = "MATERIEL123|0|" + DofusPackages.DofusTouch;

        await _service.SetInstancesEnabledAsync([key], enabled: true, CancellationToken.None);

        var writes = 0;
        _service.Changed += (_, _) => writes++;

        await _service.SetInstancesEnabledAsync([key], enabled: true, CancellationToken.None);

        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task La_visibilite_du_configurateur_est_relue_a_l_identique()
    {
        await _service.SetConfiguratorVisibleAsync(visible: false, CancellationToken.None);

        _service.Invalidate();

        Assert.False((await _service.GetAsync(CancellationToken.None)).ConfiguratorVisible);
    }

    [Fact]
    public async Task Un_ancien_nom_d_action_garde_sa_combinaison()
    {
        // "Tout fermer" ("Close All") became "Quitter" ("Quit"). A file
        // written before the rename must not lose the chosen shortcut.
        Directory.CreateDirectory(_directory);

        await File.WriteAllTextAsync(
            _store.FilePath,
            """
            { "schemaVersion": 4,
              "hotkeys": [ { "action": "CloseAll", "virtualKey": 75, "modifiers": "Control" } ] }
            """,
            CancellationToken.None);

        _service.Invalidate();

        var hotkeys = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + K", hotkeys.For(HotkeyAction.Quit)!.DisplayText);
    }

    /// <summary>
    /// A profile deleted on the phone left its row in the list forever: the
    /// stored entry outlived the profile, and no button could remove it.
    /// </summary>
    [Fact]
    public async Task Un_profil_disparu_du_telephone_est_oublie()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [0],
            }, cancellationToken: CancellationToken.None);

        Assert.Equal(1, oubliees);

        var merged = await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        Assert.Equal([0], merged.Select(i => i.UserId));
    }

    /// <summary>
    /// Nothing is concluded about a phone that could not be read: its
    /// instances remain, like those of an unplugged device.
    /// </summary>
    [Fact]
    public async Task Un_telephone_absent_du_releve_garde_ses_instances()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["UN-AUTRE-TELEPHONE"] = [0],
            }, cancellationToken: CancellationToken.None);

        Assert.Equal(0, oubliees);

        var merged = await _service.MergeInstancesAsync([], CancellationToken.None);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public async Task Un_releve_vide_n_oublie_rien()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        Assert.Equal(
            0,
            await _service.ForgetMissingProfilesAsync(
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal),
                cancellationToken: CancellationToken.None));
    }

    /// <summary>
    /// All the profiles are still there: nothing is forgotten.
    /// </summary>
    [Fact]
    public async Task Un_profil_toujours_la_n_est_pas_touche()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [0, 999],
            }, cancellationToken: CancellationToken.None);

        Assert.Equal(0, oubliees);

        var merged = await _service.MergeInstancesAsync([], CancellationToken.None);

        Assert.Equal(2, merged.Count);
    }

    /// <summary>
    /// The game was uninstalled from a profile that remains: the account must
    /// be dropped too.
    ///
    /// This is the case reported from the field. The Android profile still
    /// exists, so forgetting by profile disappearance did not apply, and the
    /// stored entry kept being carried over indefinitely, even across
    /// restarts.
    /// </summary>
    [Fact]
    public async Task Un_profil_qui_a_perdu_le_jeu_est_oublie()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [0, 999],
            },
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [999],
            },
            CancellationToken.None);

        Assert.Equal(1, oubliees);

        var merged = await _service.MergeInstancesAsync([Instance(0)], CancellationToken.None);

        Assert.Equal([0], merged.Select(i => i.UserId));
    }

    /// <summary>
    /// A profile whose query did not complete proves nothing.
    ///
    /// This is the whole caution behind this cleanup: querying a profile's
    /// packages can fail, and mistaking that failure for an empty answer would
    /// erase accounts at the first hiccup from ADB. A profile that did not
    /// answer is not declared empty, so it does not enter this list.
    /// </summary>
    [Fact]
    public async Task Un_profil_qui_n_a_pas_repondu_garde_son_compte()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [0, 999],
            },
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [],
            },
            CancellationToken.None);

        Assert.Equal(0, oubliees);
    }

    /// <summary>
    /// Without a list of profiles missing the game, nothing changes: this is
    /// the previous behavior, and callers that do not supply it keep it.
    /// </summary>
    [Fact]
    public async Task Sans_releve_des_profils_vides_rien_n_est_oublie_de_plus()
    {
        await _service.MergeInstancesAsync([Instance(0), Instance(999)], CancellationToken.None);

        var oubliees = await _service.ForgetMissingProfilesAsync(
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["MATERIEL123"] = [0, 999],
            },
            cancellationToken: CancellationToken.None);

        Assert.Equal(0, oubliees);
    }
}
