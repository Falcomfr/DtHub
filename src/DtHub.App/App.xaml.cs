using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core;
using DtHub.Core.Devices;
using DtHub.Core.Diagnostics;
using DtHub.Core.Localization;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Updates;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;
using DtHub.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DtHub.App;

/// <summary>
/// Point d'entrée. Deux cas seulement : premier lancement, on demande quelles
/// instances ouvrir ; ensuite, on ouvre directement celles qui sont cochées.
/// </summary>
public partial class App : Application, IDisposable
{
    /// <summary>
    /// Marque l'exécution en cours. Un second lancement, par le raccourci du
    /// bureau ou autrement, ne doit pas ouvrir un deuxième jeu de fenêtres :
    /// il réveille celui qui tourne déjà et s'efface.
    /// </summary>
    private const string InstanceName = @"Local\DtHub.Instance";

    private const string WakeName = @"Local\DtHub.Wake";

    /// <summary>
    /// Surveille la forme des fenêtres de jeu. La correction n'a lieu qu'une
    /// fois la taille stable : on ne lutte pas contre un geste en cours.
    /// </summary>
    private readonly DispatcherTimer _shape = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private IHost? _host;
    private ConfiguratorWindow? _configurator;
    private QuestWindow? _quests;
    /// <summary>
    /// Ce qu'on s'accorde pour retenir l'état quand Windows ferme la session.
    /// Il en donne cinq ; on en prend trois, et l'arrêt suit.
    /// </summary>
    private static readonly TimeSpan SessionSaveLimit = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ce qu'on s'accorde pour ranger à l'arrêt. Fermer les fenêtres de jeu
    /// demande le plus clair de ce temps, et l'attente est bornée pour qu'une
    /// fenêtre récalcitrante ne retienne pas l'application.
    /// </summary>
    private static readonly TimeSpan ShutdownLimit = TimeSpan.FromSeconds(8);

    private bool _quitting;
    private bool _started;
    private Mutex? _instance;
    private EventWaitHandle? _wake;
    private RegisteredWaitHandle? _wakeRegistration;


    /// <summary>
    /// Ce qu'on fait d'une faute. Il ne connaît ni l'hôte ni les fenêtres :
    /// il les demande au moment où il en a besoin, ce qui lui permet de servir
    /// avant que l'hôte n'existe.
    /// </summary>
    private readonly FaultReporting _faults = new(
        () => (Current as App)?._host?.Services,
        () => (Current as App)?._configurator is { IsVisible: true } panel ? panel : null);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Les fenêtres de jeu ne sont pas des fenêtres WPF : l'application ne
        // doit pas se fermer quand le configurateur est masqué.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DarkTitleBar.Arm();


        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        _faults.Arm(this);

