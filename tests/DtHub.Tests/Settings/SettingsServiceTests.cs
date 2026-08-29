using DtHub.Core.Hotkeys;
using DtHub.Core.Settings;
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

    [Fact]
    public async Task Les_valeurs_par_defaut_correspondent_a_ce_qui_est_annonce()
    {
        var settings = await _service.GetAsync(CancellationToken.None);

        Assert.Equal(45, settings.MaxFps);
        Assert.Equal(4000, settings.VideoBitrateKbps);
        Assert.False(settings.AudioEnabled);
        Assert.True(settings.ClipboardSyncEnabled);
        Assert.Equal([60, 70, 80, 90], settings.SizePercentages);
        Assert.True(settings.CheckUpdatesAutomatically);
        Assert.True(settings.ReconnectOnStartup);
        Assert.False(settings.ShowSystemApps);
    }

    [Fact]
    public async Task Une_modification_est_ecrite_immediatement()
    {
        await _service.UpdateAsync(s => s.MaxFps = 60, CancellationToken.None);

        _service.Invalidate();

        Assert.Equal(60, (await _service.GetAsync(CancellationToken.None)).MaxFps);
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
    public async Task Les_reglages_scrcpy_derivent_des_preferences()
    {
        await _service.UpdateAsync(s =>
        {
            s.MaxFps = 30;
            s.VideoBitrateKbps = 8000;
            s.AudioEnabled = true;
            s.ClipboardSyncEnabled = false;
            s.KeyboardMode = ScrcpyKeyboardModeSetting.Uhid;
        }, CancellationToken.None);

        var options = await _service.GetScrcpyOptionsAsync(CancellationToken.None);

        Assert.Equal(30, options.MaxFps);
        Assert.Equal("8000K", options.VideoBitrateArgument);
        Assert.True(options.AudioEnabled);
        Assert.False(options.ClipboardSyncEnabled);
        Assert.Equal(Core.Scrcpy.ScrcpyKeyboardMode.Uhid, options.KeyboardMode);
    }

    [Fact]
    public async Task Des_reglages_scrcpy_aberrants_sont_corriges_a_la_lecture()
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
    public async Task Les_pourcentages_de_taille_sont_personnalisables_et_assainis()
    {
        await _service.UpdateAsync(s => s.SizePercentages = [95, 50, 50, 500], CancellationToken.None);

        var presets = await _service.GetWindowPresetsAsync(CancellationToken.None);

        Assert.Equal([50, 95, 100], presets.Percentages);
    }

    [Fact]
    public async Task Sans_raccourci_enregistre_les_valeurs_par_defaut_sont_rendues()
    {
        var hotkeys = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + Tab", hotkeys.For(HotkeyAction.NextSession)!.DisplayText);
    }

    [Fact]
    public async Task Les_raccourcis_modifies_sont_relus_a_l_identique()
    {
        var modified = HotkeySet.Default.With(HotkeyAction.Recenter, VirtualKeys.F9, HotkeyModifiers.Control);

        await _service.SaveHotkeysAsync(modified, CancellationToken.None);
        _service.Invalidate();

        var reloaded = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + F9", reloaded.For(HotkeyAction.Recenter)!.DisplayText);
        Assert.Equal("Ctrl + Tab", reloaded.For(HotkeyAction.NextSession)!.DisplayText);
    }

    [Fact]
    public async Task Une_action_inconnue_dans_le_fichier_est_ignoree_sans_tout_perdre()
    {
        await _service.UpdateAsync(s => s.Hotkeys =
        [
            new StoredHotkey { Action = "ActionDUneVersionFuture", VirtualKey = 0x41, Modifiers = HotkeyModifiers.Control },
            new StoredHotkey { Action = "Recenter", VirtualKey = VirtualKeys.F9, Modifiers = HotkeyModifiers.Control },
        ], CancellationToken.None);

        var hotkeys = await _service.GetHotkeysAsync(CancellationToken.None);

        Assert.Equal("Ctrl + F9", hotkeys.For(HotkeyAction.Recenter)!.DisplayText);
        Assert.Equal("Ctrl + Tab", hotkeys.For(HotkeyAction.NextSession)!.DisplayText);
    }

    [Fact]
    public async Task Un_favori_se_bascule_dans_les_deux_sens()
    {
        Assert.True(await _service.ToggleFavoriteAsync("MATERIEL123", 999, "com.exemple.app", CancellationToken.None));
        Assert.True(await _service.IsFavoriteAsync("MATERIEL123", 999, "com.exemple.app", CancellationToken.None));

        Assert.False(await _service.ToggleFavoriteAsync("MATERIEL123", 999, "com.exemple.app", CancellationToken.None));
        Assert.False(await _service.IsFavoriteAsync("MATERIEL123", 999, "com.exemple.app", CancellationToken.None));
    }

    [Fact]
    public async Task Le_meme_paquet_sur_deux_profils_a_deux_favoris_distincts()
    {
        await _service.ToggleFavoriteAsync("MATERIEL123", 0, "com.exemple.app", CancellationToken.None);

        Assert.True(await _service.IsFavoriteAsync("MATERIEL123", 0, "com.exemple.app", CancellationToken.None));
        Assert.False(await _service.IsFavoriteAsync("MATERIEL123", 999, "com.exemple.app", CancellationToken.None));
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
        Assert.Equal([60, 70, 80, 90], settings.SizePercentages);
    }

    [Fact]
    public async Task Les_enumerations_sont_ecrites_en_clair_dans_le_fichier()
    {
        await _service.UpdateAsync(s => s.Theme = AppTheme.Dark, CancellationToken.None);

        var json = await File.ReadAllTextAsync(_store.FilePath, CancellationToken.None);

        Assert.Contains("\"theme\": \"Dark\"", json, StringComparison.Ordinal);
    }
}
