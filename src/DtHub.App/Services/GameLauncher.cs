using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Windows;

using Microsoft.Extensions.Logging;

namespace DtHub.App.Services;

/// <summary>Compte rendu d'un lancement.</summary>
public sealed record LaunchReport(int Opened, IReadOnlyList<string> Problems)
{
    public bool AnyOpened => Opened > 0;
}

/// <summary>
/// Ouvre les instances cochées, place leurs fenêtres et branche les
/// raccourcis. C'est le seul endroit qui enchaîne ces trois choses.
/// </summary>
public sealed partial class GameLauncher : IAsyncDisposable
{
    private readonly ScrcpySessionManager _sessions;
    private readonly WindowManagerService _windows;
    private readonly DeviceDiscoveryService _devices;
    private readonly DeviceReconnectService _reconnect;
    private readonly IDeviceRegistry _registry;
    private readonly DofusInstanceService _instances;
    private readonly SettingsService _settings;
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly IAppLauncher _apps;
    private readonly ILogger<GameLauncher> _logger;

    private bool _hotkeysWired;

    public GameLauncher(
        ScrcpySessionManager sessions,
        WindowManagerService windows,
        DeviceDiscoveryService devices,
        DeviceReconnectService reconnect,
        IDeviceRegistry registry,
        DofusInstanceService instances,
        SettingsService settings,
        IHotkeyRegistrar hotkeys,
        IAppLauncher apps,
        ILogger<GameLauncher> logger)
    {
        _sessions = sessions;
        _windows = windows;
        _devices = devices;
        _reconnect = reconnect;
        _registry = registry;
        _instances = instances;
        _settings = settings;
        _hotkeys = hotkeys;
        _apps = apps;
        _logger = logger;
    }

    /// <summary>Sessions actuellement ouvertes.</summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions => _sessions.ActiveSessions;

    /// <summary>Signalé à chaque changement d'état d'une session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged
    {
        add => _sessions.SessionChanged += value;
        remove => _sessions.SessionChanged -= value;
    }

    /// <summary>Demandé par le raccourci d'affichage du configurateur.</summary>
    public event EventHandler? ConfiguratorToggleRequested;

    /// <summary>
    /// Balaye les téléphones et rend les instances connues, à jour. Les
    /// instances dont le téléphone est absent restent listées, hors ligne.
    /// </summary>
    public async Task<IReadOnlyList<DofusInstance>> RefreshInstancesAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        _instances.PackageName = settings.PackageName;

        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        var found = await _instances.DiscoverAsync(discovery.Devices, cancellationToken)
            .ConfigureAwait(false);

        return await _settings.MergeInstancesAsync(found, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Téléphones vus maintenant. Les appareils déjà associés sont reconnectés
    /// au passage : un téléphone qui s'annonce sur le réseau n'a pas à être
    /// réassocié à la main.
    /// </summary>
    public async Task<DeviceDiscoveryResult> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKnownDevicesConnectedAsync(cancellationToken).ConfigureAwait(false);

        return await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tente de reconnecter les téléphones déjà associés qui ne répondent pas.
    /// Espacé dans le temps : inutile de sonder le réseau à chaque
    /// rafraîchissement de la liste.
    /// </summary>
    private async Task EnsureKnownDevicesConnectedAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - _lastReconnectAttempt < ReconnectInterval)
        {
            return;
        }

        _lastReconnectAttempt = DateTimeOffset.UtcNow;

        try
        {
            var known = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);
            if (known.Count == 0)
            {
                return;
            }

            var live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var connected = live.Devices.Where(d => d.IsConnected).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

            var missing = known.Where(d => !connected.Contains(d.Id)).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            var outcomes = await _reconnect.TryReconnectAllAsync(missing, cancellationToken)
                .ConfigureAwait(false);

            var recovered = outcomes.Count(o => o.Value is ReconnectOutcome.ReconnectedToLastAddress
                                                or ReconnectOutcome.ReconnectedByDiscovery);

