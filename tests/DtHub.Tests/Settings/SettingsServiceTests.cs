using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
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
    public async Task Les_valeurs_par_defaut_correspondent_a_ce_qui_est_annonce()
    {
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.False(settings.SetupCompleted);
        Assert.Empty(settings.Instances);
        Assert.Equal(WindowAnchor.MiddleLeft, settings.GameAnchor);
        Assert.Equal([40, 60, 80, 100], settings.SizePercentages);
        Assert.Equal(1, settings.SizeIndex);
        Assert.Equal(45, settings.MaxFps);
        Assert.False(settings.AudioEnabled);
        Assert.True(settings.ClipboardSyncEnabled);
        Assert.Equal("com.ankama.dofustouch", settings.PackageName);
    }

    [Fact]
    public async Task L_ecran_virtuel_par_defaut_est_en_paysage()
    {
        // Le jeu s'affiche en paysage : un écran vertical le réduirait à une
        // bande au milieu de la fenêtre.
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

        await _service.UpdateAsync(s => s.MaxFps = 30, CancellationToken.None);
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

        // Au balayage suivant, le téléphone n'est plus là.
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
            s.MaxFps = 30;
            s.VideoBitrateKbps = 8000;
            s.AudioEnabled = true;
            s.ClipboardSyncEnabled = false;
        }, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(30, options.MaxFps);
        Assert.Equal("8000K", options.VideoBitrateArgument);
        Assert.True(options.AudioEnabled);
        Assert.False(options.ClipboardSyncEnabled);
    }

    [Fact]
    public async Task Des_reglages_de_mirroring_aberrants_sont_corriges()
    {
        await _service.UpdateAsync(s =>
        {
            s.MaxFps = 10000;
            s.VideoBitrateKbps = 0;
        }, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(240, options.MaxFps);
        Assert.Equal(200, options.VideoBitrateKbps);
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
        await _service.UpdateAsync(s => s.MaxFps = 30, CancellationToken.None);
        await File.WriteAllTextAsync(_store.FilePath, "{ pas du json", CancellationToken.None);

        _service.Invalidate();

        Assert.Equal(45, (await _service.GetAsync(CancellationToken.None)).MaxFps);
    }

    [Fact]
    public async Task Un_fichier_partiel_complete_les_valeurs_manquantes()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_store.FilePath, """{ "maxFps": 24 }""", CancellationToken.None);

        _service.Invalidate();
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(24, settings.MaxFps);
        Assert.Equal(4000, settings.VideoBitrateKbps);
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
        // L'ordre est global : il doit survivre à une redécouverte, qui
        // refusionne toutes les instances.
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
        // C'est le seul geste qui l'en retire : fermer la fenêtre de jeu à la
        // main la laisse dans l'ensemble et elle rouvrira.
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
        // Chaque lancement réaffirme l'état : sans ce garde-fou, le fichier
        // serait réécrit et tout le monde prévenu pour rien.
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
        // « Tout fermer » est devenu « Quitter ». Un fichier écrit avant le
        // renommage ne doit pas perdre le raccourci choisi.
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
}
