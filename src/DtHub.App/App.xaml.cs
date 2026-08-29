using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using DtHub.App.Services;
using DtHub.App.Windows;
using DtHub.Core;
using DtHub.Core.Settings;
using DtHub.Core.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace DtHub.App;

/// <summary>
/// Point d'entrée. Deux cas seulement : premier lancement, on demande quelles
/// instances ouvrir ; ensuite, on ouvre directement celles qui sont cochées.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private ConfiguratorWindow? _configurator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Les fenêtres de jeu ne sont pas des fenêtres WPF : l'application ne
        // doit pas se fermer quand le configurateur est masqué.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

        var current = await settings.GetAsync().ConfigureAwait(true);

        if (!current.SetupCompleted || !current.Instances.Any(i => i.IsEnabled))
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

        var report = await launcher.LaunchEnabledAsync().ConfigureAwait(true);

        var placement = (await settings.GetAsync().ConfigureAwait(true)).GameAnchor;
        _configurator.Show();
        _configurator.PlaceAwayFrom(placement);

        if (!report.AnyOpened && report.Problems.Count > 0)
        {
            Log.Warning("Aucune fenêtre ouverte : {Problems}", string.Join(" ", report.Problems));
        }
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
