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
    private readonly DevicePairingService _pairing;
    private readonly IDeviceRegistry _registry;
    private readonly DofusInstanceService _instances;
    private readonly SettingsService _settings;
    private readonly IHotkeyRegistrar _hotkeys;
    private readonly AppRestartService _restarts;
    private readonly ILogger<GameLauncher> _logger;

    private bool _hotkeysWired;

    /// <summary>Dossier de l'icône des fenêtres de jeu, posé par l'application.</summary>
    public string? IconDirectory { get => _iconDirectory; set => _iconDirectory = value; }

    private string? _iconDirectory;

    /// <summary>Vrai pendant une fermeture voulue : inutile d'en journaliser le détail.</summary>
    private bool _closing;

    public GameLauncher(
        ScrcpySessionManager sessions,
        WindowManagerService windows,
        DeviceDiscoveryService devices,
        DeviceReconnectService reconnect,
        DevicePairingService pairing,
        IDeviceRegistry registry,
        DofusInstanceService instances,
        SettingsService settings,
        IHotkeyRegistrar hotkeys,
        AppRestartService restarts,
        ILogger<GameLauncher> logger)
    {
        _sessions = sessions;
        _windows = windows;
        _devices = devices;
        _reconnect = reconnect;
        _pairing = pairing;
        _registry = registry;
        _instances = instances;
        _settings = settings;
        _hotkeys = hotkeys;
        _restarts = restarts;
        _logger = logger;

        // Une session qui meurt après son ouverture ne laissait aucune trace :
        // la fenêtre disparaissait et le journal restait muet.
        _sessions.SessionChanged += OnSessionChanged;

        // La fenêtre est placée avant l'ouverture du jeu, pour qu'il naisse à
        // la taille définitive.
        _sessions.PrepareWindow = async (session, cancellationToken) =>
        {
            // La fenêtre est maintenue garée hors écran : scrcpy recentre la
            // sienne à la première image. Sa taille, elle, n'est pas touchée :
            // l'afficheur est déjà né à la bonne, et le jeu fige la hauteur de
            // sa mise en page à son initialisation.
            if (_pendingPlacement is { } placement)
            {
                await _windows.MoveOnlyAsync(session, placement.X, placement.Y, cancellationToken)
                    .ConfigureAwait(false);
            }
        };
    }

    /// <summary>Instances laissées de côté par les placements automatiques.</summary>
    private IReadOnlySet<string> _unmanaged = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Réglages dérivés de la qualité choisie, relus à chaque lancement.</summary>
    private QualityProfile _quality = QualityProfile.For(StreamQuality.Medium);

    /// <summary>Rythme des contrôles et des sondages, selon la qualité.</summary>
    public QualityProfile Quality => _quality;

    /// <summary>Position à poser avant l'ouverture du jeu.</summary>
    private ScrcpyWindowPlacement? _pendingPlacement;

    /// <summary>
    /// Journalise la mort d'une session, avec la sortie de scrcpy. Sans cela,
    /// une fenêtre qui se ferme d'elle-même est indiagnosticable.
    /// </summary>
    private void OnSessionChanged(object? sender, ScrcpySession session)
    {
        if (session.IsAlive || _closing)
        {
            return;
        }

        LogSessionEnded(
            session.Target.DisplayName,
            session.State.ToString(),
            session.FailureMessage ?? "aucun message",
            string.Join(Environment.NewLine, session.RecentOutput));

        if (_sessions.ActiveSessions.Count == 0)
        {
            LastWindowClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sessions actuellement ouvertes.</summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions => _sessions.ActiveSessions;

    /// <summary>
    /// Fenêtres qui suivent les placements automatiques.
    ///
    /// Une fenêtre décochée dans la liste reste où elle est : le parcours au
    /// clavier, le replacement, le côte à côte et les tailles l'ignorent. Elle
    /// s'ouvre, se ferme et se souvient de sa place comme les autres.
    /// </summary>
    public IReadOnlyList<ScrcpySession> ManagedSessions =>
        [.. _sessions.ActiveSessions.Where(s => !_unmanaged.Contains(s.Target.Key))];

    /// <summary>Signalé à chaque changement d'état d'une session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged
    {
        add => _sessions.SessionChanged += value;
        remove => _sessions.SessionChanged -= value;
    }

    /// <summary>Demandé par le raccourci d'affichage du configurateur.</summary>
    public event EventHandler? ConfiguratorToggleRequested;

    /// <summary>
    /// Demandé par le raccourci de sortie. L'arrêt lui-même appartient à
    /// l'application, qui doit d'abord retenir l'état de la session.
    /// </summary>
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Signalé quand la dernière fenêtre de jeu se ferme d'elle-même. Les
    /// fermetures voulues par l'application n'en font pas partie.
    /// </summary>
    public event EventHandler? LastWindowClosed;

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
    /// Reconnecte tout ce qui peut l'être, sans intervention.
    ///
    /// Deux voies complémentaires. D'abord, tout ce qui s'annonce sur le
    /// réseau : l'annonce porte le numéro de série et l'adresse du moment, et
    /// ADB conserve la clé d'association, donc un changement d'adresse ou de
    /// port ne gêne pas. Ensuite, les appareils mémorisés qui ne répondent
    /// toujours pas, via leur dernière adresse connue.
    ///
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
            // Ménage d'abord : une connexion morte fausse la liste et peut
            // masquer le téléphone réellement joignable.
            await _devices.PruneStaleWirelessTransportsAsync(cancellationToken).ConfigureAwait(false);

            var live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

            var addresses = live.Devices
                .Where(d => d.IsConnected)
                .Select(d => d.Serial)
                .ToHashSet(StringComparer.Ordinal);

            // Une tentative sur une annonce n'aboutit que pour un téléphone
            // déjà associé à ce PC : ADB refuse les autres.
            var opened = await _pairing.ConnectAnnouncedAsync(addresses, cancellationToken)
                .ConfigureAwait(false);

            var recovered = opened.Count;

            var known = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);

            if (known.Count > 0)
            {
                if (recovered > 0)
                {
                    live = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }

                var connected = live.Devices
                    .Where(d => d.IsConnected)
                    .Select(d => d.Id)
                    .ToHashSet(StringComparer.Ordinal);

                var missing = known.Where(d => !connected.Contains(d.Id)).ToList();

                if (missing.Count > 0)
                {
                    var outcomes = await _reconnect.TryReconnectAllAsync(missing, cancellationToken)
                        .ConfigureAwait(false);

                    recovered += outcomes.Count(o => o.Value is ReconnectOutcome.ReconnectedToLastAddress
                                                     or ReconnectOutcome.ReconnectedByDiscovery);
                }
            }

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

    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

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

        options = options with
        {
            WindowTitleHint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false),
            IconDirectory = _iconDirectory,
        };

        var serials = await ResolveSerialsAsync(cancellationToken).ConfigureAwait(false);

        // La position est donnée à scrcpy dès le lancement. Le faire après
        // coup ne suffit pas : scrcpy recentre sa fenêtre quand il reçoit la
        // première image, donc après notre placement.
        var remembered = await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

        List<string> problems = [];
        List<ScrcpySession> started = [];

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

            remembered.TryGetValue(instance.Key, out var stored);

            var placement = ComputePlacement(options, stored);
            _pendingPlacement = placement;

            var target = ToTarget(instance, serial);
            var display = WithDisplayFor(options, placement, stored);

            var session = await _sessions.StartAsync(
                target, display, placement, cancellationToken).ConfigureAwait(false);

            // Les encodeurs vidéo annoncent une définition maximale, variable
            // d'un appareil à l'autre : une tablette modeste peut plafonner là
            // où un téléphone récent monte en 8K. Plutôt que de renoncer, on
            // retente une fois à une définition qu'aucun encodeur ne refuse.
            if (session.State == ScrcpySessionState.Failed
                && display.VirtualDisplayHeight > DisplayLadder.FallbackHeight)
            {
                LogDisplayFallback(instance.DisplayName, display.VirtualDisplayHeight);

                session = await _sessions.StartAsync(
                    target,
                    display with
                    {
                        VirtualDisplayWidth = DisplayLadder.FallbackWidth,
                        VirtualDisplayHeight = DisplayLadder.FallbackHeight,
                    },
                    placement,
                    cancellationToken).ConfigureAwait(false);
            }

            if (session.State == ScrcpySessionState.Failed)
            {
                problems.Add($"{instance.DisplayName} : {session.FailureMessage}");

                // Sans la sortie de scrcpy, un refus se résume à « la session
                // n'a pas pu s'ouvrir », ce qui n'aide personne.
                LogSessionFailure(
                    instance.DisplayName,
                    session.CommandLine,
                    string.Join(Environment.NewLine, session.RecentOutput));

                continue;
            }

            started.Add(session);
        }

        // Seules les fenêtres qui viennent d'ouvrir sont placées. Replacer les
        // autres les arracherait à l'endroit où l'utilisateur les a mises, et
        // ferait recréer leur afficheur virtuel côté Android.
        if (started.Count > 0)
        {
            await _windows.RestoreAsync(started, remembered, cancellationToken).ConfigureAwait(false);

        }

        // Ce qui vient d'être ouvert rouvrira au lancement suivant. Seul le
        // bouton « Fermer » retire une instance de cet ensemble : fermer une
        // fenêtre de jeu à la main ne doit rien y changer.
        if (started.Count > 0)
        {
            await _settings.SetInstancesEnabledAsync(
                [.. started.Select(s => s.Target.Key)], enabled: true, cancellationToken)
                .ConfigureAwait(false);
        }

        LogLaunch(started.Count, problems.Count);

        return new LaunchReport(started.Count, problems);
    }

    /// <summary>
    /// Ferme puis rouvre une instance, sans toucher aux autres. Le jeu est
    /// arrêté franchement sur l'appareil : sans cela il reprendrait dans
    /// l'état où il était, et la relance n'aurait servi à rien.
    /// </summary>
    public async Task<LaunchReport> RestartAsync(
        DofusInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FindSession(instance) is not { } session)
        {
            return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
        }

        // Seul le jeu repart : la session scrcpy et sa fenêtre sont conservées,
        // et l'utilisateur ne voit rien clignoter.
        var restart = await _restarts.RestartAsync(session, cancellationToken).ConfigureAwait(false);

        if (restart.Outcome == AppRestartOutcome.Restarted)
        {
            return new LaunchReport(1, []);
        }

        // Le redémarrage court a échoué : on ferme et on rouvre tout, ce qui
        // reste le dernier recours utile.
        LogRestartFallback(instance.DisplayName, restart.UserMessage ?? "afficheur inconnu");

        await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);

        return await LaunchAsync([instance], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ferme une instance, et la retire du lancement suivant.
    ///
    /// C'est le seul geste qui l'en retire : fermer la fenêtre de jeu à la
    /// main la laisse dans l'ensemble et elle rouvrira.
    /// </summary>
    public async Task StopAsync(DofusInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (FindSession(instance) is { } session)
        {
            // La géométrie est relevée avant la fermeture : sans quoi la
            // fenêtre rouvrirait ailleurs le jour où on la relance.
            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        await _settings.SetInstancesEnabledAsync([instance.Key], enabled: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Arrête une session sans toucher au lancement suivant.
    ///
    /// Le drapeau évite que fermer la dernière session ne referme
    /// l'application : c'est nous qui fermons, pas le jeu qui meurt.
    /// </summary>
    private async Task StopSessionAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        _closing = true;

        try
        {
            await _sessions.StopAsync(session.Id, cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }
        finally
        {
            _closing = false;
        }
    }

    /// <summary>
    /// Rompt l'association d'un appareil : ses fenêtres se ferment et il sort
    /// de la mémoire, code d'appairage compris.
    /// </summary>
    public async Task ForgetDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        foreach (var session in _sessions.ActiveSessions
            .Where(s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal))
            .ToList())
        {
            await StopSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        await _registry.ForgetAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ferme toutes les fenêtres ouvertes par l'application.</summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        // La géométrie est relevée tant que les fenêtres existent encore.
        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        _closing = true;

        try
        {
            await _sessions.StopAllAsync(cancellationToken).ConfigureAwait(false);
            _sessions.PruneFinished();
        }
        finally
        {
            _closing = false;
        }

        await _hotkeys.SetEnabledAsync(false).ConfigureAwait(false);
    }

    /// <summary>
    /// Relève où chaque fenêtre a été laissée et l'enregistre. Appelée avant
    /// toute fermeture, et à la sortie : c'est ce qui permet à une fenêtre
    /// déplacée à la souris de revenir au même endroit.
    /// </summary>
    public async Task CaptureGeometriesAsync(CancellationToken cancellationToken = default)
    {
        var captured = _windows.CaptureGeometries(_sessions.ActiveSessions);

        if (captured.Count == 0)
        {
            return;
        }

        await _settings.SaveWindowRectsAsync(
            captured.ToDictionary(c => c.Key, c => c.Rect, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ramène les fenêtres au rapport de leur afficheur, une fois leur taille
    /// stabilisée. Appelée régulièrement.
    /// </summary>
    /// <summary>
    /// Entretien périodique : corrige la forme des fenêtres et retient
    /// laquelle est au premier plan.
    /// </summary>
    public void Watch()
    {
        var sessions = _sessions.ActiveSessions;

        _windows.TrackActiveWindow(sessions);
        _windows.EnforceAspect(sessions);
    }


    /// <summary>
    /// Empile les fenêtres sur celle qui est active, ou sur la première.
    /// C'est ce que fait le raccourci de replacement, et le bouton de la barre
    /// du bas : on place une fenêtre où on la veut, les autres la rejoignent.
    /// </summary>
    /// <summary>
    /// Range les fenêtres côte à côte, la fenêtre active à droite.
    /// </summary>
    public async Task<int> TileAsync(CancellationToken cancellationToken = default)
    {
        var placed = await _windows
            .TileAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return placed;
    }

    public async Task<int> StackOnActiveAsync(CancellationToken cancellationToken = default)
    {
        var moved = await _windows
            .StackOnActiveAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return moved;
    }


    /// <summary>Remet toutes les fenêtres en place, à la taille en cours.</summary>
    public async Task<int> ArrangeAsync(CancellationToken cancellationToken = default)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var moved = await _windows.ArrangeAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        // Le replacement rapide devient la nouvelle géométrie de référence,
        // sans quoi la mémoire divergerait de ce qui est à l'écran.
        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return moved;
    }

    /// <summary>
    /// Applique la taille posée au curseur.
    ///
    /// Deux usages, volontairement distincts. Pendant que le curseur bouge on
    /// ne fait que déplacer les fenêtres : relire les réglages et écrire le
    /// fichier à chaque cran rendait le geste saccadé. L'enregistrement n'a
    /// lieu qu'une fois, quand le curseur est reposé.
    /// </summary>
    public async Task<int> ApplyPercentAsync(
        int percent,
        bool persist = true,
        CancellationToken cancellationToken = default)
    {
        var moved = await _windows
            .ApplyPercentAsync(ManagedSessions, percent, cancellationToken)
            .ConfigureAwait(false);

        if (persist)
        {
            await _settings.SaveCustomSizePercentAsync(percent, cancellationToken).ConfigureAwait(false);

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);
        }

        return moved;
    }

    /// <summary>
    /// Rouvre les fenêtres devenues trop petites pour la taille demandée.
    ///
    /// Le jeu ne se remet pas en page au-delà de la hauteur qu'il avait à son
    /// ouverture : agrandir davantage laisserait une bande. La seule façon de
    /// lui donner cette hauteur est de le rouvrir dessus. Cela n'arrive que
    /// sur une demande explicite d'agrandissement, jamais pendant un geste.
    /// </summary>
    /// <summary>Applique une taille à toutes les fenêtres et la retient.</summary>
    public async Task<int> ApplySizeAsync(int sizeIndex, CancellationToken cancellationToken = default)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        await _settings.UpdateAsync(
            s =>
            {
                s.SizeIndex = sizeIndex;
                s.CustomSizePercent = 0;
            },
            cancellationToken).ConfigureAwait(false);


        var moved = await _windows
            .ApplySizeAsync(ManagedSessions, sizeIndex, cancellationToken)
            .ConfigureAwait(false);

        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return moved;
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
        // Zéro signifie « aucune contrainte de rapport » : c'est le cas en
        // mode flexible, où l'afficheur épouse la fenêtre.
        var aspect = sessions.Count > 0 ? sessions[0].SourceAspectRatio : 0;

        return _windows.PreviewGameArea(aspect);
    }

    public ScreenRect? WorkArea() => _windows.WorkArea();


    /// <summary>Recharge les raccourcis après une modification.</summary>
    public async Task<IReadOnlyList<HotkeyAction>> ReloadHotkeysAsync(
        CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);

        // Le rappel inscrit dans le titre des fenêtres suit la combinaison.
        // C'est ici qu'il faut le faire : l'éditeur recharge par ce chemin,
        // quelle que soit la fenêtre qui l'a ouvert.
        await RefreshWindowTitlesAsync(cancellationToken).ConfigureAwait(false);

        return await _hotkeys.ApplyAsync(hotkeys).ConfigureAwait(false);
    }

    /// <summary>
    /// Met à jour le rappel du raccourci dans le titre des fenêtres ouvertes.
    /// Appelé après une modification dans l'éditeur.
    /// </summary>
    public async Task<int> RefreshWindowTitlesAsync(CancellationToken cancellationToken = default)
    {
        var hint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false);

        return _windows.Retitle(
            _sessions.ActiveSessions,
            session => ScrcpyCommandBuilder.BuildWindowTitle(session.Target.DisplayName, hint));
    }

    public async ValueTask DisposeAsync()
    {
        _hotkeys.HotkeyPressed -= OnHotkeyPressed;
        _hotkeys.ForegroundWindowChanged -= OnForegroundChanged;

        await _sessions.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Rectangle que toutes les fenêtres partageront. Calculé une fois : c'est
    /// ce qui garantit leur superposition exacte.
    /// </summary>
    private ScrcpyWindowPlacement? ComputePlacement(ScrcpyOptions options, StoredWindowRect? remembered)
    {
        // La fenêtre garde le rapport de l'afficheur : l'image y est mise à
        // l'échelle, et s'en écarter laisserait une bande.
        var aspect = options is { UseVirtualDisplay: true, VirtualDisplayHeight: > 0 }
            ? (double)options.VirtualDisplayWidth / options.VirtualDisplayHeight
            : 0;

        var monitors = _windows.GetMonitors();

        var rect = remembered is not null
            ? WindowLayoutCalculator.RestoreRemembered(
                  remembered.Bounds, remembered.MonitorDeviceName, remembered.Monitor, monitors)
              ?? _windows.PreviewGameArea(aspect)
            : _windows.PreviewGameArea(aspect);

        // Journalisé : la disposition dépend de l'écran et de sa mise à
        // l'échelle, et un chiffre inattendu se voit tout de suite ici.
        LogPlacement(
            string.Join(", ", monitors.Select(m => $"{m.DeviceName} {m.Bounds} utile {m.WorkArea}")),
            rect?.ToString() ?? "aucun");

        if (rect is not { } value)
        {
            return null;
        }

        // La taille transmise est celle de la zone client, cadre déduit :
        // scrcpy dimensionne sa fenêtre par l'intérieur, et lui donner le
        // rectangle extérieur la ferait naître trop grande d'une barre de
        // titre.
        var chrome = _windows.WindowChrome();

        return new ScrcpyWindowPlacement(
            value.X,
            value.Y,
            Math.Max(1, value.Width - chrome.Width),
            Math.Max(1, value.Height - chrome.Height));
    }

    /// <summary>
    /// Adapte la définition de l'afficheur à la fenêtre de cette instance.
    ///
    /// L'image est mise à l'échelle de la fenêtre : un afficheur toujours pris
    /// à la définition de l'écran rendrait l'interface du jeu minuscule dans
    /// une petite fenêtre. La définition suit donc la fenêtre, par paliers,
    /// puisqu'elle est figée pour toute la session.
    ///
    /// L'écran est celui où la fenêtre va réellement s'ouvrir, et non un écran
    /// de référence : une fenêtre laissée sur un second écran de forme
    /// différente naîtrait sinon mal formée.
    /// </summary>
    private ScrcpyOptions WithDisplayFor(
        ScrcpyOptions options,
        ScrcpyWindowPlacement? placement,
        StoredWindowRect? remembered)
    {
        if (placement is not { Height: > 0 } window
            || _windows.MonitorBoundsFor(remembered) is not { } screen)
        {
            return options;
        }

        var (width, height) = DisplayLadder.For(
            window.Height, screen.Width, screen.Height, _quality.MaximumDisplayHeight);

        return options with { VirtualDisplayWidth = width, VirtualDisplayHeight = height };
    }

    /// <summary>
    /// Relit l'ordre voulu et le donne au gestionnaire de sessions. Tout ce
    /// qui parcourt les sessions en hérite : l'ouverture, le placement et le
    /// cycle clavier.
    /// </summary>
    public async Task RefreshRanksAsync(CancellationToken cancellationToken = default)
    {
        _unmanaged = await _settings.GetUnmanagedKeysAsync(cancellationToken).ConfigureAwait(false);

        var ranks = await _settings.GetInstanceRanksAsync(cancellationToken).ConfigureAwait(false);

        _sessions.OrderKey = session =>
            ranks.TryGetValue(session.Target.Key, out var rank) ? rank : int.MaxValue;
    }

    /// <summary>
    /// Rappel du raccourci de changement de compte, tel qu'il est configuré au
    /// moment du lancement. Vide si l'utilisateur l'a retiré.
    /// </summary>
    private async Task<string?> BuildTitleHintAsync(CancellationToken cancellationToken)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);
        var next = hotkeys.For(HotkeyAction.NextInstance);

        return next is { IsAssigned: true } ? $"{next.DisplayText} : fenêtre suivante" : null;
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

        await RefreshRanksAsync(cancellationToken).ConfigureAwait(false);

        _quality = QualityProfile.For(settings.Quality);
        _windows.Anchor = settings.GameAnchor;
        _windows.Presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(false);

        // La liste vide ne sert qu'à replacer l'état de taille sans toucher
        // aux fenêtres, qui seront placées ensuite.
        await _windows.ApplySizeAsync([], settings.SizeIndex, cancellationToken).ConfigureAwait(false);

        if (settings.CustomSizePercent > 0)
        {
            await _windows.ApplyPercentAsync([], settings.CustomSizePercent, cancellationToken)
                .ConfigureAwait(false);
        }
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
                    _windows.FocusNext(ManagedSessions);
                    break;

                case HotkeyAction.PreviousInstance:
                    _windows.FocusPrevious(ManagedSessions);
                    break;

                case HotkeyAction.Rearrange:
                    await StackOnActiveAsync().ConfigureAwait(false);
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
                    await ApplySizeAsync(_windows.Presets.FullscreenIndex).ConfigureAwait(false);
                    break;

                case HotkeyAction.Quit:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ouverture de {instance} refusée.{newLine}Commande : {commandLine}{newLine}Sortie de scrcpy :{newLine}{output}")]
    private partial void LogSessionFailure(string instance, string commandLine, string output, string newLine = "\n");

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} refusée en {height} de haut : nouvel essai à la définition de repli.")]
    private partial void LogDisplayFallback(string instance, int height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Écrans : {monitors}. Fenêtres de jeu : {placement}.")]
    private partial void LogPlacement(string monitors, string placement);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Relance courte de {instance} impossible ({reason}) : la fenêtre est rouverte.")]
    private partial void LogRestartFallback(string instance, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Un raccourci n'a pas pu être traité.")]
    private partial void LogHotkeyFailure(Exception exception);


    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La session {instance} s'est terminée seule ({state}) : {message}\n{output}")]
    private partial void LogSessionEnded(string instance, string state, string message, string output);
}
