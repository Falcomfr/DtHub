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
/// Composition des services. Un seul endroit décrit comment les pièces
/// s'assemblent.
/// </summary>
public static class AppServices
{
    public static IServiceCollection AddDtHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Emplacements et persistance.
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

        // Exécution de processus.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Composants tiers.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();

        // Guides de quêtes. Son propre client : celui du dessus attend dix
        // minutes, taillé pour une archive de onze mégaoctets, ce qui ferait
        // paraître l'application figée si le site ne répondait pas.
        services.AddSingleton<IPapychaClient>(provider => new PapychaClient(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) },
            provider.GetRequiredService<ILogger<PapychaClient>>()));
        // Mises à jour. Son propre client : un exécutable de soixante mégaoctets
        // ne se télécharge pas dans le temps qu'on accorde à une page web.
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

        // Téléphones.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton<DeviceReconnectService>();
        services.AddSingleton<AndroidUserService>();

        // Jeu.
        services.AddSingleton<DofusInstanceService>();
        services.AddSingleton<IAppIconProvider, AppIconProvider>();
        services.AddSingleton<IAppLauncher, AndroidAppLauncher>();
        services.AddSingleton<AppRestartService>();
        services.AddSingleton<ScrcpySessionManager>();

        // Fenêtres et raccourcis.
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
        services.AddSingleton<QuestViewModel>();
        services.AddSingleton<WindowPlacements>();
        services.AddSingleton<QuestWindow>();

        // À part et jetable : une page liée n'a rien à retenir d'une ouverture
        // à l'autre, et on peut en vouloir plusieurs côte à côte.
        services.AddTransient<QuestPageWindow>();
        services.AddTransient<HotkeyEditorViewModel>();
        services.AddTransient<HotkeyEditorWindow>();
        services.AddSingleton<ConfiguratorViewModel>();
        services.AddSingleton<ConfiguratorWindow>();

        return services;
    }

    /// <summary>Construit un dépôt JSON pour un document donné.</summary>
    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(Func<IAppPaths, string> path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path(provider.GetRequiredService<IAppPaths>()),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));

    /// <summary>Chemin d'un fichier du cache, pour les composants qui en veulent un.</summary>
    public static string CacheFile(IAppPaths paths, string name) =>
        Path.Combine(paths.CacheDirectory, name);
}
