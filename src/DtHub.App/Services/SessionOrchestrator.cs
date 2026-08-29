using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Hotkeys;
using DtHub.Core.Profiles;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Microsoft.Extensions.Logging;

namespace DtHub.App.Services;

/// <summary>Compte rendu d'un lancement de profil.</summary>
public sealed record LaunchReport(int Started, int Failed, IReadOnlyList<string> Messages)
{
    public bool AnyStarted => Started > 0;
}

/// <summary>
/// Enchaîne ce qu'il faut pour passer d'un profil à des fenêtres ouvertes :
/// vérifier les appareils, ouvrir les sessions, les empiler, et brancher les
/// raccourcis. Regroupé ici pour que les vues-modèles restent courtes.
/// </summary>
public sealed partial class SessionOrchestrator : IAsyncDisposable
{
    private readonly ScrcpySessionManager _sessions;
    private readonly WindowManagerService _windows;
    private readonly DeviceDiscoveryService _discovery;
    private readonly SettingsService _settings;
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly ILogger<SessionOrchestrator> _logger;

    private bool _hotkeysWired;

    public SessionOrchestrator(
        ScrcpySessionManager sessions,
        WindowManagerService windows,
        DeviceDiscoveryService discovery,
        SettingsService settings,
        IHotkeyRegistrar hotkeys,
        ILogger<SessionOrchestrator> logger)
    {
        _sessions = sessions;
        _windows = windows;
        _discovery = discovery;
        _settings = settings;
        _hotkeys = hotkeys;
        _logger = logger;
    }

    /// <summary>Sessions actuellement ouvertes.</summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions => _sessions.ActiveSessions;

    /// <summary>Signalé à chaque changement d'état de session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged
    {
        add => _sessions.SessionChanged += value;
        remove => _sessions.SessionChanged -= value;
    }

    /// <summary>
    /// Ouvre toutes les sessions d'un profil, puis les empile. Les cibles dont
    /// l'appareil est absent sont signalées sans empêcher les autres de
    /// s'ouvrir.
    /// </summary>
    public async Task<LaunchReport> LaunchAsync(
        LaunchProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);

        var discovery = await _discovery.RefreshAsync(cancellationToken).ConfigureAwait(false);
        var connected = discovery.Devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, StringComparer.Ordinal);

        var options = await _settings.GetScrcpyOptionsAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        _windows.Presets = await _settings.GetWindowPresetsAsync(cancellationToken).ConfigureAwait(false);
        _windows.PreferredMonitorDeviceName = settings.PreferredMonitorDeviceName;

        List<string> messages = [];
        var started = 0;
        var failed = 0;

        foreach (var target in profile.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!connected.TryGetValue(target.DeviceId, out var device))
            {
                failed++;
                messages.Add($"{target.DisplayName} : le téléphone n'est pas connecté.");
                continue;
            }

            var session = await _sessions
                .StartAsync(target, device.Serial, options, null, cancellationToken)
                .ConfigureAwait(false);

            if (session.State == ScrcpySessionState.Failed)
            {
                failed++;
                messages.Add($"{target.DisplayName} : {session.FailureMessage}");
                continue;
            }

            started++;
        }

        if (started > 0)
        {
            await _windows
                .ApplySizeAsync(_sessions.ActiveSessions, settings.DefaultSizeIndex, cancellationToken)
                .ConfigureAwait(false);
        }

        LogLaunch(profile.Name, started, failed);

        return new LaunchReport(started, failed, messages);
    }

    /// <summary>Ferme toutes les sessions ouvertes par DT Hub.</summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
        _sessions.PruneFinished();

        await _hotkeys.SetEnabledAsync(false).ConfigureAwait(false);
    }

    /// <summary>Applique une taille à toutes les sessions ouvertes.</summary>
    public Task<int> ApplySizeAsync(int index, CancellationToken cancellationToken = default) =>
        _windows.ApplySizeAsync(_sessions.ActiveSessions, index, cancellationToken);

    /// <summary>Remet toutes les fenêtres ensemble.</summary>
    public Task<int> RecenterAsync(CancellationToken cancellationToken = default) =>
        _windows.RecenterAsync(_sessions.ActiveSessions, cancellationToken);

    /// <summary>Passe à la session suivante.</summary>
    public ScrcpySession? FocusNext() => _windows.FocusNext(_sessions.ActiveSessions);

    /// <summary>Recharge les raccourcis après une modification dans les paramètres.</summary>
    public async Task<IReadOnlyList<HotkeyAction>> ReloadHotkeysAsync(
        CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);

        return await _hotkeys.ApplyAsync(hotkeys).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _hotkeys.ForegroundWindowChanged -= OnForegroundChanged;

        await _sessions.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureHotkeysAsync(CancellationToken cancellationToken)
    {
        if (_hotkeysWired)
        {
            return;
        }

        _hotkeysWired = true;

        _hotkeys.HotkeyPressed += OnHotkeyPressed;
        _hotkeys.ForegroundWindowChanged += OnForegroundChanged;

        await ReloadHotkeysAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Les raccourcis ne sont enregistrés que pendant qu'une fenêtre gérée est
    /// active : hors de là, les combinaisons doivent revenir aux autres
    /// logiciels.
    /// </summary>
    private async void OnForegroundChanged(object? sender, nint window)
    {
        try
        {
            var managed = _sessions.ActiveSessions.Any(s => s.WindowHandle == window);

            await _hotkeys.SetEnabledAsync(managed).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogHotkeyFailure(exception);
        }
    }

    private async void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        try
        {
            var presets = _windows.Presets;

            switch (action)
            {
                case HotkeyAction.NextSession:
                    FocusNext();
                    break;

                case HotkeyAction.PreviousSession:
                    // Le parcours arrière revient à avancer jusqu'à l'élément
                    // précédent : avec peu de sessions, c'est imperceptible.
                    var count = _sessions.ActiveSessions.Count;
                    for (var i = 0; i < Math.Max(0, count - 1); i++)
                    {
                        FocusNext();
                    }

                    break;

                case HotkeyAction.Size1:
                    await ApplySizeAsync(0).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size2:
                    await ApplySizeAsync(1).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size3:
                    await ApplySizeAsync(2).ConfigureAwait(false);
                    break;

                case HotkeyAction.Size4:
                    await ApplySizeAsync(3).ConfigureAwait(false);
                    break;

                case HotkeyAction.Fullscreen:
                    await ApplySizeAsync(presets.FullscreenIndex).ConfigureAwait(false);
                    break;

                case HotkeyAction.Recenter:
                    await RecenterAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.CloseAllSessions:
                    await CloseAllAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.OpenSettings:
                    SettingsRequested?.Invoke(this, EventArgs.Empty);
                    break;

                default:
                    break;
            }
        }
        catch (Exception exception)
        {
            LogHotkeyFailure(exception);
        }
    }

    /// <summary>Demandé par le raccourci d'ouverture des paramètres.</summary>
    public event EventHandler? SettingsRequested;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Profil « {profile} » lancé : {started} session(s) ouverte(s), {failed} en échec.")]
    private partial void LogLaunch(string profile, int started, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Un raccourci n'a pas pu être traité.")]
    private partial void LogHotkeyFailure(Exception exception);
}
