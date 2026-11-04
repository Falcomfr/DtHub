using System.Net;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Papycha;
using DtHub.Core.Processes;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Users;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Android;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Devices;
using DtHub.Infrastructure.Papycha;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;
using DtHub.Mac.Pc;
using DtHub.Mac.Platform;
using DtHub.Mac.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

namespace DtHub.Mac;

/// <summary>
/// The PC's composition (src/DtHub.App/AppServices.cs), service for
/// service, with the Mac's own where Windows has its: window control
/// through the Accessibility API, the dialogs, adb and scrcpy found where
/// they are installed. Left out until the Mac has them: updates (the PC's
/// install a Windows executable), the guides' web view, desktop shortcuts.
/// </summary>
internal static class MacServices
{
    public static ServiceProvider Build()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "dthub-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        // Locations and persistence.
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton(CreateStore<AppSettingsDocument>(paths.SettingsFile));
        services.AddSingleton(CreateStore<DeviceRegistryDocument>(paths.DevicesFile));
        services.AddSingleton(CreateStore<QuestCatalogDocument>(paths.QuestCatalogFile));
        services.AddSingleton(CreateStore<TouchNamesDocument>(paths.TouchNamesFile));
        services.AddSingleton(CreateStore<ItemSourcesDocument>(Path.Combine(paths.CacheDirectory, "dofus-touch-sources.json")));
        services.AddSingleton<SettingsService>();

        // Process execution.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Third-party components. adb and scrcpy come from where the
        // player installed them, not from the PC's Windows archives.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();
        services.AddSingleton<IAdbLocator>(new InstalledTool("adb", "android-platform-tools"));
        services.AddSingleton<IScrcpyLocator>(new InstalledTool("scrcpy", "scrcpy"));
        services.AddSingleton<ToolPreparation>();

        // Quest guides and the game's names, as on the PC.
        services.AddSingleton<IPapychaClient>(provider => new PapychaClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            provider.GetRequiredService<ILogger<PapychaClient>>()));
        services.AddSingleton<IDofusTouchClient>(provider => new DofusTouchClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            provider.GetRequiredService<ILogger<DofusTouchClient>>()));
        services.AddSingleton<IPapychaTracker>(provider => new PapychaTracker(
            new HttpClient(new SocketsHttpHandler
            {
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
            })
            { Timeout = TimeSpan.FromSeconds(30) },
            provider.GetRequiredService<ILogger<PapychaTracker>>()));
        services.AddSingleton<IQuestCatalogSnapshot, EmbeddedQuestCatalogSnapshot>();
        services.AddSingleton<QuestCatalogService>();
        services.AddSingleton<ITouchIdTableSource, EmbeddedTouchIdTable>();
        services.AddSingleton<TouchNameService>();
        services.AddSingleton<ItemSourceService>();

        // Phones.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IUsbEnumerationInspector, MacUsbInspector>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<IAddressProbe, TcpAddressProbe>();
        services.AddSingleton<AdbDeviceWatch>();
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton<DeviceReconnectService>();
        services.AddSingleton<AndroidUserService>();

        // Game.
        services.AddSingleton<DofusInstanceService>();
        services.AddSingleton<IAppIconProvider, AppIconProvider>();
        services.AddSingleton<IAppLauncher, AndroidAppLauncher>();
        services.AddSingleton<AppRestartService>();
        services.AddSingleton<ScrcpySessionManager>();

        // Windows and shortcuts.
        services.AddSingleton<IWindowController, MacWindowController>();
        services.AddSingleton<WindowManagerService>();
        services.AddSingleton<IHotkeyRegistrar, MacHotkeyRegistrar>();

        // Interface.
        services.AddSingleton<IDialogService, MacDialogService>();
        services.AddSingleton<DiagnosticReporter>();
        services.AddSingleton<GameLauncher>();
        services.AddSingleton<WindowPlacements>();
        services.AddSingleton<InstanceListViewModel>();
        services.AddSingleton<ConfiguratorViewModel>();
        services.AddTransient<AddDeviceViewModel>();
        services.AddTransient<AddDeviceWindow>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<HelpWindow>();
        services.AddTransient<SleepHelpViewModel>();
        services.AddTransient<SleepHelpWindow>();
        services.AddTransient<InputHelpViewModel>();
        services.AddTransient<InputHelpWindow>();
        services.AddTransient<HotkeyEditorViewModel>();

        // A missing registration fails at launch, not on the first click.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(string path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));
}
