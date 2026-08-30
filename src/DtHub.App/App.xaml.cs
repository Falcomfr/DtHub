using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using DtHub.App.Services;
using DtHub.App.Windows;
using DtHub.Core;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
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
    private bool _quitting;
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

            _host.Services.GetRequiredService<ThemeManager>().ApplySystemTheme();

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
        launcher.OwnsWindow = handle => _configurator is not null && handle == _configurator.Handle;
        launcher.ConfiguratorToggleRequested += (_, _) => Dispatcher.Invoke(ToggleConfigurator);
        launcher.QuitRequested += (_, _) => Dispatcher.Invoke(async () => await RequestQuitAsync().ConfigureAwait(true));
        launcher.LastWindowClosed += (_, _) => Dispatcher.Invoke(OnLastWindowClosed);

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
        _configurator.PlaceAwayFrom(document.GameAnchor);
        _configurator.Opacity = 1;

        if (!StartupPresence.ShowConfigurator(document.ConfiguratorVisible, report.Opened))
        {
            _configurator.Hide();
        }

        if (!report.AnyOpened)
        {
            Log.Warning(
                "Aucune fenêtre ouverte, le configurateur reste affiché : {Problems}",
                report.Problems.Count > 0
                    ? string.Join(" ", report.Problems)
                    : "aucune instance à ouvrir.");
        }
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
        await SaveSessionStateAsync().ConfigureAwait(true);

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
    private async Task SaveSessionStateAsync()
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

            await settings.SetConfiguratorVisibleAsync(
                _configurator?.IsVisible == true).ConfigureAwait(true);
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
    private void OnLastWindowClosed()
    {
        // Deux sessions qui meurent ensemble signalent chacune la dernière.
        if (_quitting || _configurator?.IsVisible == true)
        {
            return;
        }

        _quitting = true;

        Log.Information("Dernière fenêtre de jeu fermée, configurateur masqué : arrêt.");

        // La géométrie des fenêtres vient d'être perdue avec elles : il ne
        // reste rien à relever. Le reste de l'état, lui, doit être retenu,
        // sans quoi le configurateur masqué se rouvrirait au lancement suivant.
        _ = SaveSessionStateAsync().ContinueWith(
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
    private void RevealConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Show();
        _configurator.Activate();
    }

    private void ToggleConfigurator()
    {
        if (_configurator is null)
        {
            return;
        }

        _configurator.Toggle();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                // Les fenêtres de jeu sont fermées avec l'application : les
                // laisser ouvertes sans configurateur n'aurait pas de sens.
                await _host.Services.GetRequiredService<GameLauncher>().CloseAllAsync().ConfigureAwait(true);
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

        base.OnExit(e);
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
