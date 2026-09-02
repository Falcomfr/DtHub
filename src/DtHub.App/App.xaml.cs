using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Updates;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Updates;
using DtHub.Core.Storage;
using DtHub.Infrastructure.Processes;
using DtHub.Core.Scrcpy;

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


    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Les fenêtres de jeu ne sont pas des fenêtres WPF : l'application ne
        // doit pas se fermer quand le configurateur est masqué.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!ClaimSingleInstance())
        {
            Shutdown();
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
        catch (Exception exception)
        {
            Report(exception, "Le démarrage a échoué.");
            Shutdown(1);
        }
    }

    /// <summary>Enchaîne mise en route éventuelle, lancement, puis configurateur.</summary>
    private async Task RunAsync()
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var launcher = services.GetRequiredService<GameLauncher>();

        await KillOrphansAsync(services).ConfigureAwait(true);

        launcher.IconDirectory = WindowIcons.EnsureDirectory(services.GetRequiredService<IAppPaths>());

        PlaceShortcut(services);

        var current = await settings.GetAsync().ConfigureAwait(true);

        // La question n'est posée qu'au premier lancement. Ne plus rien avoir
        // à ouvrir est un état normal depuis que l'ensemble de démarrage est
        // celui des fenêtres ouvertes à la sortie.
        if (!current.SetupCompleted)
        {
            var setup = services.GetRequiredService<SetupWindow>();
            MainWindow = setup;

            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        // Le configurateur existe avant le lancement : c'est lui qui affichera
        // les problèmes s'il y en a.
        _configurator = services.GetRequiredService<ConfiguratorWindow>();

        // Les raccourcis ne sont actifs que si une fenêtre à nous est au
        // premier plan. Sans y ajouter le suivi de quêtes, ils mourraient dès
        // qu'on lui donne le focus, ce qui est précisément ce qu'on fait pour
        // lire un guide.
        SessionEnding += OnSessionEnding;

        launcher.OwnsWindow = OwnsWindow;
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

        var report = await launcher.LaunchEnabledAsync().ConfigureAwait(true);

        _shape.Tick += (_, _) => launcher.Watch();
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

        if (!StartupPresence.ShowConfigurator(document.ConfiguratorVisible, report.Opened))
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
                    ? "Cette version est prête. Elle s'installera quand vous quitterez."
                    : "Cette version est disponible.",
                ReleaseNotes.Readable(updates.Available?.Notes)));

        if (updates.TakeNotes() is { Length: > 0 } installed)
        {
            ShowNotes(
                $"DT Hub {updates.Running}",
                "Cette version vient d'être installée.",
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
    private static async Task KillOrphansAsync(IServiceProvider services)
    {
        try
        {
            var path = await services.GetRequiredService<IScrcpyLocator>()
                .GetScrcpyPathAsync().ConfigureAwait(true);

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
                ? $"Version {release.Version} prête, elle s'installera en quittant."
                : $"Version {release.Version} disponible.";
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
    /// Mesuré à la sonde : aujourd'hui tout s'exécute, mais seulement parce que
    /// la fermeture des fenêtres de jeu se termine d'un trait quand il n'y en a
    /// aucune. Avec des fenêtres ouvertes, elle attend vraiment, et ce qui suit
    /// ne se ferait plus. Ce qui suit, c'est la pose de la mise à jour.
    ///
    /// La boucle de messages tourne donc pendant l'attente, comme à la fin de
    /// session Windows, et une minuterie la borne : mieux vaut un arrêt à
    /// moitié rangé qu'une application qui refuse de mourir.
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
        if (_host is not null)
        {
            try
            {
                // Les fenêtres de jeu sont fermées avec l'application : les
                // laisser ouvertes sans configurateur n'aurait pas de sens.
                await _host.Services.GetRequiredService<GameLauncher>().CloseAllAsync().ConfigureAwait(true);

                // La mise à jour se pose ici et nulle part ailleurs : plus rien
                // ne tourne, et l'exécutable qui se renomme n'interrompt
                // personne. Elle démarrera au prochain lancement.
                if (_host.Services.GetRequiredService<UpdateService>().Apply())
                {
                    Log.Information("Mise à jour posée, elle démarrera au prochain lancement.");
                }

                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Arrêt incomplet.");
            }

            _host.Dispose();
        }

        Dispose();

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
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "dthub-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 8 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        return builder.Build();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // L'interface reste vivante : une erreur d'affichage ne doit pas
        // fermer les fenêtres de jeu.
        e.Handled = true;
        Report(e.Exception, "Une erreur inattendue s'est produite.");
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Exception non interceptée.");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exception de tâche non observée.");
        e.SetObserved();
    }

    /// <summary>
    /// Journalise le détail technique et n'affiche qu'un message
    /// compréhensible, avec le chemin des journaux.
    /// </summary>
    private void Report(Exception exception, string headline)
    {
        Log.Fatal(exception, "{Headline}", headline);

        var logs = _host?.Services.GetService<IAppPaths>()?.LogsDirectory;

        var details = string.IsNullOrEmpty(logs)
            ? string.Empty
            : $"\n\nLe détail se trouve dans les journaux :\n{logs}";

        MessageBox.Show(
            $"{headline}{details}",
            ProductInfo.Name,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
