using System.IO;
using System.Net.Http;
using System.Reflection;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
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
using DtHub.Core.Updates;
using DtHub.Core.Users;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Android;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Devices;
using DtHub.Infrastructure.Hotkeys;
using DtHub.Infrastructure.Papycha;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Scrcpy;
using DtHub.Infrastructure.Storage;
using DtHub.Infrastructure.Updates;
using DtHub.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DtHub.App;

/// <summary>
/// Service composition. A single place describes how the pieces
/// fit together.
/// </summary>
public static class AppServices
{
    public static IServiceCollection AddDtHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Locations and persistence.
        services.AddSingleton<IAppPaths>(_ =>
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            return paths;
        });

        services.AddSingleton(CreateStore<AppSettingsDocument>(p => p.SettingsFile));
        services.AddSingleton(CreateStore<DeviceRegistryDocument>(p => p.DevicesFile));
        services.AddSingleton(CreateStore<QuestCatalogDocument>(p => p.QuestCatalogFile));

        services.AddSingleton<SettingsService>();

        // Process execution.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Third-party components.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();

        // Quest guides. Its own client: the one above waits ten
        // minutes, sized for an eleven-megabyte archive, which would
        // make the application look frozen if the site did not
        // respond.
        services.AddSingleton<IPapychaClient>(provider => new PapychaClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            provider.GetRequiredService<ILogger<PapychaClient>>()));
        // Updates. Its own client: a sixty-megabyte executable does
        // not download in the time allotted to a web page.
        services.AddSingleton<IReleaseSource>(provider => new GitHubReleaseSource(
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) },
            ReleaseChannel.Owner,
            ReleaseChannel.Repository,
            provider.GetRequiredService<ILogger<GitHubReleaseSource>>()));
        services.AddSingleton(_ => new UpdateTarget(
            ReleaseParser.Normalize(ReleaseParser.VersionOf(
                Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    .Split('+')[0])),
            Environment.ProcessPath ?? string.Empty));
        services.AddSingleton<UpdateService>();
        services.AddSingleton<WebViewEnvironment>();
        services.AddSingleton<IShortcutWriter, Win32ShortcutWriter>();

        services.AddSingleton<IQuestSuccessSeed, EmbeddedQuestSuccessSeed>();
        services.AddSingleton<QuestCatalogService>();
        services.AddSingleton<IAdbLocator, AdbLocator>();
        services.AddSingleton<IScrcpyLocator, ScrcpyLocator>();
        services.AddSingleton<ToolPreparation>();

        // Phones.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IUsbEnumerationInspector, WindowsUsbInspector>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<IAddressProbe, TcpAddressProbe>();
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
        services.AddSingleton<IWindowController, Win32WindowController>();
        services.AddSingleton<WindowManagerService>();
        services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();

        // Interface.
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<DiagnosticReporter>();
        services.AddSingleton<GameLauncher>();

        services.AddSingleton<InstanceListViewModel>();
        services.AddTransient<AddDeviceViewModel>();
        services.AddTransient<AddDeviceWindow>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<HelpWindow>();
        services.AddTransient<SleepHelpViewModel>();
        services.AddTransient<SleepHelpWindow>();
        services.AddTransient<InputHelpViewModel>();
        services.AddTransient<InputHelpWindow>();
        services.AddSingleton<QuestViewModel>();
        services.AddSingleton<WindowPlacements>();
        services.AddSingleton<QuestWindow>();

        // Separate and disposable: a linked page has nothing to
        // remember from one opening to the next, and several may be
        // wanted side by side.
        services.AddTransient<QuestPageWindow>();
        services.AddTransient<AlmanaxWindow>();
        services.AddTransient<HotkeyEditorViewModel>();
        services.AddTransient<HotkeyEditorWindow>();
        services.AddSingleton<ConfiguratorViewModel>();
        services.AddSingleton<ConfiguratorWindow>();

        return services;
    }

    /// <summary>Builds a JSON store for a given document.</summary>
    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(Func<IAppPaths, string> path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path(provider.GetRequiredService<IAppPaths>()),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));

}
