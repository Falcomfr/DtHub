using System.IO;
using System.Net.Http;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core.Adb;
using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Hotkeys;
using DtHub.Core.Processes;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Users;
using DtHub.Core.Windows;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Devices;
using DtHub.Infrastructure.Hotkeys;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Scrcpy;
using DtHub.Infrastructure.Storage;
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

        services.AddSingleton<SettingsService>();

        // Exécution de processus.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Composants tiers.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();
        services.AddSingleton<IAdbLocator>(provider => new AdbLocator(
            provider.GetRequiredService<IDependencyProvisioner>(),
            provider.GetRequiredService<ILogger<AdbLocator>>()));
        services.AddSingleton<IScrcpyLocator, ScrcpyLocator>();

        // Téléphones.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton<DeviceReconnectService>();
        services.AddSingleton<AndroidUserService>();

        // Jeu.
        services.AddSingleton<DofusInstanceService>();
        services.AddSingleton<IAppLauncher, AndroidAppLauncher>();
        services.AddSingleton<ScrcpySessionManager>();

        // Fenêtres et raccourcis.
        services.AddSingleton<IWindowController, Win32WindowController>();
        services.AddSingleton<WindowManagerService>();
        services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();

        // Interface.
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<GameLauncher>();

        services.AddSingleton<InstanceListViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddTransient<AddDeviceViewModel>();
        services.AddTransient<AddDeviceWindow>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<HelpWindow>();
        services.AddTransient<CloneHelpViewModel>();
        services.AddTransient<CloneHelpWindow>();
        services.AddTransient<HotkeyEditorViewModel>();
        services.AddTransient<HotkeyEditorWindow>();
        services.AddSingleton<ConfiguratorViewModel>();
        services.AddSingleton<ConfiguratorWindow>();
        services.AddTransient<SetupWindow>();

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
