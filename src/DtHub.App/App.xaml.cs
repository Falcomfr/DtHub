using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Settings;
using DtHub.Core.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;

namespace DtHub.App;

/// <summary>
/// Point d'entrée. Monte l'hôte, la journalisation et le thème, puis ouvre la
/// fenêtre principale. Toute exception non interceptée est journalisée et
/// présentée à l'utilisateur : l'application ne disparaît jamais sans un mot.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = BuildHost();
            await _host.StartAsync().ConfigureAwait(true);

            var settings = await _host.Services.GetRequiredService<SettingsService>()
                .GetAsync().ConfigureAwait(true);

            _host.Services.GetRequiredService<ThemeManager>().Apply(settings.Theme);

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            ReportFatal(exception, "Le démarrage a échoué.");
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Arrêt de l'hôte incomplet.");
            }

            _host.Dispose();
        }

        await Log.CloseAndFlushAsync().ConfigureAwait(true);

        base.OnExit(e);
    }

    /// <summary>
    /// Construit l'hôte. Les journaux vont dans le dossier de données de
    /// l'utilisateur, avec rotation quotidienne et un plafond en nombre de
    /// fichiers pour ne pas grossir indéfiniment.
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
        // L'interface reste vivante : une erreur dans une page ne doit pas
        // faire disparaître l'application.
        e.Handled = true;
        ReportFatal(e.Exception, "Une erreur inattendue s'est produite.");
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
    /// Journalise le détail technique et n'affiche à l'utilisateur qu'un
    /// message compréhensible, avec le chemin des journaux.
    /// </summary>
    private void ReportFatal(Exception exception, string headline)
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
