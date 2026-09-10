using System.Collections.Concurrent;
using DtHub.Core.Dependencies;
using DtHub.Core.Localization;
using DtHub.Core.Processes;
using DtHub.Core.Sessions;

namespace DtHub.Core.Scrcpy;

/// <summary>
/// Ouvre, suit et ferme les sessions de mirroring. Ne connaît que les
/// processus qu'il a lui-même démarrés : les fenêtres scrcpy ouvertes par un
/// autre logiciel ne sont jamais touchées.
/// </summary>
public sealed class ScrcpySessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ScrcpySession> _sessions = new(StringComparer.Ordinal);


    private readonly IScrcpyLocator _scrcpy;
    private readonly Adb.IAdbLocator _adbLocator;
    private readonly IProcessLauncher _launcher;
    private readonly IAppLauncher _appLauncher;

    public ScrcpySessionManager(
        IScrcpyLocator scrcpy,
        Adb.IAdbLocator adbLocator,
        IProcessLauncher launcher,
        IAppLauncher appLauncher)
    {
        _scrcpy = scrcpy;
        _adbLocator = adbLocator;
        _launcher = launcher;
        _appLauncher = appLauncher;
    }

    /// <summary>
    /// Délai laissé à scrcpy pour créer son afficheur virtuel. Un téléphone
    /// endormi ou chargé met plusieurs secondes.
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sessions connues, vivantes ou terminées.</summary>
    public IReadOnlyCollection<ScrcpySession> Sessions => [.. _sessions.Values];

    /// <summary>
    /// Rang d'une session dans l'ordre voulu par l'utilisateur. Tant qu'il
    /// n'est pas fourni, les sessions restent dans leur ordre de démarrage.
    /// </summary>
    public Func<ScrcpySession, int>? OrderKey { get; set; }

    /// <summary>
    /// Appelé quand l'afficheur existe, juste avant d'ouvrir l'application.
    ///
    /// C'est le moment de donner à la fenêtre sa taille définitive : le jeu
    /// fixe son échelle et sa mise en page à son ouverture, et ne les revoit
    /// pas toujours si on redimensionne pendant qu'il démarre. L'image se
    /// retrouve alors coupée.
    /// </summary>
    public Func<ScrcpySession, ScrcpyWindowPlacement?, CancellationToken, Task>? PrepareWindow { get; set; }

    /// <summary>
    /// Demande à une fenêtre de scrcpy de se fermer d'elle-même. Tant qu'il
    /// n'est pas fourni, l'arrêt se fait à coups de <c>Kill</c>.
    /// </summary>
    public Action<ScrcpySession>? RequestClose { get; set; }

    /// <summary>
    /// Temps laissé à scrcpy pour partir de lui-même avant d'être tué.
    ///
    /// Il doit prévenir son serveur sur le téléphone, ce qui demande un aller
    /// simple sur la liaison. Une seconde et demie couvre largement une
    /// liaison Wi-Fi ordinaire, sans faire attendre à la fermeture.
    /// </summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Vrai si fermer une fenêtre doit aussi arrêter le jeu sur le téléphone.
    ///
    /// Sans cela le jeu survit à sa fenêtre. Son afficheur virtuel est rendu,
    /// mais l'application, elle, reste au chaud : relevé sur le poste, un jeu
    /// tournait depuis dix-huit minutes sans aucune fenêtre en face, avec deux
    /// cent vingt mégaoctets à lui, et avait survécu à plusieurs fermetures de
    /// DT Hub. Le personnage reste aussi connecté aux serveurs du jeu.
    ///
    /// Le prix est connu et assumé : on ne peut plus refermer une fenêtre puis
    /// la rouvrir en étant toujours en jeu. C'est le réglage qui tranche.
    /// </summary>
    public bool StopAppOnClose { get; set; }

    /// <summary>
    /// Temps laissé à l'arrêt du jeu sur le téléphone.
    ///
    /// Court, et explicite, parce que le défaut ne conviendrait pas : une
    /// commande ADB attend vingt secondes, alors que l'application entière
    /// s'arrête en huit. Quitter avec un téléphone injoignable dépasserait le
    /// budget et figerait la fermeture. Deux secondes couvrent largement les
    /// trois à cinq dixièmes mesurés sur une liaison Wi-Fi ordinaire.
    /// </summary>
    public TimeSpan StopAppTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Une seule ouverture à la fois par téléphone.</summary>
    private readonly DeviceStartupGate _gate = new();

    /// <summary>
    /// Repos laissé sur un appareil après une ouverture. Nul par défaut : le
    /// verrou impose déjà l'espacement d'une ouverture complète.
    /// </summary>
    public TimeSpan StartupCooldown
    {
        get => _gate.Cooldown;
        init => _gate.Cooldown = value;
    }

    /// <summary>Vrai si une ouverture est en cours sur cet appareil.</summary>
    public bool IsDeviceBusy(string deviceId) => _gate.IsBusy(deviceId);

    /// <summary>Signalé quand un appareil devient occupé, ou cesse de l'être.</summary>
    public event EventHandler<DeviceBusyChangedEventArgs>? DeviceBusyChanged
    {
        add => _gate.BusyChanged += value;
        remove => _gate.BusyChanged -= value;
    }

    /// <summary>
    /// Sessions encore ouvertes, dans l'ordre configuré, ou dans leur ordre de
    /// démarrage à défaut.
    ///
    /// L'ordre de démarrage ne convenait pas seul : relancer une instance lui
    /// donnait un nouvel horodatage et la renvoyait en fin de cycle clavier,
    /// alors qu'elle n'avait pas changé de place à l'écran.
    /// </summary>
    public IReadOnlyList<ScrcpySession> ActiveSessions =>
        OrderKey is { } rank
            ? [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(rank).ThenBy(s => s.StartedUtc)]
            : [.. _sessions.Values.Where(s => s.IsAlive).OrderBy(s => s.StartedUtc)];

    /// <summary>Signalé à chaque changement d'état d'une session.</summary>
    public event EventHandler<ScrcpySession>? SessionChanged;

    /// <summary>
    /// Ouvre une session pour une cible. La méthode rend la main dès que la
    /// session est utilisable ou définitivement en échec ; elle ne se met
    /// jamais à attendre indéfiniment.
    /// </summary>
    public async Task<ScrcpySession> StartAsync(
        LaunchTarget target,
        ScrcpyOptions options,
        ScrcpyWindowPlacement? placement = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        var serial = target.Serial;

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var windowTitle = ScrcpyCommandBuilder.BuildWindowTitle(target.DisplayName, options.WindowTitleHint);

        string scrcpyPath;
        string adbPath;

        try
        {
            scrcpyPath = await _scrcpy.GetScrcpyPathAsync(cancellationToken).ConfigureAwait(false);
            adbPath = await _adbLocator.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DependencyProvisioningException exception)
        {
            return FailedSession(sessionId, target, windowTitle, exception.Message);
        }
        catch (Adb.AdbException exception)
        {
            return FailedSession(sessionId, target, windowTitle, exception.UserMessage);
        }

        var request = new ProcessRequest
        {
            FileName = scrcpyPath,
            Arguments = ScrcpyCommandBuilder.BuildMirrorArguments(serial, windowTitle, options, placement),

            // scrcpy honore cette variable : il utilisera notre copie d'ADB
            // plutôt que celle livrée dans sa propre archive.
            Environment = new Dictionary<string, string?>
            {
                ["ADB"] = adbPath,

                // Les fenêtres de jeu portent l'icône de l'application, et non
                // celle de scrcpy.
                ["SCRCPY_ICON_DIR"] = options.IconDirectory,
            },
        };

        // Une seule ouverture à la fois par téléphone : deux qui se chevauchent
        // se cassent, la première mourant sur une connexion au serveur qu'elle
        // avait pourtant déjà poussé.
        //
        // Le verrou ne couvre que la poussée, la connexion et la création de
        // l'afficheur. Il couvrait aussi l'ouverture du jeu, ce qui bloquait
        // les autres lignes pour rien : « am force-stop » et « am start » sont
        // des commandes ordinaires, propres à un profil Android, qui ne
        // touchent pas au serveur poussé par scrcpy. Mesuré sur le téléphone de
        // référence, cela retenait le verrou 1148 ms au lieu de 683, et 2094 au
        // lieu de 1310.
        //
        // Un clic pendant l'attente prend la file : le refuser obligerait à
        // recliquer.
        var lease = await _gate
            .EnterAsync(target.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        IProcessSession process;
        try
        {
            process = _launcher.Start(request);
        }
        catch (ProcessLaunchException exception)
        {
            await lease.DisposeAsync().ConfigureAwait(false);

            return FailedSession(
                sessionId, target, windowTitle,
                Strings.Get("ScrcpyDidNotStart"),
                exception.Message);
        }

        var session = new ScrcpySession(sessionId, target, windowTitle, process)
        {
            // L'afficheur garde une définition fixe : la fenêtre est calculée
            // à son rapport, sur sa zone client.
            SourceAspectRatio = options is { UseVirtualDisplay: true, VirtualDisplayHeight: > 0 }
                ? (double)options.VirtualDisplayWidth / options.VirtualDisplayHeight
                : 0,

        };

        session.CommandLine = request.ToDisplayString();
        _sessions[sessionId] = session;

        var displayReady = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => PumpAsync(session, displayReady, options.UseVirtualDisplay), CancellationToken.None);

        var chrono = System.Diagnostics.Stopwatch.StartNew();

        int? displayId;
        try
        {
            displayId = await AwaitDisplayAsync(session, options, displayReady, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Rendu ici et pas plus tard : l'afficheur existe, la prochaine
            // ouverture peut pousser son serveur sans risque.
            await lease.DisposeAsync().ConfigureAwait(false);
        }

        if (displayId is not null)
        {
            await LaunchGameAsync(session, placement, displayId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        session.StartupMs = chrono.ElapsedMilliseconds;

        return session;
    }

    /// <summary>
    /// Les encodeurs vidéo que l'appareil déclare, ou une liste vide si la
    /// question n'a pas abouti.
    ///
    /// Aucune fenêtre, aucun afficheur : scrcpy pousse son serveur, interroge
    /// et sort. Rien n'est propagé en cas d'échec, c'est un renseignement, pas
    /// une étape de lancement.
    /// </summary>
    public async Task<IReadOnlyList<VideoEncoder>> ListEncodersAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return [];
        }

        try
        {
            var scrcpyPath = await _scrcpy.GetScrcpyPathAsync(cancellationToken).ConfigureAwait(false);
            var adbPath = await _adbLocator.GetAdbPathAsync(cancellationToken).ConfigureAwait(false);

            var request = new ProcessRequest
            {
                FileName = scrcpyPath,
                Arguments = ScrcpyCommandBuilder.BuildListEncodersArguments(serial),
                Environment = new Dictionary<string, string?> { ["ADB"] = adbPath },
            };

            await using var process = _launcher.Start(request);

            var lines = new List<string>();

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(EncoderListTimeout);

            await foreach (var line in process.Output.ReadAllAsync(deadline.Token).ConfigureAwait(false))
            {
                lines.Add(line.Text);
            }

            return ScrcpyEncoders.Parse(string.Join('\n', lines));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Un appareil qui ne répond pas, scrcpy absent, un délai dépassé :
            // ne pas connaître les encodeurs est un résultat valable, et
            // l'appelant s'en passe. C'est le même silence assumé que pour la
            // chaleur et la batterie.
            return [];
        }
    }

    /// <summary>
    /// Au-delà, on renonce à connaître les encodeurs. La question coûte une
    /// poussée du serveur, et elle n'est jamais urgente.
    /// </summary>
    private static readonly TimeSpan EncoderListTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Ferme une session ouverte par DT Hub.</summary>
    public async Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return;
        }

        // Dit avant tout le reste : la boucle de lecture verra la marque quand
        // le canal se refermera, et saura que cette fin était voulue.
        session.StopRequested = true;

        // Le droit d'arrêter le jeu est réclamé maintenant, avant même de
        // toucher à scrcpy. Le réclamer après aurait laissé la boucle de
        // lecture, réveillée par la mort du processus, le prendre la première :
        // cette méthode aurait alors rendu la main sans rien attendre, et
        // quitter l'application aurait pu couper l'arrêt en plein vol.
        var mine = StopAppOnClose && session.ClaimAppStop();

        // scrcpy est prié de partir avant d'être tué : c'est lui qui prévient
        // son serveur, et le serveur qui rend l'afficheur virtuel. Un client
        // tué net sur une liaison Wi-Fi laissait le serveur en vie sur le
        // téléphone, avec son afficheur ; la fenêtre suivante s'ouvrait alors
        // sur un écran gris, le jeu étant resté sur l'afficheur abandonné.
        await RequestCloseAsync(session, cancellationToken).ConfigureAwait(false);

        if (!session.Process.HasExited)
        {
            session.Process.Kill();

            try
            {
                await session.Process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // L'appelant renonce à attendre ; le processus a reçu l'ordre.
            }
        }

        Transition(session, ScrcpySessionState.Stopped);

        // Après le départ de scrcpy, jamais avant : c'est lui qui prévient son
        // serveur, et le serveur qui rend l'afficheur virtuel. Attendu ici, et
        // pas seulement laissé à la fin de la lecture de sortie, parce que
        // quitter l'application ne laisse pas le temps à celle-ci de finir.
        if (mine)
        {
            await ForceStopGameAsync(session, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Arrête le jeu sur le téléphone, si le réglage le demande et si personne
    /// ne l'a déjà fait pour cette session.
    ///
    /// Rien n'est propagé : un téléphone parti, une liaison coupée, un profil
    /// que le shell ne peut plus atteindre, aucun de ces cas ne doit empêcher
    /// une fenêtre de se fermer ni l'application de s'arrêter. Le jeu qui
    /// survit est un désagrément ; une fermeture qui se fige est une panne.
    /// </summary>
    private async Task StopAppOnDeviceAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        if (!StopAppOnClose || !session.ClaimAppStop())
        {
            return;
        }

        await ForceStopGameAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Arrête le jeu, sans se demander si c'est le moment : l'appelant a déjà
    /// réclamé le droit de le faire.
    /// </summary>
    private async Task ForceStopGameAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        // Ce qu'on n'a pas ouvert, on ne le ferme pas : une session qui a
        // échoué avant de rien lancer laisserait tourner un jeu auquel
        // quelqu'un joue peut-être sur le téléphone.
        if (!session.AppLaunchedByUs)
        {
            return;
        }

        await ForceStopCoreAsync(session).ConfigureAwait(false);
    }

    /// <summary>
    /// L'ordre lui-même, avec son délai propre et rien d'autre.
    ///
    /// Le jeton de l'appelant n'est délibérément pas transmis. Sur le chemin de
    /// la fermeture, c'est justement lui qu'on annule : le lier ici reviendrait
    /// à renoncer à l'arrêt au moment précis où il compte le plus, et à laisser
    /// le jeu tourner sur le téléphone. Une fois décidé, l'ordre part ; son
    /// propre délai suffit à borner l'attente.
    /// </summary>
    private async Task ForceStopCoreAsync(ScrcpySession session)
    {
        using var deadline = new CancellationTokenSource(StopAppTimeout);

        try
        {
            await _appLauncher.ForceStopAsync(
                session.Serial,
                session.Target.UserId,
                session.Target.PackageName,
                deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Silence assumé, et c'est le seul comportement tenable ici : la
            // méthode est appelée sur le chemin de fermeture, y compris celui
            // de l'application entière. Une faute y serait sans destinataire.
            session.Record("Arrêt du jeu sur le téléphone impossible : " + exception.Message);
        }
    }

    /// <summary>
    /// Ferme toutes les sessions ouvertes par DT Hub, et elles seules. Les
    /// fenêtres scrcpy lancées par un autre logiciel sont ignorées.
    /// </summary>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        var running = _sessions.Values.Where(s => s.IsAlive).Select(s => s.Id).ToList();

        foreach (var id in running)
        {
            await StopAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Demande la fermeture et attend, sans dépasser le délai. Rend la main
    /// dès que le processus est parti, pour ne pas ralentir la fermeture.
    /// </summary>
    private async Task RequestCloseAsync(ScrcpySession session, CancellationToken cancellationToken)
    {
        if (RequestClose is null || session.WindowHandle == 0 || session.Process.HasExited)
        {
            return;
        }

        RequestClose(session);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CloseTimeout);

        try
        {
            await session.Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Parti trop lentement, ou l'appelant renonce : le Kill suit.
        }
    }

    /// <summary>Retire de la liste les sessions terminées.</summary>
    public void PruneFinished()
    {
        foreach (var session in _sessions.Values.Where(s => !s.IsAlive).ToList())
        {
            _sessions.TryRemove(session.Id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.Process.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
        _gate.Dispose();
    }

    /// <summary>
    /// Attend que le téléphone ouvre l'afficheur virtuel. C'est la seule partie
    /// du démarrage qui doive être sérialisée entre deux ouvertures.
    /// </summary>
    /// <returns>L'afficheur ouvert, ou null si la session a échoué.</returns>
    private async Task<int?> AwaitDisplayAsync(
        ScrcpySession session,
        ScrcpyOptions options,
        TaskCompletionSource<int?> displayReady,
        CancellationToken cancellationToken)
    {
        if (!options.UseVirtualDisplay)
        {
            Transition(session, ScrcpySessionState.Running);
            return null;
        }

        int? displayId;
        try
        {
            displayId = await displayReady.Task
                .WaitAsync(StartupTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (session.FailureKind == ScrcpyFailureKind.None)
            {
                session.FailureKind = ScrcpyFailureKind.Timeout;
            }

            // Le son d'abord, quand il est demandé. Sur certaines tablettes
            // Samsung, l'activer suffit à empêcher l'afficheur de s'ouvrir :
            // relevé sur un produit concurrent qui emprunte le même chemin, où
            // deux utilisateurs ont mis des jours à faire le lien, l'un
            // finissant par écrire « si je désactive le son ça marche ». Le
            // message ne le dit que si le son est effectivement demandé, sinon
            // il enverrait chercher une cause qu'on a déjà écartée.
            Fail(
                session,
                Strings.Get(options.AudioEnabled
                    ? "ScrcpyDisplayTimeoutWithAudio"
                    : "ScrcpyDisplayTimeout"));
            session.Process.Kill();
            return null;
        }

        if (displayId is null)
        {
            Fail(session, session.FailureMessage ?? Strings.Get("ScrcpySessionFailed"));
            return null;
        }

        session.VirtualDisplayId = displayId;
        session.DisplayReadyMs = (long)(DateTimeOffset.UtcNow - session.StartedUtc).TotalMilliseconds;

        return displayId;
    }

    /// <summary>
    /// Place la fenêtre puis ouvre le jeu sur l'afficheur. Hors verrou : ces
    /// commandes sont propres à un profil Android et ne touchent pas au serveur
    /// poussé par scrcpy.
    /// </summary>
    private async Task LaunchGameAsync(
        ScrcpySession session,
        ScrcpyWindowPlacement? placement,
        int displayId,
        CancellationToken cancellationToken)
    {
        // La fenêtre prend sa taille définitive avant que le jeu n'arrive :
        // il fixe son échelle à l'ouverture et ne la revoit pas toujours si on
        // redimensionne pendant son démarrage.
        if (PrepareWindow is { } prepare)
        {
            try
            {
                await prepare(session, placement, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                session.Record(Strings.Format("ScrcpyPrePlacementFailed", exception.Message));
            }
        }

        // Le jeu est arrêté avant d'être rouvert sur le nouvel afficheur.
        //
        // « am start --display » ne déplace pas une tâche existante : si le jeu
        // tourne déjà, Android le ramène simplement au premier plan là où il
        // est, et le nouvel afficheur reste vide, donc la fenêtre grise. Arrêter
        // la tâche est le seul moyen sûr de la faire renaître au bon endroit ;
        // ouvrir une fenêtre redémarre le jeu de toute façon.
        await _appLauncher.ForceStopAsync(
            session.Serial,
            session.Target.UserId,
            session.Target.PackageName,
            cancellationToken).ConfigureAwait(false);

        var launch = await _appLauncher.LaunchAsync(
            session.Serial,
            session.Target.UserId,
            session.Target.PackageName,
            session.Target.LaunchComponent,
            displayId,
            cancellationToken).ConfigureAwait(false);

        // Posé même quand l'ouverture échoue : « am start » peut avoir lancé
        // le jeu et rendu tout de même une faute, et dans le doute le jeu qu'on
        // laisse est bien le nôtre.
        session.AppLaunchedByUs = true;

        if (!launch.Succeeded)
        {
            // Sans application, la fenêtre resterait vide : on ferme plutôt que
            // de laisser un écran noir sans explication.
            Fail(session, launch.UserMessage ?? Strings.Get("ScrcpyAppNotOpened"));
            session.Process.Kill();
            return;
        }

        Transition(session, ScrcpySessionState.Running);
    }

    /// <summary>
    /// Lit la sortie de scrcpy jusqu'à la fin du processus : identifiant
    /// d'afficheur, erreurs, puis état final.
    /// </summary>
    private async Task PumpAsync(
        ScrcpySession session,
        TaskCompletionSource<int?> displayReady,
        bool expectVirtualDisplay)
    {
        try
        {
            await foreach (var line in session.Process.Output.ReadAllAsync().ConfigureAwait(false))
            {
                session.Record(line.Text);

                if (expectVirtualDisplay
                    && !displayReady.Task.IsCompleted
                    && ScrcpyOutputParser.TryParseVirtualDisplayId(line.Text) is { } displayId)
                {
                    displayReady.TrySetResult(displayId);
                    continue;
                }

                if (ScrcpyOutputParser.IsFatal(line.Text))
                {
                    var kind = ScrcpyOutputParser.Classify(line.Text);

                    // La première erreur est la cause, les suivantes en sont
                    // souvent les conséquences : on retient la première.
                    if (session.FailureKind == ScrcpyFailureKind.None)
                    {
                        session.FailureKind = kind;
                    }

                    session.FailureMessage ??= ScrcpyOutputParser.Describe(kind);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Le canal s'est refermé pendant la lecture, rien à signaler.
        }

        // La sortie est close : le processus est terminé ou l'a été.
        displayReady.TrySetResult(null);

        // C'est ici que passe la fenêtre fermée à la main, et le téléphone
        // débranché : aucun code à nous n'a été appelé, seul le canal s'est
        // tu. La fermeture volontaire est déjà servie par StopAsync, et la
        // réclamation à usage unique empêche le double aller-retour.
        await StopAppOnDeviceAsync(session, CancellationToken.None).ConfigureAwait(false);

        var exitCode = session.Process.ExitCode ?? -1;

        if (session.State == ScrcpySessionState.Failed)
        {
            SessionChanged?.Invoke(this, session);
            return;
        }

        if (exitCode == 0 || session.FailureMessage is null)
        {
            Transition(session, ScrcpySessionState.Stopped);
        }
        else
        {
            Fail(session, session.FailureMessage);
        }
    }

    private ScrcpySession FailedSession(
        string sessionId,
        LaunchTarget target,
        string windowTitle,
        string message,
        string? details = null)
    {
        var session = new ScrcpySession(sessionId, target, windowTitle, NullProcessSession.Instance)
        {
            State = ScrcpySessionState.Failed,
            FailureMessage = details is null ? message : $"{message} ({details})",
            FailureKind = ScrcpyFailureKind.Environment,
        };

        _sessions[sessionId] = session;
        SessionChanged?.Invoke(this, session);

        return session;
    }

    private void Fail(ScrcpySession session, string? message)
    {
        session.FailureMessage = message ?? Strings.Get("ScrcpySessionInterrupted");
        Transition(session, ScrcpySessionState.Failed);
    }

    private void Transition(ScrcpySession session, ScrcpySessionState state)
    {
        // Avant la garde d'égalité : la marque doit se poser même si l'état
        // était déjà celui-là.
        if (state == ScrcpySessionState.Running)
        {
            session.EverRan = true;
        }

        if (session.State == state)
        {
            return;
        }

        session.State = state;
        SessionChanged?.Invoke(this, session);
    }
}