        try
        {
            _host = BuildHost();
            AppHost.Initialize(_host.Services);
            await _host.StartAsync().ConfigureAwait(true);

            // Une trace de démarrage garantit qu'un fichier de journal existe
            // toujours, même quand la session se passe sans incident.
            Log.Information("{Product} {Version} démarre.", ProductInfo.Name, ProductInfo.Version);

            await RunAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Le garde-fou du démarrage attrape tout, c'est son rôle : une
            // faute ici laisserait une application sans fenêtre. Tout sauf le
            // manque de mémoire, où ouvrir une fenêtre de plus n'aboutirait pas.
            _faults.Show(exception, Strings.Get("StartupFailed"));
            Shutdown(1);
        }
    }

    /// <summary>Enchaîne mise en route éventuelle, lancement, puis configurateur.</summary>
    private async Task RunAsync()
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var launcher = services.GetRequiredService<GameLauncher>();

        // La langue est posée avant la première fenêtre : les textes sont lus
        // à la construction des vues, et une fenêtre déjà bâtie ne changerait
        // plus de langue.
        await ApplyLanguageAsync(settings).ConfigureAwait(true);

        // Avant tout le reste, et avant la première fenêtre : sur un poste
        // neuf, ADB et scrcpy s'installaient au détour de deux appels qui
        // avaient besoin d'autre chose, dix-neuf mégaoctets durant lesquels
        // rien ne paraissait à l'écran. Ne montre rien quand ils sont là.
        await PreparationWindow
            .RunAsync(services.GetRequiredService<ToolPreparation>())
            .ConfigureAwait(true);

        await SweepLeftoversAsync().ConfigureAwait(true);

        await KillOrphansAsync(services).ConfigureAwait(true);

        launcher.IconDirectory = WindowIcons.EnsureDirectory(services.GetRequiredService<IAppPaths>());

        PlaceShortcut(services);

        // Le premier lancement se reconnaît à un registre d'appareils vide, et
        // non à une marque dans les réglages : c'est le fait qui compte, et il
        // se lit déjà.
        var firstRun = (await services.GetRequiredService<IDeviceRegistry>()
            .GetKnownAsync().ConfigureAwait(true)).Count == 0;

        // Le configurateur existe avant le lancement : c'est lui qui affichera
        // les problèmes s'il y en a.
        _configurator = services.GetRequiredService<ConfiguratorWindow>();

        // Les raccourcis ne sont actifs que si une fenêtre à nous est au
        // premier plan. Sans y ajouter le suivi de quêtes, ils mourraient dès
        // qu'on lui donne le focus, ce qui est précisément ce qu'on fait pour
        // lire un guide.
        SessionEnding += OnSessionEnding;

        launcher.OwnsWindow = OwnsWindow;

        // Sans lui, le rapport annonce zéro compte ouvert alors que deux
        // tournent, et ne sait pas quels noms masquer.
        services.GetRequiredService<DiagnosticReporter>().Launcher = launcher;
        launcher.ConfiguratorToggleRequested += (_, _) => Dispatcher.Invoke(ToggleConfigurator);
        launcher.QuestsToggleRequested += (_, _) => Dispatcher.Invoke(ToggleQuests);

        // Le bouton d'outil passe par le même chemin que le raccourci.
        services.GetRequiredService<ConfiguratorViewModel>().QuestsRequested +=
            (_, _) => Dispatcher.Invoke(ToggleQuests);
        launcher.QuitRequested += (_, _) => Dispatcher.Invoke(async () => await RequestQuitAsync().ConfigureAwait(true));
        // Le panneau était déjà masqué : c'est bien qu'on le veut masqué.
        launcher.LastWindowClosed += (_, _) => Dispatcher.Invoke(
            () => OnNothingLeft(rememberConfigurator: false));

        // Masquer le panneau alors qu'il ne reste aucune fenêtre de jeu
        // revient au même que fermer la dernière fenêtre panneau masqué : dans
        // les deux cas il ne reste rien, et l'application ne doit pas survivre
        // invisible. Sans cela, elle restait en vie sans rien à l'écran, et la
        // relancer ne faisait que redonner le panneau, sans rouvrir les
        // instances.
        //
        // Le panneau, lui, est retenu comme affiché : le masquer en dernier
        // n'est pas dire qu'on le veut masqué, c'est la façon de refermer ce
        // qui restait. Seul un panneau déjà masqué avant de fermer les fenêtres
        // de jeu vaut ce choix.
        _configurator.IsVisibleChanged += (_, _) =>
        {
            if (_started && _configurator?.IsVisible == false)
            {
                OnNothingLeft(rememberConfigurator: true);
            }
        };

        // La session nommée du démarrage, s'il y en a une, décide de ce qui
        // s'ouvre. Sans elle on ne touche à rien, et l'application rouvre ce
        // qui était ouvert la fois d'avant, comme elle l'a toujours fait.
        await ApplyDefaultLaunchProfileAsync(settings).ConfigureAwait(true);

        var report = await launcher.LaunchEnabledAsync().ConfigureAwait(true);

        _shape.Tick += (_, _) =>
        {
            // Même raison que pour le sondage du panneau : le palier de qualité
            // se change en cours de route, et l'intervalle posé au démarrage ne
            // suivait pas.
            if (_shape.Interval != launcher.Quality.WindowWatch)
            {
                _shape.Interval = launcher.Quality.WindowWatch;
            }

            launcher.Watch();
        };

        _shape.Interval = launcher.Quality.WindowWatch;
        _shape.Start();

        var document = await settings.GetAsync().ConfigureAwait(true);

        // La fenêtre doit être affichée une fois pour que son chargement se
        // fasse et que sa mise à l'échelle soit connue : PlaceAwayFrom mesure
        // le rapport de la fenêtre elle-même. L'opacité évite le clignotement
        // quand elle doit finalement rester masquée.
        _configurator.Opacity = 0;
        _configurator.Show();
        _configurator.RestorePlacement(document);
        _configurator.PlaceAwayFrom(document.GameAnchor);
        _configurator.Opacity = 1;

        // Au premier lancement, le panneau reste et s'ouvre sur les appareils :
        // c'est là qu'il n'y a rien et que tout commence. La fenêtre
        // d'association vient par-dessus, puisque sans téléphone associé aucun
        // autre geste n'a de sens.
        if (firstRun)
        {
            _configurator.ShowDevices();
            _configurator.BeginPairing();
        }
        else if (!StartupPresence.ShowConfigurator(document.ConfiguratorVisible, report.Opened))
        {
            _configurator.Hide();
        }

        // Le suivi de quêtes revient comme on l'a laissé : ouvert ou non, et
        // sur la quête qu'on y lisait. Sans cela il fallait le rouvrir puis
        // retrouver sa quête à chaque lancement.
        if (document.QuestsVisible)
        {
            await RestoreQuestsAsync(document.LastQuestUrl, document.LastQuestStep)
                .ConfigureAwait(true);
        }

        // Mises à jour : le ménage d'abord, puis ce qui vient d'être posé
        // s'annonce, puis on regarde s'il existe mieux. La recherche n'est pas
        // attendue : l'application ne doit pas démarrer au rythme du réseau.
        var updates = services.GetRequiredService<UpdateService>();
        var configuratorModel = services.GetRequiredService<ConfiguratorViewModel>();

        updates.Sweep();
        updates.Changed += (_, _) => Dispatcher.Invoke(() => ShowUpdateState(updates, configuratorModel));
        configuratorModel.UpdateNotesRequested += (_, _) => Dispatcher.Invoke(
            () => ShowNotes(
                $"DT Hub {updates.Available?.Version}",
                updates.Ready
                    ? Strings.Get("UpdateReadyNotes")
                    : Strings.Get("UpdateAvailableNotes"),
                ReleaseNotes.Readable(updates.Available?.Notes)));

        if (updates.TakeNotes() is { Length: > 0 } installed)
        {
            ShowNotes(
                $"DT Hub {updates.Running}",
                Strings.Get("UpdateJustInstalled"),
                installed);
        }

        _ = updates.CheckAsync(document.UpdatesAutomatic);

        // À partir d'ici seulement, masquer le panneau vaut décision de
        // l'utilisateur : le masquage de démarrage, lui, suit les réglages.
        _started = true;

        if (!report.AnyOpened)
        {
            Log.Warning(
                "Aucune fenêtre ouverte, les réglages restent affichés : {Problems}",
                report.Problems.Count > 0
                    ? string.Join(" ", report.Problems)
                    : "aucune instance à ouvrir.");
        }
    }

    /// <summary>
    /// Pose le raccourci du menu Démarrer sur l'exécutable, là où il se trouve.
    ///
    /// L'application n'est pas installée : c'est un fichier qu'on pose où l'on
    /// veut. Sans raccourci, on va le chercher là où on l'a mis, et il n'y a
    /// rien à épingler. Le raccourci est récrit à chaque démarrage, si bien que
    /// déplacer le fichier suffit à le corriger.
    ///
    /// Rien n'est copié ni déplacé : se copier laisserait un exécutable orphelin
    /// qui ne se mettrait jamais à jour, et se déplacer reviendrait à bouger le
    /// fichier de quelqu'un sans le lui demander.
    ///
    /// Rien n'est fait depuis un arbre de sources : le raccourci viserait la
    /// sortie de publication, que le lanceur de développement récrit à chaque
    /// fois. C'est la règle déjà écrite pour la mise à jour.
    ///
    /// Un raccourci que Windows refuse n'empêche rien : l'application démarre.
    /// </summary>
    /// <summary>
    /// Pose la langue de l'interface sur tous les fils.
    ///
    /// Le réglage l'emporte sur Windows quand il est rempli ; vide, c'est la
    /// langue d'affichage du système qui décide, et l'anglais quand elle n'est
    /// pas traduite. Les formats de nombres et de dates ne sont pas touchés :
    /// ils suivent le pays, qui est un autre réglage.
    /// </summary>
    private static async Task ApplyLanguageAsync(SettingsService settings)
    {
        var reglages = await settings.GetAsync().ConfigureAwait(true);
        var langue = AppLanguage.Choose(reglages.Language, CultureInfo.CurrentUICulture.Name);
        var culture = CultureInfo.GetCultureInfo(langue);

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Log.Information(
            "Langue de l'interface : {Langue} (réglage {Reglage}, Windows {Windows}).",
            langue,
            reglages.Language,
            CultureInfo.InstalledUICulture.Name);
    }

    private static void PlaceShortcut(IServiceProvider services)
    {
        var executable = Environment.ProcessPath;

        if (!UpdatePaths.CanReplace(executable, File.Exists))
        {
            return;
        }

        var link = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            ProductInfo.Name + ".lnk");

        if (!services.GetRequiredService<IShortcutWriter>()
            .Write(link, executable!, "Ouvrir " + ProductInfo.Name))
        {
            Log.Warning("Le raccourci du menu Démarrer n'a pas pu être posé.");
        }
    }

    /// <summary>
    /// Windows ferme la session : arrêt, redémarrage, déconnexion.
    ///
    /// C'est un arrêt volontaire comme un autre, et il doit retenir ce qu'un
    /// « Quitter » retient. Sans cela, redémarrer le poste ramenait les
    /// fenêtres à leur place de l'avant-dernière fois, celle du dernier arrêt
    /// par le bouton, et la fenêtre qu'on venait de déplacer perdait sa place.
    ///
    /// L'enregistrement est attendu, et non lancé en tâche de fond : Windows
    /// n'accorde que quelques secondes avant de fermer d'autorité, et une
    /// écriture lancée sans être attendue n'a aucune chance d'arriver. Il est
    /// attendu en laissant tourner la boucle de messages, faute de quoi les
    /// suites qui reviennent sur le fil d'affichage attendraient un fil qu'on
    /// aurait soi-même bloqué. Une minuterie borne l'attente : mieux vaut un
    /// état à moitié écrit qu'une session que l'on retient.
    /// </summary>
    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        Log.Information("Fin de session Windows : l'état est retenu avant l'arrêt.");

        var frame = new DispatcherFrame();

        var limit = new DispatcherTimer(
            SessionSaveLimit, DispatcherPriority.Send, (_, _) => frame.Continue = false, Dispatcher);

        limit.Start();

        _ = SaveSessionStateAsync(_configurator?.IsVisible == true)
            .ContinueWith(
                _ => frame.Continue = false,
                TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.PushFrame(frame);
        limit.Stop();
    }

    /// <summary>
    /// Sortie volontaire, par le bouton « Quitter ». C'est le seul moment où
    /// l'état de la session est retenu pour le prochain lancement : où sont
    /// les fenêtres, lesquelles étaient ouvertes, et si le configurateur était
    /// affiché.
    ///
    /// Fermer une fenêtre de jeu à la main ne change donc rien : elle revient
    /// au lancement suivant. C'est le geste de quitter qui fait foi.
    /// </summary>
    internal async Task RequestQuitAsync()
    {
        // Quitter depuis le panneau ou par le raccourci : c'est son état du
        // moment qui fait foi.
        await SaveSessionStateAsync(_configurator?.IsVisible == true).ConfigureAwait(true);

        Shutdown();
    }

    /// <summary>
    /// Retient où sont les fenêtres et si le configurateur était affiché.
    ///
    /// Appelée par toutes les sorties, et non par le seul bouton « Quitter » :
    /// fermer les dernières fenêtres de jeu à la main arrête aussi
    /// l'application, et ce chemin oubliait d'enregistrer quoi que ce soit. Le
    /// configurateur masqué se rouvrait alors au lancement suivant, et les
    /// fenêtres revenaient à leur place d'avant-dernière fois.
    /// </summary>
    private async Task SaveSessionStateAsync(bool configuratorVisible)
    {
        var services = _host?.Services;

        if (services is null)
        {
            return;
        }

        var launcher = services.GetRequiredService<GameLauncher>();
        var settings = services.GetRequiredService<SettingsService>();

        try
        {
            // Ce qui rouvrira au lancement suivant n'est pas décidé ici :
            // il suit les lancements et les fermetures explicites, pas
            // l'état du moment où l'on quitte.
            await launcher.CaptureGeometriesAsync().ConfigureAwait(true);

            await settings.SetConfiguratorVisibleAsync(configuratorVisible).ConfigureAwait(true);

            if (_configurator is not null)
            {
                await _configurator.SavePlacementAsync().ConfigureAwait(true);
            }

            if (_quests is not null)
            {
                await settings
                    .SetQuestsStateAsync(
                        _quests.IsVisible, _quests.LastQuestUrl, _quests.LastQuestStep)
                    .ConfigureAwait(true);

                await services
                    .GetRequiredService<WindowPlacements>()
                    .SaveAsync(_quests, WindowPlacements.Quests)
                    .ConfigureAwait(true);
            }
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "L'état de la session n'a pas pu être enregistré.");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "L'état de la session n'a pas pu être enregistré.");
        }
    }

    /// <summary>
    /// Ferme les fenêtres de mirroring laissées par une exécution précédente
    /// qui ne s'est pas terminée proprement. Sans cela, elles resteraient à
    /// l'écran et de nouvelles viendraient s'y ajouter.
    /// </summary>
    /// <summary>
    /// Efface les dossiers d'extraction des versions précédentes.
    ///
    /// Attendu, et non lancé en arrière-plan : quand rien ne s'ouvre,
    /// l'application s'arrête trois secondes après son démarrage, et la tâche
    /// détachée était coupée avant d'avoir effacé quoi que ce soit. Sans rien à
    /// faire, ce qui est le cas ordinaire, cela coûte le parcours d'un dossier.
    /// </summary>
    private static async Task SweepLeftoversAsync()
    {
        var removed = await Task.Run(BundleLeftovers.Sweep).ConfigureAwait(true);

        if (removed > 0)
        {
            Log.Information(
                "{Count} dossier(s) d'extraction laissé(s) par des versions précédentes effacé(s).",
                removed);
        }
    }

    private static async Task KillOrphansAsync(IServiceProvider services)
    {
        try
        {
            // Sans rien télécharger : scrcpy jamais installé veut dire scrcpy
            // jamais lancé, donc aucune fenêtre restée d'une exécution
            // précédente. Demander le chemin tout court mettait onze
            // mégaoctets sur le chemin du premier démarrage pour un ramassage
            // qui n'avait rien à ramasser.
            if (services.GetRequiredService<IScrcpyLocator>().TryGetInstalledPath() is not { } path)
            {
                return;
            }

            var killed = OrphanProcesses.KillFrom(path);

            if (killed > 0)
            {
                Log.Information("{Count} fenêtre(s) restée(s) d'une exécution précédente fermée(s).", killed);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Un ramassage impossible ne doit pas empêcher de démarrer.
            Log.Warning(exception, "Le ménage des fenêtres restantes a échoué.");
        }
    }

    /// <summary>
    /// Ferme l'application quand la dernière fenêtre de jeu disparaît sans que
    /// le configurateur soit à l'écran.
    ///
    /// Il ne resterait sinon rien de visible, et les raccourcis ne répondent
    /// pas quand aucune de nos fenêtres n'est au premier plan : l'application
    /// serait injoignable autrement que par le gestionnaire des tâches. Rien
    /// n'est enregistré au passage : fermer une fenêtre à la main ne change
    /// pas ce qui doit rouvrir au lancement suivant.
    /// </summary>
    /// <summary>
    /// Il ne reste ni fenêtre de jeu ni panneau : l'application s'arrête.
    /// </summary>
    /// <param name="rememberConfigurator">
    /// Ce qu'il faut retenir de la présence du panneau au prochain démarrage.
    /// Vrai quand c'est lui qu'on vient de masquer en dernier, faux quand il
    /// était déjà masqué avant que les fenêtres de jeu ne se ferment.
    /// </param>
    private void OnNothingLeft(bool rememberConfigurator)
    {
        // Deux sessions qui meurent ensemble signalent chacune la dernière.
        //
        // Les guides tiennent l'application en vie comme le panneau : ils sont
        // à l'écran, ils reçoivent les raccourcis, et on les consulte fenêtres
        // de jeu fermées. Sans eux dans le compte, fermer la dernière fenêtre
        // de jeu emportait le guide qu'on était en train de lire.
        if (_quitting || _configurator?.IsVisible == true || _quests?.IsVisible == true)
        {
            return;
        }

        // Appelée aussi quand le panneau se masque : il peut alors rester des
        // fenêtres de jeu, et l'application doit continuer.
        if (_host?.Services.GetRequiredService<GameLauncher>().ActiveSessions.Count > 0)
        {
            return;
        }

        _quitting = true;

        Log.Information("Plus aucune fenêtre ni panneau : arrêt.");

        // La géométrie des fenêtres vient d'être perdue avec elles : il ne
        // reste rien à relever. Le reste de l'état, lui, doit être retenu,
        // sans quoi le configurateur masqué se rouvrirait au lancement suivant.
        _ = SaveSessionStateAsync(rememberConfigurator).ContinueWith(
            _ => Shutdown(),
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>
    /// Prend la place unique, ou réveille l'exécution déjà en cours et rend
    /// faux. Sans cela, un second lancement ouvrirait un deuxième jeu de
    /// fenêtres de jeu par-dessus le premier.
    /// </summary>
    private bool ClaimSingleInstance()
    {
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var mine);

        if (!mine)
        {
            if (EventWaitHandle.TryOpenExisting(WakeName, out var running))
            {
                using (running)
                {
                    running.Set();
                }
            }

            return false;
        }

        _wake = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, WakeName);

        _wakeRegistration = ThreadPool.RegisterWaitForSingleObject(
            _wake,
            (_, _) => Dispatcher.Invoke(RevealConfigurator),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    /// <summary>Libère la place unique et son signal de réveil.</summary>
    public void Dispose()
    {
        _wakeRegistration?.Unregister(null);
        _wake?.Dispose();
        _instance?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>Ramène le configurateur à l'écran, sur un second lancement.</summary>
    /// <summary>
    /// Réveille l'exécution déjà en cours, parce qu'on a relancé l'application
    /// alors qu'elle tournait encore.
    ///
    /// Elle peut n'avoir plus rien à l'écran : fermer les fenêtres de jeu à la
    /// main ne l'arrête pas tant que le panneau est affiché, et masquer le
    /// panneau ensuite la laisse vivante et invisible. Relancer redonnait alors
    /// le panneau sans rouvrir les instances, ce qui n'est pas ce qu'on attend
    /// d'un relancement. Elles reviennent donc, comme au démarrage, et les
    /// fenêtres fermées depuis le panneau restent fermées puisqu'elles ne sont
    /// plus dans l'ensemble de démarrage.
    /// </summary>
    private void RevealConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Show();
        _configurator.Activate();

        _ = ReopenIfNothingIsRunningAsync();
    }

    private async Task ReopenIfNothingIsRunningAsync()
    {
        var services = _host?.Services;

        if (services is null)
        {
            return;
        }

        var launcher = services.GetRequiredService<GameLauncher>();

        if (launcher.ActiveSessions.Count > 0)
        {
            return;
        }

        try
        {
            await launcher.LaunchEnabledAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La reprise de la session a échoué.");
        }
    }

    /// <summary>Vrai si la fenêtre appartient à l'application.</summary>
    private bool OwnsWindow(nint handle)
    {
        if (_configurator is not null && handle == _configurator.Handle)
        {
            return true;
        }

        if (_quests is not null && _quests.Handle == handle)
        {
            return true;
        }

        // Les pages liées, qu'on ouvre par un clic dans un guide. Sans elles,
        // les raccourcis mourraient dès qu'une de ces fenêtres a le focus, ce
        // qui arrive précisément quand on lit.
        //
        // Par un ensemble de poignées et non par la liste des fenêtres : cette
        // question est posée depuis le guet du premier plan, qui ne vit pas sur
        // le fil de l'interface.
        return QuestPageWindow.Owns(handle);
    }

    /// <summary>
    /// Montre ou cache le suivi de quêtes. La fenêtre est construite au premier
    /// appel seulement : elle porte un navigateur, qu'il serait inutile de
    /// mettre en route pour quelqu'un qui ne s'en sert pas.
    /// </summary>
    private void ToggleQuests() => Quests()?.Toggle();

    /// <summary>Rouvre le suivi de quêtes sur ce qu'on y lisait au dernier arrêt.</summary>
    private async Task RestoreQuestsAsync(string? url, int step)
    {
        if (Quests() is not { } quests)
        {
            return;
        }

        await quests.RestoreAsync(url, step).ConfigureAwait(true);
    }

    /// <summary>
    /// Les guides, construits au premier besoin.
    ///
    /// Ils comptent au même titre que le panneau pour décider s'il reste
    /// quelque chose : les masquer alors qu'il ne reste rien d'autre arrête
    /// l'application, comme masquer le panneau.
    /// </summary>
    private QuestWindow? Quests()
    {
        if (_quests is not null)
        {
            return _quests;
        }

        _quests = _host?.Services.GetRequiredService<QuestWindow>();

        if (_quests is null)
        {
            return null;
        }

        // Le panneau était déjà masqué, sans quoi on ne serait pas en train de
        // s'arrêter : c'est bien qu'on le veut masqué.
        _quests.IsVisibleChanged += (_, _) =>
        {
            if (_started && _quests?.IsVisible == false)
            {
                OnNothingLeft(rememberConfigurator: false);
            }
        };

        return _quests;
    }

    /// <summary>
    /// Dit en une ligne où en est la mise à jour. Le bandeau ne paraît que
    /// lorsqu'il y a quelque chose à dire.
    /// </summary>
    private static void ShowUpdateState(UpdateService updates, ConfiguratorViewModel model)
    {
        model.UpdateText = updates.Available is not { } release
            ? string.Empty
            : updates.Ready
                ? Strings.Format("UpdateReadyBanner", release.Version)
                : Strings.Format("UpdateAvailableBanner", release.Version);
    }

    /// <summary>Ouvre la note de version, posée sur le panneau s'il est là.</summary>
    private void ShowNotes(string heading, string lead, string notes)
    {
        if (notes.Length == 0)
        {
            return;
        }

        var window = new UpdateWindow(heading, lead, notes);

        if (_configurator?.IsVisible == true)
        {
            window.Owner = _configurator;
        }

        window.Show();
    }

    private void ToggleConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Toggle();
    }

    /// <summary>
    /// L'arrêt, quelle qu'en soit la porte.
    ///
    /// Rien n'y est attendu à la façon ordinaire. WPF appelle cette méthode
    /// depuis son propre arrêt, et coupe le répartiteur dès qu'elle rend la
    /// main : or un « await » la lui rend au premier travail qui ne se termine
    /// pas sur place, et la suite serait alors postée sur un répartiteur mort.
    ///
    /// La boucle de messages tourne donc pendant l'attente, comme à la fin de
    /// session Windows, et une minuterie la borne : mieux vaut un arrêt à
    /// moitié rangé qu'une application qui refuse de mourir.
    ///
    /// On craignait que ce qui suit la fermeture des fenêtres de jeu, dont la
    /// pose de la mise à jour, ne s'exécute jamais dès qu'il y a vraiment des
    /// fenêtres à fermer, la sonde d'alors n'ayant mesuré que le cas où il n'y
    /// en avait aucune. Mesuré depuis, deux comptes ouverts sur un vrai
    /// téléphone : les fenêtres se ferment en six cent vingt-six millisecondes
    /// et l'arrêt entier tient en six cent trente-quatre, sur une borne de huit
    /// secondes. Le tour est joué par la boucle pompée, et les deux durées sont
    /// désormais journalisées : la question ne se reposera pas à l'aveugle.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        var frame = new DispatcherFrame();
        var limit = new DispatcherTimer(
            ShutdownLimit, DispatcherPriority.Send, (_, _) => frame.Continue = false, Dispatcher);

        limit.Start();

        _ = CloseDownAsync().ContinueWith(
            _ => frame.Continue = false,
            TaskScheduler.FromCurrentSynchronizationContext());

        Dispatcher.PushFrame(frame);
        limit.Stop();

        base.OnExit(e);
    }

    private async Task CloseDownAsync()
    {
        // Chronométré, et pas par curiosité : tout ce qui suit la fermeture des
        // fenêtres de jeu, dont la pose de la mise à jour, ne s'exécute que si
        // cette fermeture rend la main avant la borne de ShutdownLimit. Le cas
        // avec des fenêtres ouvertes n'avait jamais été mesuré, faute d'en
        // avoir jamais eu la trace.
        var start = System.Diagnostics.Stopwatch.StartNew();

        if (_host is not null)
        {
            try
            {
                // Les fenêtres de jeu sont fermées avec l'application : les
                // laisser ouvertes sans configurateur n'aurait pas de sens.
                var launcher = _host.Services.GetRequiredService<GameLauncher>();
                var windows = launcher.ActiveSessions.Count;

                await launcher.CloseAllAsync().ConfigureAwait(true);

                Log.Information(
                    "Arrêt : {windows} fenêtre(s) de jeu fermée(s) en {elapsed} ms.",
                    windows,
                    start.ElapsedMilliseconds);

                // La mise à jour se pose ici et nulle part ailleurs : plus rien
                // ne tourne, et l'exécutable qui se renomme n'interrompt
                // personne. Elle démarrera au prochain lancement.
                if (_host.Services.GetRequiredService<UpdateService>().Apply())
                {
                    Log.Information("Mise à jour posée, elle démarrera au prochain lancement.");
                }

                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // L'arrêt fait au mieux : ce qui échoue ici n'empêche pas de
                // partir, et l'application se ferme de toute façon.
                Log.Warning(exception, "Arrêt incomplet.");
            }

            _host.Dispose();
        }

        Dispose();

        Log.Information("Arrêt rangé en {elapsed} ms.", start.ElapsedMilliseconds);

        await Log.CloseAndFlushAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Construit l'hôte. Les journaux vont dans le dossier de données, avec
    /// rotation quotidienne et un plafond en nombre de fichiers.
    /// </summary>
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDtHub();

        var paths = builder.Services.BuildServiceProvider().GetRequiredService<IAppPaths>();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()

            // L'identifiant du lancement, sur chaque ligne : c'est ce qui
            // permet à un rapport de ne prendre que la session en cours dans un
            // fichier où se mêlent tous les démarrages de la journée.
            .Enrich.WithProperty("Session", AppSession.Id)
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "dthub-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 8 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Session}] "
                    + "{SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        return builder.Build();
    }

    /// <summary>
    /// Ouvre la session nommée du démarrage, s'il y en a une.
    ///
    /// Le journal dit ce qui a été retenu, et c'est indispensable : un
    /// démarrage qui n'ouvre pas ce qu'on attend n'a sinon aucune trace, et l'on
    /// ne sait pas distinguer un profil mal enregistré d'un compte disparu du
    /// téléphone.
    /// </summary>
    private static async Task ApplyDefaultLaunchProfileAsync(SettingsService settings)
    {
        var name = await settings.GetDefaultLaunchProfileAsync().ConfigureAwait(true);

        if (name is null)
        {
            Log.Information("Démarrage sans session nommée : on rouvre ce qui était ouvert.");
            return;
        }

        var keys = await settings.ApplyLaunchProfileAsync(name).ConfigureAwait(true);

        if (keys.Count == 0)
        {
            Log.Warning(
                "La session « {Profile} » n'ouvre aucun compte : elle a disparu des réglages, "
                + "ou tous ses comptes ont été retirés du téléphone.",
                name);

            return;
        }

        Log.Information("Session « {Profile} » retenue : {Count} compte(s).", name, keys.Count);
    }
}
