using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Guidance;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
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
    private readonly WindowPlacements _placements;
    private readonly ILogger<GameLauncher> _logger;

    private bool _hotkeysWired;

    /// <summary>Un seul rouvrement à la fois : deux qui se chevauchent se volent leurs sessions.</summary>
    private readonly SemaphoreSlim _reopening = new(1, 1);

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
        WindowPlacements placements,
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
        _placements = placements;
        _logger = logger;

        // Une session qui meurt après son ouverture ne laissait aucune trace :
        // la fenêtre disparaissait et le journal restait muet.
        _sessions.SessionChanged += OnSessionChanged;

        // L'arrêt du jeu à la fermeture se décide au moment de fermer, pas au
        // lancement : sans cet abonnement, décocher la case en cours de partie
        // n'aurait rien changé avant le lancement suivant, et la fenêtre qu'on
        // ferme juste après aurait quand même tué le jeu.
        _settings.Changed += (_, document) => _sessions.StopAppOnClose = document.StopAppOnClose;

        // La fenêtre est placée avant l'ouverture du jeu, pour qu'il naisse à
        // la taille définitive.
        _sessions.PrepareWindow = async (session, placement, cancellationToken) =>
        {
            // Le coin visé est celui du cadre, alors que le placement transmis
            // à scrcpy est celui de la zone client : reposer la fenêtre sur les
            // coordonnées du placement la décalait d'une bordure et d'une barre
            // de titre, sous les yeux, juste après son ouverture.
            //
            // Sa taille n'est pas touchée : l'afficheur est déjà né à la bonne,
            // et le jeu fige la hauteur de sa mise en page à son initialisation.
            if (placement is { } wanted)
            {
                var frame = _windows.WindowChrome();

                await _windows
                    .MoveOnlyAsync(session, wanted.X - frame.Left, wanted.Y - frame.Top, cancellationToken)
                    .ConfigureAwait(false);
            }
        };

        _sessions.RequestClose = session => _windows.RequestClose(session);
    }

    /// <summary>Instances laissées de côté par les placements automatiques.</summary>
    private IReadOnlySet<string> _unmanaged = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Les comptes qui s'ouvrent dans le cadre à onglets.</summary>
    private HashSet<string> _tabbed = new(StringComparer.Ordinal);

    /// <summary>
    /// Le cadre à onglets, créé au premier compte qui en demande un.
    ///
    /// Paresseux : la plupart des sessions n'en veulent pas, et une fenêtre
    /// vide ouverte pour rien se remarquerait.
    /// </summary>
    private Windows.TabbedGameWindow? _tabs;

    /// <summary>Réglages dérivés de la qualité choisie, relus à chaque lancement.</summary>
    private QualityProfile _quality = QualityProfile.For(StreamQuality.Medium);
    private GameZoom _zoom = GameZoom.Normal;

    /// <summary>
    /// Retard d'affichage qui convient à cette liaison, en millisecondes.
    ///
    /// Relu en même temps que le budget, et pour la même raison : les deux se
    /// déduisent de ce que l'appareil dit de sa liaison, et ni l'un ni l'autre
    /// ne change entre deux redimensionnements de fenêtre.
    /// </summary>
    private int _videoBufferMs = VideoBuffer.None;

    /// <summary>Rythme des contrôles et des sondages, selon la qualité.</summary>
    public QualityProfile Quality => _quality;

    /// <summary>
    /// Les instances déjà lancées, pour savoir quoi rouvrir quand une fenêtre
    /// tombe. La session ne porte qu'une cible, pas l'instance d'origine.
    /// </summary>
    private readonly Dictionary<string, DofusInstance> _launched = new(StringComparer.Ordinal);

    /// <summary>Tentatives de reprise par instance, et heure de la dernière.</summary>
    private readonly Dictionary<string, (int Count, DateTimeOffset Last)> _recoveries =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Ce que la reprise a à dire, ou <c>null</c>. Rejoint le même bandeau que
    /// la chaleur : c'est là qu'on regarde quand quelque chose cloche.
    /// </summary>
    public string? RecoveryNotice { get; private set; }

    /// <summary>
    /// Signalé quand une fenêtre perdue doit être rouverte.
    ///
    /// La fenêtre est reconstruite par l'application, pas ici : ce service ne
    /// vit pas sur le fil de l'interface, et la mort d'une session est
    /// annoncée depuis la boucle de lecture de scrcpy.
    /// </summary>
    public event EventHandler<RecoveryRequest>? RecoveryRequested;


    /// <summary>
    /// Journalise la mort d'une session, avec la sortie de scrcpy. Sans cela,
    /// une fenêtre qui se ferme d'elle-même est indiagnosticable.
    /// </summary>
    private void OnSessionChanged(object? sender, ScrcpySession session)
    {
        // La fenêtre est revenue : l'avis de reprise n'a plus lieu d'être. Le
        // compte des tentatives, lui, survit, et c'est voulu : une liaison qui
        // clignote doit finir par épuiser son crédit.
        if (session.State == ScrcpySessionState.Running)
        {
            RecoveryNotice = null;
        }

        if (session.IsAlive || _closing)
        {
            return;
        }

        LogSessionEnded(
            session.Target.DisplayName,
            session.State.ToString(),
            session.FailureMessage ?? "aucun message",
            string.Join(Environment.NewLine, session.RecentOutput));

        // Avant de conclure qu'il ne reste rien : une fenêtre qu'on va rouvrir
        // n'est pas une fenêtre perdue. Sans cette réserve, un hoquet Wi-Fi sur
        // la dernière session fermait l'application.
        if (TryRecover(session))
        {
            return;
        }

        if (_sessions.ActiveSessions.Count == 0)
        {
            LastWindowClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Décide s'il faut rouvrir cette fenêtre, et le demande le cas échéant.
    ///
    /// La décision elle-même vit dans <see cref="SessionRecovery" />, où elle
    /// s'éprouve. Ici on ne tient que le compte des tentatives, et l'oubli au
    /// bout d'un moment sans rechute.
    /// </summary>
    private bool TryRecover(ScrcpySession session)
    {
        var key = session.Target.Key;

        if (!_launched.TryGetValue(key, out var instance))
        {
            return false;
        }

        var already = _recoveries.TryGetValue(key, out var seen)
            && DateTimeOffset.UtcNow - seen.Last < SessionRecovery.Forget
                ? seen.Count
                : 0;

        var decision = SessionRecovery.Decide(session.End, already);

        if (!decision.Retry)
        {
            // On ne se tait que si l'on n'avait rien promis. Après des
            // tentatives annoncées, renoncer sans le dire laisserait le
            // lecteur attendre une fenêtre qui ne reviendra pas.
            if (already > 0 && SessionRecovery.Recoverable(session.End.Failure))
            {
                RecoveryNotice = Strings.Format("SessionRecoveryGaveUp", instance.DisplayName);
                LogRecoveryGaveUp(instance.DisplayName, already);
            }

            return false;
        }

        _recoveries[key] = (already + 1, DateTimeOffset.UtcNow);

        RecoveryNotice = Strings.Format("SessionRecovering", instance.DisplayName);

        LogRecovering(instance.DisplayName, already + 1, (int)decision.Delay.TotalSeconds);

        RecoveryRequested?.Invoke(this, new RecoveryRequest(instance, decision.Delay));

        return true;
    }

    /// <summary>Sessions actuellement ouvertes.</summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions => _sessions.ActiveSessions;

    /// <summary>Vrai si une ouverture est en cours sur ce téléphone.</summary>
    public bool IsDeviceBusy(string deviceId) => _sessions.IsDeviceBusy(deviceId);

    /// <summary>Signalé quand un appareil devient occupé, ou cesse de l'être.</summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? DeviceBusyChanged
    {
        add => _sessions.DeviceBusyChanged += value;
        remove => _sessions.DeviceBusyChanged -= value;
    }

    /// <summary>
    /// Fenêtres qui suivent les placements automatiques.
    ///
    /// Une fenêtre décochée dans la liste reste où elle est : le parcours au
    /// clavier, le replacement, le côte à côte et les tailles l'ignorent. Elle
    /// s'ouvre, se ferme et se souvient de sa place comme les autres.
    /// </summary>
    public IReadOnlyList<ScrcpySession> ManagedSessions =>
        [.. _sessions.ActiveSessions.Where(
            s => !_unmanaged.Contains(s.Target.Key) && !_tabbed.Contains(s.Target.Key))];

    /// <summary>
    /// Comptes réellement logés dans le cadre à onglets.
    ///
    /// Compté sur les sessions ouvertes et non sur le réglage : un compte peut
    /// être marqué logé et n'avoir jamais pu être arrimé.
    /// </summary>
    public int HousedCount =>
        _sessions.ActiveSessions.Count(s => _tabbed.Contains(s.Target.Key));

    /// <summary>
    /// Vrai si le cadre à onglets suit les commandes de géométrie.
    ///
    /// Un seul compte logé verrouillé le fige : le cadre est une seule fenêtre,
    /// et on ne peut pas en immobiliser un onglet tout en déplaçant l'autre.
    /// </summary>
    public bool FrameMoves => _tabs is not null && !FrameLock.Freezes(_unmanaged, _tabbed);

    /// <summary>
    /// Nombre de fenêtres qu'un rangement peut bouger, le cadre comptant pour
    /// une. En dessous de deux, il n'y a rien à ranger et les boutons du pied
    /// de fenêtre se retirent.
    /// </summary>
    public int ArrangeableCount =>
        ManagedSessions.Count + (HousedCount > 0 && FrameMoves ? 1 : 0);

    /// <summary>Le cadre, quand il est posé, visible, et qu'aucun cadenas ne le fige.</summary>
    private Windows.TabbedGameWindow? MovableFrame =>
        _tabs is { Handle: not 0 } frame && FrameMoves ? frame : null;

    /// <summary>
    /// Sérialise les commandes de géométrie.
    ///
    /// Elles arrivent du fil des raccourcis, sautent sur celui de l'interface
    /// et écrivent les réglages en repassant : deux Ctrl+5 rapprochés
    /// entrelaçaient deux entrées en plein écran, et le cadre perdait le
    /// rectangle d'où il venait.
    /// </summary>
    private readonly SemaphoreSlim _arranging = new(1, 1);

    /// <summary>
    /// Donne au cadre la taille en cours, ou le plein écran, sans le déplacer.
    ///
    /// Rien n'est jamais appliqué à la fenêtre logée : elle est fille du cadre,
    /// et c'est le cadre qu'on dimensionne. Sa forme, elle, suit l'onglet
    /// montré : la taille du cadre est donc en pratique une part de largeur, la
    /// hauteur venant du rapport de l'image.
    /// </summary>
    private Task ResizeFrameAsync(bool fullscreen) =>
        MovableFrame is not { } frame
            ? Task.CompletedTask
            : OnUiAsync(() =>
            {
                if (fullscreen)
                {
                    if (_windows.ScreenBoundsFor(frame.Handle) is { } bounds)
                    {
                        frame.SetFullscreen(true, bounds);
                    }

                    return;
                }

                // Le rectangle d'avant est retenu par le cadre lui-même.
                frame.SetFullscreen(false, default);

                if (frame.Chassis is { } chassis
                    && _windows.ResizedRect(frame.Handle, frame.SelectedAspect ?? 0, chassis)
                        is { } rect)
                {
                    frame.ApplyRect(rect);
                }
            });

    /// <summary>Pose le cadre sur un rectangle imposé, celui d'un empilement.</summary>
    private Task StackFrameAsync(ScreenRect rect) =>
        MovableFrame is not { } frame
            ? Task.CompletedTask
            : OnUiAsync(() =>
            {
                frame.SetFullscreen(false, default);
                frame.ApplyRect(rect);
            });

    /// <summary>
    /// Signalé quand l'ensemble des fenêtres que les placements peuvent ranger
    /// a pu changer : une mise de côté, une entrée ou une sortie du cadre à
    /// onglets, un réordonnancement.
    ///
    /// L'ouverture et la fermeture passent par <see cref="SessionChanged"/> ;
    /// celui-ci couvre ce qui change sans qu'aucune session ne bouge.
    /// </summary>
    public event EventHandler? ArrangeableChanged;

    /// <summary>Signalé à chaque changement d'état d'une session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged
    {
        add => _sessions.SessionChanged += value;
        remove => _sessions.SessionChanged -= value;
    }

    /// <summary>Demandé par le raccourci d'affichage du configurateur.</summary>
    public event EventHandler? ConfiguratorToggleRequested;

    /// <summary>Le raccourci du suivi de quêtes a été pressé.</summary>
    public event EventHandler? QuestsToggleRequested;

    /// <summary>Demandé par le raccourci de l'Almanax.</summary>
    public event EventHandler? AlmanaxRequested;

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

        // Les comptes disparus s'oublient avant la fusion : sinon leur entrée
        // mémorisée reparaîtrait dans le résultat, et la liste garderait un
        // compte qui n'existe plus nulle part. Deux disparitions comptent, le
        // profil retiré du téléphone et le jeu désinstallé d'un profil qui
        // reste.
        var forgotten = await _settings
            .ForgetMissingProfilesAsync(
                _instances.ScannedProfiles,
                _instances.ProfilesWithoutGame,
                cancellationToken)
            .ConfigureAwait(false);

        if (forgotten > 0)
        {
            LogProfilesForgotten(forgotten);
        }

        return await _settings.MergeInstancesAsync(found, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Incidents non bloquants du dernier balayage d'instances, à joindre à
    /// ceux de la découverte d'appareils.
    /// </summary>
    public IReadOnlyList<string> InstanceWarnings => _instances.Warnings;

    /// <summary>
    /// Téléphones vus maintenant. Les appareils déjà associés sont reconnectés
    /// au passage : un téléphone qui s'annonce sur le réseau n'a pas à être
    /// réassocié à la main.
    /// </summary>
    public async Task<DeviceDiscoveryResult> RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureKnownDevicesConnectedAsync(cancellationToken).ConfigureAwait(false);

        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        await RefreshHeatAsync(cancellationToken).ConfigureAwait(false);

        return discovery;
    }

    /// <summary>
    /// Ce que les appareils qui portent une fenêtre disent de leur chaleur, ou
    /// <c>null</c> s'ils n'ont rien à en dire.
    ///
    /// C'est la limite qui mord en premier sur une tablette à plusieurs
    /// comptes, et elle est silencieuse : rien n'échoue, tout ralentit.
    /// </summary>
    public string? HeatWarning { get; private set; }

    /// <summary>Dernier état thermique journalisé par appareil.</summary>
    private readonly Dictionary<string, int> _loggedHeat = new(StringComparer.Ordinal);

    /// <summary>
    /// Relit la chaleur des appareils qui portent une session.
    ///
    /// Seulement ceux-là, et seulement quand une fenêtre est ouverte : un
    /// téléphone posé sur la table ne chauffe pas, et l'interroger pour rien
    /// coûterait un aller-retour de shell à chaque balayage. La lecture est
    /// gardée une minute par la découverte, ce qui borne le coût même quand le
    /// panneau sonde toutes les deux secondes.
    /// </summary>
    private async Task RefreshHeatAsync(CancellationToken cancellationToken)
    {
        var serials = _sessions.ActiveSessions
            .Where(s => s.IsAlive)
            .Select(s => s.Target.Serial)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (serials.Count == 0)
        {
            HeatWarning = null;
            _loggedHeat.Clear();
            return;
        }

        string? warning = null;

        foreach (var serial in serials)
        {
            var reading = await _devices.GetThermalAsync(serial, cancellationToken).ConfigureAwait(false);

            if (reading?.Describe() is not { } said)
            {
                _ = _loggedHeat.Remove(serial);
                continue;
            }

            // Journalisé au changement d'état, non à chaque relecture : le
            // panneau sonde jusqu'à deux fois par seconde, et la même ligne
            // répétée trois cents fois en dix minutes noierait le reste.
            if (!_loggedHeat.TryGetValue(serial, out var already) || already != reading.Status)
            {
                _loggedHeat[serial] = reading.Status;
                LogHeat(serial, reading.Status, reading.SkinCelsius ?? 0);
            }

            // Le plus chaud parle pour tous : deux avertissements côte à côte
            // dans le même bandeau se liraient comme un seul, plus long.
            if (warning is null || reading.Status >= ThermalReading.Severe)
            {
                warning = said;
            }
        }

        HeatWarning = warning;
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
            // déjà associé à ce PC : ADB refuse les autres. Encore faut-il ne
            // pas reprendre celui dont on vient de rompre l'association, qui
            // s'annonce toujours et dont ADB garde la clé.
            var (known, discarded) = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

            var opened = await _pairing
                .ConnectAnnouncedAsync(addresses, discarded, cancellationToken)
                .ConfigureAwait(false);

            var recovered = opened.Count;

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
            return new LaunchReport(0, [Strings.Get("NoInstanceChecked")]);
        }

        return await LaunchAsync(enabled, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Ouvre une liste d'instances précise.</summary>
    public Task<LaunchReport> LaunchAsync(
        IReadOnlyList<DofusInstance> instances,
        CancellationToken cancellationToken = default) =>
        LaunchAsync(instances, remembered: null, cancellationToken);

    /// <summary>
    /// Ouvre une liste d'instances, éventuellement avec une géométrie relevée
    /// à l'instant plutôt que celle des réglages.
    /// </summary>
    private async Task<LaunchReport> LaunchAsync(
        IReadOnlyList<DofusInstance> instances,
        IReadOnlyDictionary<string, StoredWindowRect>? remembered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);

        // Le préambule est chronométré parce qu'il se voit : rien ne s'ouvre
        // pendant ce temps, et les boutons des autres comptes attendent. Sans
        // ces trois nombres, « ça bloque trop longtemps » ne se corrige qu'au
        // hasard.
        var preambule = System.Diagnostics.Stopwatch.StartNew();

        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);

        var raccourcis = preambule.ElapsedMilliseconds;

        // Les appareils sont résolus avant les réglages de fenêtre, et non
        // après : c'est d'eux qu'on tire la liaison, et c'est la liaison qui
        // borne la qualité que ces réglages vont poser.
        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        var appareils = preambule.ElapsedMilliseconds;

        await RefreshLinkAsync(instances, devices, cancellationToken).ConfigureAwait(false);

        var liaison = preambule.ElapsedMilliseconds;

        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var options = await _settings.GetScrcpyOptionsAsync(cancellationToken).ConfigureAwait(false);

        LogPreamble(
            raccourcis,
            appareils - raccourcis,
            liaison - appareils,
            preambule.ElapsedMilliseconds - liaison,
            preambule.ElapsedMilliseconds);

        options = options with
        {
            WindowTitleHint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false),
            IconDirectory = _iconDirectory,
            VideoBufferMs = _videoBufferMs,
        };

        // La position est donnée à scrcpy dès le lancement. Le faire après
        // coup ne suffit pas : scrcpy recentre sa fenêtre quand il reçoit la
        // première image, donc après notre placement.
        remembered ??= await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

        List<string> problems = [];
        List<ScrcpySession> started = [];

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Retenue avant tout filtre : c'est par elle qu'on saura quoi
            // rouvrir si la fenêtre tombe.
            _launched[instance.Key] = instance;

            if (IsOpen(instance))
            {
                continue;
            }

            if (!devices.TryGetValue(instance.DeviceId, out var device))
            {
                problems.Add(Strings.Format("PhoneNotConnectedNamed", instance.DisplayName));
                continue;
            }

            // Le niveau d'API est connu depuis la découverte. Le lire ici évite
            // d'attendre le délai complet de scrcpy pour un appareil dont on
            // sait déjà qu'il ne créera pas d'afficheur virtuel.
            if (AndroidRequirements.DescribeVirtualDisplayShortfall(
                    device.SdkVersion, device.AndroidVersion) is { } shortfall)
            {
                problems.Add($"{instance.DisplayName} : {shortfall}");
                LogAndroidTooOld(instance.DisplayName, device.SdkVersion ?? 0);
                continue;
            }

            remembered.TryGetValue(instance.Key, out var stored);

            var placement = ComputePlacement(options, stored);

            var target = ToTarget(instance, device.Serial);
            var display = WithDisplayFor(options, placement, stored);

            // Le son capté est celui du téléphone entier : Android ne sait pas
            // l'isoler par application. Une seule session par appareil le porte
            // donc, la première ouverte. L'accorder à toutes donnerait le même
            // flux en plusieurs exemplaires, c'est-à-dire un écho.
            if (display.AudioEnabled && HasOpenSessionOn(instance.DeviceId))
            {
                display = display with { AudioEnabled = false };
            }

            var session = await _sessions.StartAsync(
                target, display, placement, cancellationToken).ConfigureAwait(false);

            // Les encodeurs vidéo annoncent une définition maximale, variable
            // d'un appareil à l'autre : une tablette modeste peut plafonner à
            // 1280x720 là où un téléphone récent monte en 8K. Plutôt que de
            // renoncer, on redescend les paliers de repli.
            //
            // Seulement pour les refus qu'une définition plus modeste peut
            // réparer : un téléphone débranché le restera, et chaque tentative
            // coûte l'attente complète.
            while (session.State == ScrcpySessionState.Failed
                && ScrcpyOutputParser.CanRetrySmaller(session.FailureKind)
                && DisplayLadder.Below(
                       display.VirtualDisplayHeight,
                       display.VirtualDisplayWidth,
                       display.VirtualDisplayHeight) is { } smaller)
            {
                LogDisplayFallback(instance.DisplayName, display.VirtualDisplayHeight, smaller.Height);

                display = display with
                {
                    VirtualDisplayWidth = smaller.Width,
                    VirtualDisplayHeight = smaller.Height,
                    VirtualDisplayDpi = ZoomProfile.DpiFor(smaller.Height, _zoom),
                    VideoBitrateKbps = _quality.BitrateFor(smaller.Width, smaller.Height),
                };

                session = await _sessions.StartAsync(
                    target, display, placement, cancellationToken).ConfigureAwait(false);
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

            LogStartupTiming(instance.DisplayName, session.DisplayReadyMs, session.StartupMs);

            // La définition et le débit retenus : ils dépendent de la fenêtre,
            // de l'écran et du palier de qualité, et se lisaient jusqu'ici
            // nulle part. C'est aussi ce qui permet de vérifier que le débit
            // suit bien la définition.
            LogStreamSettings(
                instance.DisplayName,
                display.VirtualDisplayWidth,
                display.VirtualDisplayHeight,
                display.VirtualDisplayDpi,
                display.MaxFps,
                display.VideoBitrateKbps);

            started.Add(session);
        }

        // Seules les fenêtres qui viennent d'ouvrir sont placées. Replacer les
        // autres les arracherait à l'endroit où l'utilisateur les a mises, et
        // ferait recréer leur afficheur virtuel côté Android.
        if (started.Count > 0)
        {
            await _windows.RestoreAsync(started, remembered, cancellationToken).ConfigureAwait(false);

            // Les comptes à onglets rejoignent le cadre. Après le placement :
            // arrimer d'abord ferait replacer une fenêtre déjà logée, qui
            // sauterait hors du cadre le temps d'y revenir.
            foreach (var session in started.Where(s => _tabbed.Contains(s.Target.Key)))
            {
                await AttachToTabsAsync(session, cancellationToken).ConfigureAwait(false);
            }

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

        // L'onglet part avec la session. Le laisser faisait croire au cadre
        // que le compte y était encore, et rouvrir ce compte ne le relogeait
        // plus : il reparaissait en fenêtre libre par-dessus le cadre.
        if (_tabs is not null)
        {
            await OnUiAsync(() => _tabs?.Detach(session.Target.Key)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ajoute un compte sur un téléphone : un profil Android neuf, le jeu
    /// dedans, prêt à ouvrir.
    ///
    /// Le message de refus se complète ici, où l'on sait de quelle marque est
    /// l'appareil : quand la surcouche interdit la création, dire « passez par
    /// vos réglages » sans dire où n'avance à rien.
    /// </summary>
    public async Task<AccountAddition> AddAccountAsync(
        string deviceId,
        string name,
        CancellationToken cancellationToken = default)
    {
        // L'état vivant, et non celui du registre : le registre garde le
        // dernier état écrit, qui vaut souvent « hors ligne » alors que le
        // téléphone répond. Le bouton refusait ainsi de travailler sur un
        // appareil que la liste montrait pourtant connecté.
        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        if (!devices.TryGetValue(deviceId, out var device))
        {
            return new AccountAddition(false, Strings.Get("PhoneNotConnected"));
        }

        var result = await _instances
            .AddAccountAsync(device.Serial, name, cancellationToken)
            .ConfigureAwait(false);

        LogAccountAdded(device.DisplayName, name, result.Succeeded, result.Message);

        if (result.Succeeded)
        {
            return result;
        }

        var brand = PhoneBrands.FromManufacturer(device.Manufacturer);

        return result with
        {
            // Par une ressource : la phrase était écrite en français dans le
            // code, si bien qu'une interface en anglais ou en espagnol rendait
            // un message traduit suivi d'une phrase qui ne l'était pas.
            Message = result.Message + Strings.Format("OnBrandUseClonePath", brand.Name, brand.ClonePath),
        };
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

        // ADB garde ses connexions ouvertes, et le balayage suivant reverrait
        // donc le téléphone comme n'importe quel autre. L'appareil est relu
        // avant l'écart, puisque l'écart efface l'entrée.
        var known = await _registry.GetKnownAsync(cancellationToken).ConfigureAwait(false);
        var device = known.FirstOrDefault(
            d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));

        if (device is not null)
        {
            try
            {
                await _devices.DisconnectDeviceAsync(device, cancellationToken).ConfigureAwait(false);
            }
            catch (AdbException exception)
            {
                // Un appareil déjà parti n'a pas à faire échouer une rupture :
                // le reste du nettoyage compte davantage que cette coupure.
                LogDisconnectFailed(device.DisplayName, exception.UserMessage);
            }
        }

        await _registry.DiscardAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Referme puis rouvre les fenêtres ouvertes, à leur place et à leur
    /// taille.
    ///
    /// La qualité et le zoom sont des arguments de démarrage de scrcpy, figés
    /// pour toute la durée d'une session : les changer ne se voyait nulle part
    /// tant qu'on n'avait pas tout refermé à la main. La géométrie est relevée
    /// avant la fermeture, si bien que chaque fenêtre revient là où elle était.
    /// </summary>
    public async Task<LaunchReport> ReopenAsync(CancellationToken cancellationToken = default)
    {
        // Deux rouvrements qui se chevauchent se volaient leurs sessions : le
        // second n'en voyait plus qu'une, refermait celle-là, et rouvrait tout
        // au coin par défaut faute d'avoir relevé quoi que ce soit.
        await _reopening.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var instances = _sessions.ActiveSessions
                .Select(s => s.Target)
                .ToList();

            if (instances.Count == 0)
            {
                return new LaunchReport(0, []);
            }

            // La géométrie est gardée sous la main et passée telle quelle au
            // lancement, plutôt que relue depuis les réglages : un relevé
            // vide retombait sinon en silence sur le placement par défaut, et
            // toutes les fenêtres se retrouvaient empilées au même endroit.
            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            var remembered = await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

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

            // Les cibles portent l'identité d'une session, pas d'une instance :
            // c'est la liste à jour des instances qui sait ce qu'il faut rouvrir.
            var known = await RefreshInstancesAsync(cancellationToken).ConfigureAwait(false);

            var reopen = known
                .Where(i => instances.Any(t => string.Equals(t.Key, i.Key, StringComparison.Ordinal)))
                .ToList();

            var missing = reopen
                .Where(i => !remembered.ContainsKey(i.Key))
                .Select(i => i.DisplayName)
                .ToList();

            if (missing.Count > 0)
            {
                // Sans ce relevé, la fenêtre rouvre au coin par défaut : autant
                // le dire au journal plutôt que de laisser chercher.
                LogMissingGeometry(string.Join(", ", missing));
            }

            return await LaunchAsync(reopen, remembered, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reopening.Release();
        }
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

        // Voir StopSessionAsync : le cadre ne doit pas garder d'onglet sur une
        // fenêtre qui n'existe plus. Ici l'ouverture d'un profil enchaîne
        // aussitôt, et attendre le prochain entretien serait trop tard.
        if (_tabs is not null)
        {
            await OnUiAsync(() => _tabs?.DetachAll()).ConfigureAwait(false);
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
        await CaptureTabsPlacementAsync(cancellationToken).ConfigureAwait(false);

        // Les fenêtres logées sont écartées : leur rectangle est celui qu'elles
        // occupent dans le cadre, et le retenir comme place libre les faisait
        // reparaître au milieu de l'écran le jour où on les sortait des onglets.
        var captured = _windows.CaptureGeometries(
            [.. _sessions.ActiveSessions.Where(s => !_tabbed.Contains(s.Target.Key))]);

        if (captured.Count == 0)
        {
            return;
        }

        await _settings.SaveWindowRectsAsync(
            captured.ToDictionary(c => c.Key, c => c.Rect, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retient où est le cadre à onglets.
    ///
    /// À part des fenêtres de jeu : une fenêtre logée n'a plus de place à
    /// elle, et c'est celle du cadre qui compte. La lecture se fait sur le fil
    /// d'interface, la poignée d'une fenêtre WPF n'étant pas lisible ailleurs.
    /// </summary>
    private async Task CaptureTabsPlacementAsync(CancellationToken cancellationToken)
    {
        if (_tabs is not { } frame || frame.IsFullscreen)
        {
            // En plein écran, le rectangle est celui de l'écran entier :
            // l'enregistrer ferait rouvrir le cadre couvrant tout, sans plus
            // rien qui dise d'où il venait.
            return;
        }

        WindowPlacement? place = null;

        await OnUiAsync(() => place = _windows.Controller.GetPlacement(frame.Handle))
            .ConfigureAwait(false);

        await _settings
            .SetWindowPlacementAsync(WindowPlacements.Tabs, place, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Entretien périodique : corrige la forme des fenêtres et retient
    /// laquelle est au premier plan.
    /// </summary>
    public void Watch()
    {
        // Le suivi regarde toutes les fenêtres : savoir laquelle est active
        // vaut aussi pour celles qui sont logées dans le cadre.
        _windows.TrackActiveWindow(_sessions.ActiveSessions);

        // Le rapport d'image, lui, ne concerne que les fenêtres libres. Sur une
        // fenêtre logée, il défaisait la pose du cadre toutes les demi-secondes,
        // et le jeu revenait se coller de travers sans qu'on comprenne pourquoi.
        _windows.EnforceAspect(ManagedSessions);

        // Filet pour les sessions qui meurent sans passer par nous : fenêtre du
        // jeu fermée à la main, téléphone débranché. Les fermetures voulues
        // détachent déjà l'onglet elles-mêmes, sans attendre ce passage.
        _tabs?.KeepOnly(
            [.. _sessions.ActiveSessions.Select(s => s.Target.Key)]);
    }


    /// <summary>
    /// Range les fenêtres côte à côte, la fenêtre active à droite.
    /// </summary>
    public async Task<int> TileAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Le cadre compte pour une fenêtre. Au premier plan, il prend la
            // droite et les fenêtres libres passent toutes à gauche : sans
            // cela, l'une d'elles se poserait à droite par-dessus lui.
            var frame = ArrangeableCount > 1 ? MovableFrame : null;
            var frameFirst = frame is not null
                && _windows.Controller.GetForegroundWindow() == frame.Handle;

            var placed = await _windows
                .TileAsync(ManagedSessions, frameFirst, cancellationToken)
                .ConfigureAwait(false);

            if (frame is not null)
            {
                await TileFrameAsync(frame, frameFirst).ConfigureAwait(false);
                placed++;
            }

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            return placed;
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    /// <summary>Donne au cadre sa moitié d'écran.</summary>
    private Task TileFrameAsync(Windows.TabbedGameWindow frame, bool onRight) =>
        OnUiAsync(() =>
        {
            frame.SetFullscreen(false, default);

            if (frame.Chassis is { } chassis && _windows.WorkAreaFor(frame.Handle) is { } work)
            {
                frame.ApplyRect(
                    TileLayout.Half(work, onRight, frame.SelectedAspect ?? 0, chassis));
            }
        });

    /// <summary>
    /// Empile les fenêtres sur celle qui est active, ou sur la première.
    /// C'est ce que fait le raccourci de replacement, et le bouton de la barre
    /// du bas : on place une fenêtre où on la veut, les autres la rejoignent.
    /// </summary>
    public async Task<int> StackOnActiveAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var frame = MovableFrame;
            var sessions = ManagedSessions;

            // Le cadre au premier plan sert de référence : on ne déplace pas ce
            // qu'on regarde pour aligner tout le reste ailleurs.
            var vu = frame is not null
                && _windows.Controller.GetForegroundWindow() == frame.Handle
                    ? _windows.Controller.GetWindowRect(frame.Handle)
                    : null;

            if (vu is { IsEmpty: false } depuisLeCadre)
            {
                var pris = await _windows
                    .StackOnAsync(sessions, depuisLeCadre, null, cancellationToken)
                    .ConfigureAwait(false);

                await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

                return pris;
            }

            if (await _windows.StackTargetAsync(sessions, cancellationToken).ConfigureAwait(false)
                is not { } cible)
            {
                return 0;
            }

            var moved = await _windows
                .StackOnAsync(sessions, cible.Rect, cible.Reference, cancellationToken)
                .ConfigureAwait(false);

            if (frame is not null)
            {
                await StackFrameAsync(cible.Rect).ConfigureAwait(false);
                moved++;
            }

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

            return moved;
        }
        finally
        {
            _ = _arranging.Release();
        }
    }


    /// <summary>Remet toutes les fenêtres en place, à la taille en cours.</summary>
    public async Task<int> ArrangeAsync(CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ArrangeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    private async Task<int> ArrangeCoreAsync(CancellationToken cancellationToken)
    {
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var moved = await _windows.ArrangeAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        if (MovableFrame is { } frame)
        {
            await OnUiAsync(() =>
            {
                frame.SetFullscreen(false, default);

                if (frame.Chassis is { } chassis
                    && _windows.AnchoredRect(frame.Handle, frame.SelectedAspect ?? 0, chassis)
                        is { } rect)
                {
                    frame.ApplyRect(rect);
                }
            }).ConfigureAwait(false);

            moved++;
        }

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
            // Le cadre ne suit qu'au relâchement : à chaque cran, il faudrait
            // un aller-retour par le fil d'interface pour redimensionner une
            // fenêtre WPF, et le geste deviendrait saccadé.
            await ResizeFrameAsync(fullscreen: false).ConfigureAwait(false);

            await _settings.SaveCustomSizePercentAsync(percent, cancellationToken).ConfigureAwait(false);

            await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);
        }

        return moved;
    }

    /// <summary>Applique une taille à toutes les fenêtres et la retient.</summary>
    public async Task<int> ApplySizeAsync(int sizeIndex, CancellationToken cancellationToken = default)
    {
        await _arranging.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ApplySizeCoreAsync(sizeIndex, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _arranging.Release();
        }
    }

    private async Task<int> ApplySizeCoreAsync(int sizeIndex, CancellationToken cancellationToken)
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

        await ResizeFrameAsync(sizeIndex >= _windows.Presets.FullscreenIndex).ConfigureAwait(false);

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

        // Le rectangle transmis est celui de la zone client, cadre déduit et
        // coin décalé : scrcpy dimensionne **et positionne** sa fenêtre par
        // l'intérieur. Lui donner le rectangle extérieur la faisait naître trop
        // grande d'une barre de titre, puis, la taille corrigée, naître une
        // bordure trop à gauche et une barre de titre trop haut. Le placement
        // qui suit la recalait alors sous les yeux, deux cents millisecondes
        // après son ouverture.
        var client = _windows.WindowChrome().ClientOf(value);

        return new ScrcpyWindowPlacement(client.X, client.Y, client.Width, client.Height);
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

        // La densité se déduit de la définition retenue : la laisser fixe
        // faisait varier le zoom du jeu avec la taille de la fenêtre, puisque
        // la définition, elle, la suit.
        //
        // Le débit s'en déduit aussi, et pour la même raison : un débit fixe
        // servait grassement une petite fenêtre et affamait une grande.
        return options with
        {
            VirtualDisplayWidth = width,
            VirtualDisplayHeight = height,
            VirtualDisplayDpi = ZoomProfile.DpiFor(height, _zoom),
            VideoBitrateKbps = _quality.BitrateFor(width, height),
        };
    }

    /// <summary>
    /// Fait suivre l'ordre de la liste à l'ordre des fenêtres, Alt+Tab compris.
    ///
    /// Toutes les fenêtres y passent, verrouillées comprises : le verrou porte
    /// sur la position, pas sur le rang.
    /// </summary>
    public Task<int> ApplyWindowOrderAsync(CancellationToken cancellationToken = default) =>
        _windows.ApplyOrderAsync(_sessions.ActiveSessions, cancellationToken);

    /// <summary>
    /// Relit l'ordre voulu et le donne au gestionnaire de sessions. Tout ce
    /// qui parcourt les sessions en hérite : l'ouverture, le placement et le
    /// cycle clavier.
    /// </summary>
    public async Task RefreshRanksAsync(CancellationToken cancellationToken = default)
    {
        _unmanaged = await _settings.GetUnmanagedKeysAsync(cancellationToken).ConfigureAwait(false);

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        _tabbed = settings.Instances
            .Where(i => i.IsTabbed)
            .Select(i => i.Key)
            .ToHashSet(StringComparer.Ordinal);

        var ranks = await _settings.GetInstanceRanksAsync(cancellationToken).ConfigureAwait(false);

        _sessions.OrderKey = session =>
            ranks.TryGetValue(session.Target.Key, out var rank) ? rank : int.MaxValue;

        // Après la relecture, non avant : ce qui écoute lira alors le bon
        // ensemble. C'est ici que « mise de côté » et « logée en onglet »
        // prennent effet pour tout le reste.
        ArrangeableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rappel du raccourci de changement de compte, tel qu'il est configuré au
    /// moment du lancement. Vide si l'utilisateur l'a retiré.
    /// </summary>
    private async Task<string?> BuildTitleHintAsync(CancellationToken cancellationToken)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(false);

        // Le configurateur d'abord : c'est le seul rappel dont on a besoin
        // quand on ne sait plus comment revenir à l'application. Il n'a pas
        // d'icône dans la barre des tâches une fois masqué, et sans ce rappel
        // la combinaison ne s'apprend nulle part.
        List<string> parts = [];

        if (hotkeys.For(HotkeyAction.ToggleConfigurator) is { IsAssigned: true } toggle)
        {
            parts.Add(Strings.Format("TitleHintSettings", toggle.DisplayText));
        }

        if (hotkeys.For(HotkeyAction.NextInstance) is { IsAssigned: true } next)
        {
            parts.Add(Strings.Format("TitleHintNextWindow", next.DisplayText));
        }

        return parts.Count > 0 ? string.Join("  ·  ", parts) : null;
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

    /// <summary>
    /// Relit ce que vaut la liaison, pour en déduire le tampon d'affichage.
    ///
    /// Une fois par lancement, et jamais pendant un geste de géométrie : le
    /// chiffre ne bouge pas entre deux redimensionnements, alors que l'appel
    /// coûterait une demi-seconde à chaque fois.
    ///
    /// Toutes les sessions d'un lancement passent par le même téléphone : le
    /// premier trouvé suffit à connaître la liaison, et interroger les autres
    /// rendrait la même réponse.
    /// </summary>
    private async Task RefreshLinkAsync(
        IReadOnlyList<DofusInstance> instances,
        Dictionary<string, AndroidDevice> devices,
        CancellationToken cancellationToken)
    {
        var serial = instances
            .Select(i => devices.TryGetValue(i.DeviceId, out var device) ? device.Serial : null)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        var link = string.IsNullOrWhiteSpace(serial)
            ? null
            : await _devices.GetWifiLinkAsync(serial, cancellationToken).ConfigureAwait(false);

        _videoBufferMs = VideoBuffer.MillisecondsFor(link);

        if (link is null)
        {
            LogUnknownLink();
            return;
        }

        LogLink(
            link.Standard,
            link.FrequencyMhz,
            link.LinkSpeedMbps,
            link.Rssi,
            Math.Round(link.RetryShare * 100, 1),
            _videoBufferMs);
    }

    /// <summary>
    /// Appareils connectés, indexés par identifiant. On garde l'enregistrement
    /// entier et non le seul numéro de série : la version d'Android s'y trouve,
    /// et le lancement en a besoin.
    /// </summary>
    private async Task<Dictionary<string, AndroidDevice>> ResolveDevicesAsync(
        CancellationToken cancellationToken)
    {
        var discovery = await _devices.RefreshAsync(cancellationToken).ConfigureAwait(false);

        return discovery.Devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, d => d, StringComparer.Ordinal);
    }

    /// <summary>
    /// Loge une session dans le cadre à onglets, en le créant s'il le faut.
    ///
    /// La fenêtre doit exister : elle est cherchée comme pour un placement,
    /// avec la même attente. Une session dont la fenêtre n'est jamais apparue
    /// reste libre plutôt que d'être perdue.
    /// </summary>
    private async Task AttachToTabsAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        var handle = await _windows.ResolveWindowAsync(session, cancellationToken).ConfigureAwait(false);

        if (handle == 0)
        {
            LogTabWindowMissing(session.DisplayName);
            return;
        }

        // Sur le fil d'interface, et non celui qui nous a menés ici : une
        // fenêtre WPF ne se crée que sur un fil en mode STA, et la chaîne
        // asynchrone du lanceur n'en est pas un.
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        // L'ordre retenu est reposé après chaque arrivée, et non laissé à
        // celui des arrivées. Les afficheurs ne se préparent pas à la même
        // vitesse : d'un lancement à l'autre, les onglets sortaient dans un
        // ordre différent, et celui qu'on avait rangé à la souris ne tenait pas
        // d'une session à la suivante.
        var order = settings.Instances
            .OrderBy(i => i.Order)
            .Select(i => i.Key)
            .ToList();

        await OnUiAsync(() =>
        {
            var frame = EnsureTabs(settings);

            frame.Attach(
                session.Target.Key,
                session.DisplayName,
                IconFor(session),
                handle,
                session.SourceAspectRatio);

            frame.Reorder(order);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Exécute un geste d'interface sur le fil qui a le droit de le faire.
    ///
    /// Rend la main tout de suite s'il n'y a pas d'application WPF, ce qui est
    /// le cas des tests.
    /// </summary>
    private static Task OnUiAsync(Action geste)
    {
        var thread = System.Windows.Application.Current?.Dispatcher;

        if (thread is null || thread.CheckAccess())
        {
            geste();
            return Task.CompletedTask;
        }

        return thread.InvokeAsync(geste).Task;
    }

    /// <summary>Le cadre, créé au premier besoin et gardé ouvert ensuite.</summary>
    /// <summary>
    /// Ferme les comptes que le cadre à onglets logeait.
    ///
    /// Le compte se comporte comme si l'on avait fermé sa fenêtre une à une :
    /// sa place est retenue, et il ne rouvrira pas de lui-même au prochain
    /// démarrage. Il reste logé en onglets, si bien qu'il y retournera le jour
    /// où on le rouvrira.
    ///
    /// Une session qui survit à l'arrêt retrouve sa fenêtre visible : elle a
    /// quitté le cadre masquée, et la laisser ainsi la rendrait introuvable.
    /// </summary>
    private async Task CloseTabbedAsync(IReadOnlyList<string> keys)
    {
        List<string> fermes = [];

        foreach (var key in keys)
        {
            if (_sessions.ActiveSessions.FirstOrDefault(
                    s => string.Equals(s.Target.Key, key, StringComparison.Ordinal)) is not { } session)
            {
                continue;
            }

            await StopSessionAsync(session, CancellationToken.None).ConfigureAwait(false);

            if (session.IsAlive && session.WindowHandle != 0)
            {
                await OnUiAsync(() => _windows.Controller.SetVisible(session.WindowHandle, true))
                    .ConfigureAwait(false);

                continue;
            }

            fermes.Add(key);
        }

        if (fermes.Count > 0)
        {
            await _settings.SetInstancesEnabledAsync(fermes, enabled: false).ConfigureAwait(false);
        }
    }

    private Windows.TabbedGameWindow EnsureTabs(AppSettingsDocument document)
    {
        if (_tabs is { } existant)
        {
            return existant;
        }

        var frame = new Windows.TabbedGameWindow(_windows.Controller);

        frame.Closed += (_, _) => _tabs = null;

        // Fermer le cadre ferme les comptes qu'il logeait : c'est ce que le
        // geste annonce, et les voir se disperser en fenêtres libres était le
        // contraire de ce qu'on demandait.
        frame.CloseRequested += async (_, loges) =>
        {
            // Bornés, ces deux gestes-ci. Une faute s'échapperait d'une lambda
            // « async void » et atteindrait le garde-fou du répartiteur, qui
            // ouvrirait une fenêtre d'erreur pour un clic sur une croix ou un
            // glissement d'onglet. Le journal la retient, le geste échoue seul.
            try
            {
                await CloseTabbedAsync(loges).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogTabsFailure(exception);
            }
        };

        // Réordonner les onglets réordonne les comptes : un seul ordre partout.
        frame.Reordered += async (_, mouvement) =>
        {
            try
            {
                if (await _settings
                        .MoveInstanceAsync(mouvement.Moved, mouvement.Onto, mouvement.Before)
                        .ConfigureAwait(true))
                {
                    await RefreshRanksAsync().ConfigureAwait(true);

                    var order = await _settings.GetInstanceRanksAsync().ConfigureAwait(true);

                    frame.Reorder([.. order.OrderBy(p => p.Value).Select(p => p.Key)]);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogTabsFailure(exception);
            }
        };

        _tabs = frame;
        frame.Show();

        // Après l'affichage : il n'y a pas de poignée avant, donc rien à
        // placer. Le cadre reparaît ainsi là où on l'avait laissé, et un profil
        // en onglets le rouvre là où il était quand on l'a enregistré.
        _placements.Restore(frame, WindowPlacements.Tabs, document);

        return frame;
    }

    /// <summary>
    /// Fait entrer ou sortir un compte du cadre, sans rouvrir sa session.
    ///
    /// C'est la même fenêtre qu'on arrime ou détache : la rouvrir coûterait
    /// plusieurs secondes et ferait recréer son afficheur virtuel.
    /// </summary>
    public async Task SetTabbedAsync(
        DofusInstance instance,
        bool tabbed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        await _settings.SetInstanceTabbedAsync(instance.Key, tabbed, cancellationToken)
            .ConfigureAwait(false);

        await RefreshRanksAsync(cancellationToken).ConfigureAwait(false);

        if (FindSession(instance) is not { } session)
        {
            return;
        }

        if (tabbed)
        {
            await AttachToTabsAsync(session, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await OnUiAsync(() => _tabs?.Detach(instance.Key)).ConfigureAwait(false);
        }

        // Une seconde fois, et c'est la seule qui compte pour les touches de
        // rangement. La première est partie avant que le cadre n'existe : le
        // compte des fenêtres à ranger le voyait donc absent, et « une fenêtre
        // libre plus un onglet » faisait un au lieu de deux. Les deux touches
        // disparaissaient alors qu'il y avait bien deux fenêtres à ranger.
        ArrangeableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Icône du compte, telle que la liste la montre.</summary>
    private string? IconFor(ScrcpySession session) =>
        _iconDirectory is null ? null : System.IO.Path.Combine(_iconDirectory, "scrcpy.png");

    /// <summary>Vrai si une session est déjà ouverte sur ce téléphone.</summary>
    private bool HasOpenSessionOn(string deviceId) =>
        _sessions.ActiveSessions.Any(
            s => string.Equals(s.Target.DeviceId, deviceId, StringComparison.Ordinal));

    private async Task ApplyWindowSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        await RefreshRanksAsync(cancellationToken).ConfigureAwait(false);

        // Le palier choisi est rendu tel quel. Il a été un temps rogné par un
        // calcul de liaison ; la mesure a montré que ce calcul coûtait de la
        // netteté sans rien gagner, la bande passante n'ayant jamais été le
        // facteur limitant. Voir la décision sur la gigue.
        _quality = QualityProfile.For(settings.Quality, settings.CustomQuality);

        _zoom = settings.GameZoom;
        _sessions.StopAppOnClose = settings.StopAppOnClose;
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
            // Reconnue par son processus, et non par le handle que nous avons
            // retenu : celui d'une session fraîchement rouverte n'est pas
            // encore résolu, et les raccourcis se croyaient alors hors de chez
            // eux. Ils restaient éteints tant qu'on ne cliquait pas ailleurs
            // puis de nouveau sur une fenêtre de jeu.
            var owner = _windows.GetWindowProcessId(window);

            // Le cadre à onglets compte parmi nos fenêtres. Il manquait :
            // cliquer sur la barre d'onglets ou sur le bord du cadre pour
            // changer de compte éteignait les douze raccourcis, Ctrl+P compris,
            // jusqu'à ce qu'on reclique dans l'image du jeu.
            var ours = OwnsWindow?.Invoke(window) == true
                       || (_tabs is { Handle: var frame } && frame != 0 && frame == window);

            var mine = HotkeyScope.Holds(
                window,
                owner,
                _sessions.ActiveSessions.Select(s => new SessionWindow(s.WindowHandle, s.ProcessId)),
                ours);

            // La bascule est journalisée : sans elle, des raccourcis éteints
            // par une fenêtre non reconnue ne laissaient aucune trace, et le
            // symptôme ressemblait à un raccourci qui « ne marche plus ».
            if (mine != _hotkeysActive)
            {
                _hotkeysActive = mine;

                LogHotkeyScope(mine ? "actifs" : "en veille", window);
            }

            await _hotkeys.SetEnabledAsync(mine).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Ces deux gardes tournent sur un fil de fond, au rythme des
            // changements de fenêtre au premier plan. Une faute y est fréquente
            // et sans gravité pour la session ; elle part au journal, et la
            // ligne du panneau dit qu'un incident a été relevé.
            LogHotkeyFailure(exception);
        }
    }

    private bool _hotkeysActive = true;

    /// <summary>
    /// Le parcours au clavier change d'onglet quand le cadre a la main, et de
    /// fenêtre libre sinon.
    ///
    /// Ctrl+Tab ne faisait rien dans le cadre : le parcours passe par
    /// <see cref="ManagedSessions"/>, qui écarte les comptes logés parce que
    /// les placements automatiques n'ont rien à leur dire. Le clavier, lui,
    /// avait quand même besoin d'eux, et le raccourci ne voulait donc pas dire
    /// la même chose selon le mode.
    ///
    /// La fenêtre au premier plan reste le cadre même quand c'est le jeu qui
    /// tient le clavier : logé, il est fille du cadre et ne peut pas l'être.
    /// </summary>
    private async Task<bool> CycleTabsAsync(bool forward)
    {
        if (_tabs is not { } frame
            || frame.Handle == 0
            || _windows.Controller.GetForegroundWindow() != frame.Handle)
        {
            return false;
        }

        var tourne = false;

        await OnUiAsync(() => tourne = frame.Cycle(forward)).ConfigureAwait(false);

        return tourne;
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
                    if (!await CycleTabsAsync(forward: true).ConfigureAwait(false))
                    {
                        _windows.FocusNext(ManagedSessions);
                    }

                    break;

                case HotkeyAction.PreviousInstance:
                    if (!await CycleTabsAsync(forward: false).ConfigureAwait(false))
                    {
                        _windows.FocusPrevious(ManagedSessions);
                    }

                    break;

                case HotkeyAction.Rearrange:
                    await StackOnActiveAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Tile:
                    await TileAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Quests:
                    QuestsToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case HotkeyAction.Almanax:
                    AlmanaxRequested?.Invoke(this, EventArgs.Empty);
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
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Ces deux gardes tournent sur un fil de fond, au rythme des
            // changements de fenêtre au premier plan. Une faute y est fréquente
            // et sans gravité pour la session ; elle part au journal, et la
            // ligne du panneau dit qu'un incident a été relevé.
            LogHotkeyFailure(exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lancement terminé : {opened} fenêtre(s) ouverte(s), {problems} problème(s).")]
    private partial void LogLaunch(int opened, int problems);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Préambule d'ouverture : raccourcis {hotkeysMs} ms, appareils {devicesMs} ms, "
            + "liaison {linkMs} ms, réglages {settingsMs} ms, total {totalMs} ms.")]
    private partial void LogPreamble(
        long hotkeysMs,
        long devicesMs,
        long linkMs,
        long settingsMs,
        long totalMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La coupure d'ADB sur {address} a échoué : {reason}. L'association est rompue quand même.")]
    private partial void LogDisconnectFailed(string address, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "L'appareil {serial} se bride : état thermique {status}, surface {skin} °C.")]
    private partial void LogHeat(string serial, int status, double skin);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Fenêtre perdue, réouverture de {name} dans {seconds} s (tentative {attempt}).")]
    private partial void LogRecovering(string name, int attempt, int seconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Réouverture de {name} abandonnée après {attempts} tentatives.")]
    private partial void LogRecoveryGaveUp(string name, int attempts);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Liaison : filaire, ou Wi-Fi que l'appareil ne décrit pas. Aucun tampon.")]
    private partial void LogUnknownLink();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Liaison : {standard} à {frequencyMhz} MHz, {linkSpeedMbps} Mb/s annoncés, "
            + "{rssi} dBm, {retryPercent} % de réémissions. "
            + "Tampon d'affichage {bufferMs} ms.")]
    private partial void LogLink(
        string standard,
        int frequencyMhz,
        int linkSpeedMbps,
        int rssi,
        double retryPercent,
        int bufferMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Aucune géométrie mémorisée pour {instances} : la fenêtre rouvre au coin par défaut.")]
    private partial void LogMissingGeometry(string instances);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{instance} : afficheur prêt en {displayMs} ms, démarrage complet en {totalMs} ms.")]
    private partial void LogStartupTiming(string instance, long displayMs, long totalMs);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{instance} : afficheur {width}x{height} à {dpi} ppp, {fps} ips, {kbps} kb/s.")]
    private partial void LogStreamSettings(
        string instance,
        int width,
        int height,
        int dpi,
        int fps,
        int kbps);

    [LoggerMessage(Level = LogLevel.Information, Message = "{count} téléphone(s) reconnecté(s) automatiquement.")]
    private partial void LogReconnected(int count);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Compte « {name} » ajouté sur {device} : {succeeded}. {message}")]
    private partial void LogAccountAdded(string device, string name, bool succeeded, string message);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{count} compte(s) oublié(s) : leur profil Android a disparu, "
            + "ou le jeu n'y est plus installé.")]
    private partial void LogProfilesForgotten(int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Ouverture de {instance} refusée.{newLine}Commande : {commandLine}{newLine}Sortie de scrcpy :{newLine}{output}")]
    private partial void LogSessionFailure(string instance, string commandLine, string output, string newLine = "\n");

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} refusée en {height} de haut : nouvel essai en {retry} de haut.")]
    private partial void LogDisplayFallback(string instance, int height, int retry);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} : fenêtre introuvable, le compte reste en fenêtre libre.")]
    private partial void LogTabWindowMissing(string instance);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{instance} non lancée : appareil au niveau d'API {sdk}, sous le minimum requis.")]
    private partial void LogAndroidTooOld(string instance, int sdk);

    [LoggerMessage(Level = LogLevel.Information, Message = "Raccourcis {state} (fenêtre {window}).")]
    private partial void LogHotkeyScope(string state, nint window);

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
        Message = "Un geste sur le cadre à onglets n'a pas pu être traité.")]
    private partial void LogTabsFailure(Exception exception);


    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La session {instance} s'est terminée seule ({state}) : {message}\n{output}")]
    private partial void LogSessionEnded(string instance, string state, string message, string output);
}
