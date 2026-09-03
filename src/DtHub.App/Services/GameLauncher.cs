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

        // La fenêtre est placée avant l'ouverture du jeu, pour qu'il naisse à
        // la taille définitive.
        _sessions.PrepareWindow = async (session, placement, cancellationToken) =>
        {
            // La fenêtre est maintenue garée hors écran : scrcpy recentre la
            // sienne à la première image. Sa taille, elle, n'est pas touchée :
            // l'afficheur est déjà né à la bonne, et le jeu fige la hauteur de
            // sa mise en page à son initialisation.
            if (placement is { } wanted)
            {
                await _windows.MoveOnlyAsync(session, wanted.X, wanted.Y, cancellationToken)
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

    /// <summary>Rythme des contrôles et des sondages, selon la qualité.</summary>
    public QualityProfile Quality => _quality;


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

        // Les profils disparus s'oublient avant la fusion : sinon leur entrée
        // mémorisée reparaîtrait dans le résultat, et la liste garderait un
        // compte qui n'existe plus nulle part.
        var forgotten = await _settings
            .ForgetMissingProfilesAsync(_instances.ScannedProfiles, cancellationToken)
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

        await EnsureHotkeysAsync(cancellationToken).ConfigureAwait(false);
        await ApplyWindowSettingsAsync(cancellationToken).ConfigureAwait(false);

        var options = await _settings.GetScrcpyOptionsAsync(cancellationToken).ConfigureAwait(false);

        options = options with
        {
            WindowTitleHint = await BuildTitleHintAsync(cancellationToken).ConfigureAwait(false),
            IconDirectory = _iconDirectory,
        };

        var devices = await ResolveDevicesAsync(cancellationToken).ConfigureAwait(false);

        // La position est donnée à scrcpy dès le lancement. Le faire après
        // coup ne suffit pas : scrcpy recentre sa fenêtre quand il reçoit la
        // première image, donc après notre placement.
        remembered ??= await _settings.GetWindowRectsAsync(cancellationToken).ConfigureAwait(false);

        List<string> problems = [];
        List<ScrcpySession> started = [];

        foreach (var instance in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            Message = result.Message + $"\n\nSur un appareil {brand.Name} : {brand.ClonePath}.",
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

        await _registry.ForgetAsync(deviceId, cancellationToken).ConfigureAwait(false);
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
        if (_tabs is not { } cadre)
        {
            return;
        }

        WindowPlacement? place = null;

        await OnUiAsync(() => place = _windows.Controller.GetPlacement(cadre.Handle))
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
        var placed = await _windows
            .TileAsync(ManagedSessions, cancellationToken)
            .ConfigureAwait(false);

        await CaptureGeometriesAsync(cancellationToken).ConfigureAwait(false);

        return placed;
    }

    /// <summary>
    /// Empile les fenêtres sur celle qui est active, ou sur la première.
    /// C'est ce que fait le raccourci de replacement, et le bouton de la barre
    /// du bas : on place une fenêtre où on la veut, les autres la rejoignent.
    /// </summary>
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

        var reglages = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        _tabbed = reglages.Instances
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
        var reglages = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);

        // L'ordre retenu est reposé après chaque arrivée, et non laissé à
        // celui des arrivées. Les afficheurs ne se préparent pas à la même
        // vitesse : d'un lancement à l'autre, les onglets sortaient dans un
        // ordre différent, et celui qu'on avait rangé à la souris ne tenait pas
        // d'une session à la suivante.
        var ordre = reglages.Instances
            .OrderBy(i => i.Order)
            .Select(i => i.Key)
            .ToList();

        await OnUiAsync(() =>
        {
            var cadre = EnsureTabs(reglages);

            cadre.Attach(
                session.Target.Key,
                session.DisplayName,
                IconFor(session),
                handle,
                session.SourceAspectRatio);

            cadre.Reorder(ordre);
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
        var fil = System.Windows.Application.Current?.Dispatcher;

        if (fil is null || fil.CheckAccess())
        {
            geste();
            return Task.CompletedTask;
        }

        return fil.InvokeAsync(geste).Task;
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

        var cadre = new Windows.TabbedGameWindow(_windows.Controller);

        cadre.Closed += (_, _) => _tabs = null;

        // Fermer le cadre ferme les comptes qu'il logeait : c'est ce que le
        // geste annonce, et les voir se disperser en fenêtres libres était le
        // contraire de ce qu'on demandait.
        cadre.CloseRequested += async (_, loges) =>
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
        cadre.Reordered += async (_, mouvement) =>
        {
            try
            {
                if (await _settings
                        .MoveInstanceAsync(mouvement.Moved, mouvement.Onto, mouvement.Before)
                        .ConfigureAwait(true))
                {
                    await RefreshRanksAsync().ConfigureAwait(true);

                    var ordre = await _settings.GetInstanceRanksAsync().ConfigureAwait(true);

                    cadre.Reorder([.. ordre.OrderBy(p => p.Value).Select(p => p.Key)]);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogTabsFailure(exception);
            }
        };

        _tabs = cadre;
        cadre.Show();

        // Après l'affichage : il n'y a pas de poignée avant, donc rien à
        // placer. Le cadre reparaît ainsi là où on l'avait laissé, et un profil
        // en onglets le rouvre là où il était quand on l'a enregistré.
        _placements.Restore(cadre, WindowPlacements.Tabs, document);

        return cadre;
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
            return;
        }

        await OnUiAsync(() => _tabs?.Detach(instance.Key)).ConfigureAwait(false);
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

        _quality = QualityProfile.For(settings.Quality, settings.CustomQuality);
        _zoom = settings.GameZoom;
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

                case HotkeyAction.Tile:
                    await TileAsync().ConfigureAwait(false);
                    break;

                case HotkeyAction.Quests:
                    QuestsToggleRequested?.Invoke(this, EventArgs.Empty);
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
        Message = "{count} instance(s) oubliée(s) : leur profil Android n'existe plus.")]
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
