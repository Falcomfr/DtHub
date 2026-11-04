using System.Net;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
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
/// through the Accessibility API, the dialogs, the web view, adb and scrcpy
/// from the manifest's macOS archives. Left out until the Mac has them:
/// updates (the PC's install a Windows executable), desktop shortcuts.
/// </summary>
internal static class MacServices
{
    public static ServiceProvider Build()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // The PC's logger (App.xaml.cs BuildHost), minus its debugger sink:
        // the launch identifier on every line is what lets a problem report
        // take only the current session from a day's file, and the lines
        // are written in the player's language.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Session", DtHub.Core.Diagnostics.AppSession.Id)
            .WriteTo.File(
                new DtHub.Infrastructure.Diagnostics.LocalizedLogFormatter(
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Session}] "
                    + "{SourceContext} {Message:lj}{NewLine}{Exception}",
                    System.Globalization.CultureInfo.InvariantCulture),
                Path.Combine(paths.LogsDirectory, "dthub-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 8 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true)
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

        // Third-party components. On macOS the manifest's archives, downloaded
        // on first need as on the PC, the copy the player installed only when
        // that download fails; scrcpy is always handed this adb. On Linux the
        // player's install (scrcpy's official archive, which carries adb): the
        // manifest has no Linux archives.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();

        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IAdbLocator>(provider => Provisioned(
                provider,
                DependencyManifest.PlatformToolsMacKey,
                new InstalledTool("adb", Strings.Format("InstallWithBrew", "android-platform-tools"))));
            services.AddSingleton<IScrcpyLocator>(provider => Provisioned(
                provider,
                DependencyManifest.ScrcpyMacKey,
                new InstalledTool("scrcpy", Strings.Format("InstallWithBrew", "scrcpy"))));
        }
        else
        {
            var linuxHint = Strings.Get("InstallLinuxTools");
            services.AddSingleton<IAdbLocator>(new InstalledTool("adb", linuxHint));
            services.AddSingleton<IScrcpyLocator>(new InstalledTool("scrcpy", linuxHint));
        }

        services.AddSingleton<MacToolPreparation>();

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
        // macOS's own (libDtHubMac.dylib); on Linux passive stand-ins.
        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IWindowController, MacWindowController>();
            services.AddSingleton<IHotkeyRegistrar, MacHotkeyRegistrar>();
        }
        else
        {
            services.AddSingleton<IWindowController, PassiveWindowController>();
            services.AddSingleton<IHotkeyRegistrar, PassiveHotkeyRegistrar>();
        }

        services.AddSingleton<WindowManagerService>();

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
        services.AddTransient<Views.HotkeyEditorWindow>();

        // The web windows, as the PC's: the guides kept for the session,
        // the linked pages, the Almanax and the web tools built each time.
        services.AddSingleton<QuestViewModel>();
        services.AddSingleton<QuestTrackerViewModel>();
        services.AddSingleton<QuestWindow>();
        services.AddTransient<QuestPageWindow>();
        services.AddTransient<AlmanaxWindow>();
        services.AddTransient<WebToolWindow>();
        services.AddSingleton<Func<WebToolWindow>>(p => () => p.GetRequiredService<WebToolWindow>());

        // A missing registration fails at launch, not on the first click.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static ProvisionedTool Provisioned(IServiceProvider provider, string key, InstalledTool fallback) =>
        new(
            provider.GetRequiredService<IDependencyProvisioner>(),
            DependencyManifest.Get(key),
            fallback,
            provider.GetRequiredService<ILogger<ProvisionedTool>>());

    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(string path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));
}