            if (recovered > 0)
            {
                LogReconnected(recovered);
            }
        }
        catch (AdbException)
        {
            // Rien à reconnecter si ADB lui-même est indisponible : le
            // balayage suivant le signalera.
        }
    }

    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(8);

    private DateTimeOffset _lastReconnectAttempt = DateTimeOffset.MinValue;

    /// <summary>
    /// Ouvre toutes les instances cochées, puis empile leurs fenêtres. Une
    /// instance dont le téléphone est absent est signalée sans empêcher les
    /// autres de s'ouvrir.
    /// </summary>
    public async Task<LaunchReport> LaunchEnabledAsync(CancellationToken cancellationToken = default)
    {
        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);

        var instances = await RefreshInstancesAsync(cancellationToken).ConfigureAwait(false);
        var enabled = instances.Where(i => i.IsEnabled).ToList();

        if (enabled.Count == 0)
        {
            return new LaunchReport(0, ["Aucune instance n'est cochée."]);
        }

        return await LaunchAsync(enabled, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ouvre une liste d'instances précise.</summary>
    public async Task<LaunchReport> LaunchAsync(
        IReadOnlyList<DofusInstance> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);

        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var options = await _settings.GetScrcpyOptionsAsync(cancellationToken).ConfigureAwait(false);
        var serials = await ResolveSerialsAsync(cancellationToken).ConfigureAwait(false);

        List<string> problems = [];
        var opened = 0;

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsOpen(instance))
            {
                continue;
            }

            if (!serials.TryGetValue(instance.DeviceId, out var serial))
            {
                problems.Add($"{instance.DisplayName} : le téléphone n'est pas connecté.");
                continue;
            }

            var session = await _sessions.StartAsync(
                ToTarget(instance, serial), options, null, cancellationToken).ConfigureAwait(false);

            if (session.State == ScrcpySessionState.Failed)
            {
                problems.Add($"{instance.DisplayName} : {session.FailureMessage}");
                continue;
            }

            opened++;
        }

        if (opened > 0)
        {
            await _windows.ArrangeAsync(_sessions.ActiveSessions, cancellationToken).ConfigureAwait(false);
        }

        LogLaunch(opened, problems.Count);

        return new LaunchReport(opened, problems);
    }

    /// <summary>Ferme puis rouvre une instance, sans toucher aux autres.</summary>
    public async Task<LaunchReport> RestartAsync(
        DofusInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var existing = FindSession(instance);
        if (existing is not null)
        {
            await _sessions.StopAsync(existing.Id, cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }

        var serials = await ResolveSerialsAsync(cancellationToken).ConfigureAwait(false);

        if (serials.TryGetValue(instance.DeviceId, out var serial))
        {
            // Le jeu garde son état côté téléphone : on l'arrête franchement
            // pour que la relance parte d'un écran propre.
            await _apps.ForceStopAsync(serial, instance.UserId, instance.PackageName, cancellationToken)
                .ConfigureAwait(false);
        }

        return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ferme une instance.</summary>
    public async Task StopAsync(DofusInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FindSession(instance) is { } session)
        {
            await _sessions.StopAsync(session.Id, cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }
    }

    /// <summary>Ferme toutes les fenêtres ouvertes par l'application.</summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
        _sessions.PruneFinished();

        await _hotkeys.SetEnabledAsync(false).ConfigureAwait(false);
    }

    /// <summary>Remet toutes les fenêtres en place.</summary>
    public async Task<int> ArrangeAsync(CancellationToken cancellationToken = default)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        return await _windows.ArrangeAsync(_sessions.ActiveSessions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Session ouverte correspondant à une instance, s'il y en a une.</summary>
    public ScrcpySession? FindSession(DofusInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return _sessions.ActiveSessions.FirstOrDefault(
            s => string.Equals(s.Target.Key, instance.Key, StringComparison.Ordinal));
    }

    /// <summary>Vrai si l'instance a une fenêtre ouverte.</summary>
    public bool IsOpen(DofusInstance instance) => FindSession(instance) is not null;

    /// <summary>Zone occupée par les fenêtres de jeu, pour placer le configurateur ailleurs.</summary>
    public ScreenRect? GameArea()
    {
        var sessions = _sessions.ActiveSessions;
        var aspect = sessions.Count > 0 ? sessions[0].SourceAspectRatio : 1080.0 / 1920.0;

        return _windows.PreviewGameArea(aspect);
    }

    public ScreenRect? WorkArea() => _windows.WorkArea();

    public IReadOnlyList<MonitorInfo> Monitors => _windows.GetMonitors();

    /// <summary>Recharge les raccourcis après une modification.</summary>
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

    private static LaunchTarget ToTarget(DofusInstance instance, string serial) => new()
    {
        DeviceId = instance.DeviceId,
        Serial = serial,
        UserId = instance.UserId,
        PackageName = instance.PackageName,
        LaunchComponent = instance.LaunchComponent,
        DisplayName = instance.DisplayName,
    };

    private async Task<Dictionary<string, string>> ResolveSerialsAsync(CancellationToken cancellationToken)
    {
        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        return discovery.Devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, d => d.Serial, StringComparer.Ordinal);
    }

    private async Task ApplyWindowSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        _windows.Anchor = settings.GameAnchor;
        _windows.SizePercent = settings.GameSizePercent;
        _windows.PreferredMonitorDeviceName = settings.PreferredMonitorDeviceName;
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
        await _hotkeys.SetEnabledAsync(true).ConfigureAwait(false);
    }

    /// <summary>
    /// Les raccourcis restent actifs tant qu'une fenêtre de l'application est
    /// au premier plan, fenêtres de jeu comme configurateur. Ailleurs, les
    /// combinaisons reviennent aux autres logiciels.
    /// </summary>
    private async void OnForegroundChanged(object? sender, nint window)
    {
        try
        {
            var mine = _sessions.ActiveSessions.Any(s => s.WindowHandle == window)
                       || OwnsWindow?.Invoke(window) == true;

            await _hotkeys.SetEnabledAsync(mine).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogHotkeyFailure(exception);
        }
    }

    /// <summary>
    /// Permet à l'interface de déclarer ses propres fenêtres, pour que les
    /// raccourcis fonctionnent aussi depuis le configurateur.
    /// </summary>
    public Func<nint, bool>? OwnsWindow { get; set; }

    private async void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        try
        {
            switch (action)
            {
                case HotkeyAction.ToggleConfigurator:
                    ConfiguratorToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case HotkeyAction.NextInstance:
                    _windows.FocusNext(_sessions.ActiveSessions);
                    break;

                case HotkeyAction.PreviousInstance:
                    _windows.FocusPrevious(_sessions.ActiveSessions);
                    break;

                case HotkeyAction.Rearrange:
                    await ArrangeAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.CloseAll:
                    await CloseAllAsync().ConfigureAwait(false);
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lancement terminé : {opened} fenêtre(s) ouverte(s), {problems} problème(s).")]
    private partial void LogLaunch(int opened, int problems);

    [LoggerMessage(Level = LogLevel.Information, Message = "{count} téléphone(s) reconnecté(s) automatiquement.")]
    private partial void LogReconnected(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Un raccourci n'a pas pu être traité.")]
    private partial void LogHotkeyFailure(Exception exception);
}
