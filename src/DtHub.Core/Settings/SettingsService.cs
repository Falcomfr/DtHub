using DtHub.Core.Hotkeys;
using DtHub.Core.Scrcpy;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Point d'accès unique aux réglages. Les valeurs sont chargées une fois puis
/// tenues en mémoire ; chaque modification est écrite immédiatement, il n'y a
/// pas de bouton Enregistrer à oublier.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly IDocumentStore<AppSettingsDocument> _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppSettingsDocument? _current;

    public SettingsService(IDocumentStore<AppSettingsDocument> store) => _store = store;

    /// <summary>Déclenché après chaque écriture réussie.</summary>
    public event EventHandler<AppSettingsDocument>? Changed;

    /// <summary>Réglages courants, chargés à la demande.</summary>
    public async Task<AppSettingsDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_current is not null)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Modifie les réglages et les écrit.</summary>
    public async Task UpdateAsync(
        Action<AppSettingsDocument> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsDocument document;

        try
        {
            _current ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            mutate(_current);
            _current.SchemaVersion = AppSettingsDocument.CurrentSchemaVersion;

            await _store.SaveAsync(_current, cancellationToken).ConfigureAwait(false);
            document = _current;
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, document);
    }

    /// <summary>Force la relecture depuis le disque au prochain accès.</summary>
    public void Invalidate() => _current = null;

    /// <summary>Réglages scrcpy dérivés des préférences.</summary>
    public async Task<ScrcpyOptions> GetScrcpyOptionsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new ScrcpyOptions
        {
            MaxFps = settings.MaxFps,
            VideoBitrateKbps = settings.VideoBitrateKbps,
            AudioEnabled = settings.AudioEnabled,
            ClipboardSyncEnabled = settings.ClipboardSyncEnabled,
            VirtualDisplayWidth = settings.VirtualDisplayWidth,
            VirtualDisplayHeight = settings.VirtualDisplayHeight,
            VirtualDisplayDpi = settings.VirtualDisplayDpi,
            KeyboardMode = settings.KeyboardMode == ScrcpyKeyboardModeSetting.Uhid
                ? ScrcpyKeyboardMode.Uhid
                : ScrcpyKeyboardMode.Sdk,
        }.Sanitized();
    }

    /// <summary>Tailles de fenêtre configurées, corrigées si nécessaire.</summary>
    public async Task<WindowSizePresets> GetWindowPresetsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return new WindowSizePresets { Percentages = settings.SizePercentages }.Sanitized();
    }

    /// <summary>Raccourcis configurés, réparés si le fichier est incohérent.</summary>
    public async Task<HotkeySet> GetHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        var bindings = settings.Hotkeys
            .Select(h => h.ToBinding())
            .Where(b => b is not null)
            .Select(b => b!)
            .ToList();

        return HotkeySet.FromBindings(bindings.Count == 0 ? null : bindings);
    }

    /// <summary>Enregistre l'ensemble des raccourcis.</summary>
    public Task SaveHotkeysAsync(HotkeySet hotkeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);

        return UpdateAsync(
            settings => settings.Hotkeys = [.. hotkeys.Bindings.Select(StoredHotkey.From)],
            cancellationToken);
    }

    /// <summary>Clé de favori d'une application, stable entre deux lancements.</summary>
    public static string FavoriteKey(string deviceId, int userId, string packageName) =>
        $"{deviceId}|{userId.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{packageName}";

    /// <summary>Bascule le statut de favori d'une application.</summary>
    public async Task<bool> ToggleFavoriteAsync(
        string deviceId,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        var key = FavoriteKey(deviceId, userId, packageName);
        var added = false;

        await UpdateAsync(settings =>
        {
            if (settings.FavoriteApps.Remove(key))
            {
                return;
            }

            settings.FavoriteApps.Add(key);
            added = true;
        }, cancellationToken).ConfigureAwait(false);

        return added;
    }

    /// <summary>Vrai si l'application est marquée comme favorite.</summary>
    public async Task<bool> IsFavoriteAsync(
        string deviceId,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken).ConfigureAwait(false);

        return settings.FavoriteApps.Contains(FavoriteKey(deviceId, userId, packageName));
    }

    public void Dispose() => _gate.Dispose();
}
