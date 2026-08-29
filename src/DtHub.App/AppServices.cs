using System.IO;
using System.Net.Http;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Apps;
using DtHub.Core.Dependencies;
using DtHub.Core.Devices;
using DtHub.Core.Hotkeys;
using DtHub.Core.Processes;
using DtHub.Core.Profiles;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
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
/// s'assemblent, ce qui rend la structure du programme lisible d'un coup.
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
        services.AddSingleton(CreateStore<ProfileDocument>(p => p.ProfilesFile));
        services.AddSingleton(CreateStore<AppCatalogDocument>(p => Path.Combine(p.CacheDirectory, "apps.json")));

        services.AddSingleton<SettingsService>();

        // Exécution de processus.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Composants tiers.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
        services.AddSingleton<IDependencyProvisioner, ArchiveDependencyProvisioner>();

        services.AddSingleton<IAdbLocator>(provider => new AdbLocator(
            provider.GetRequiredService<IDependencyProvisioner>(),
            provider.GetRequiredService<ILogger<AdbLocator>>(),
            () => provider.GetRequiredService<SettingsService>()
                .GetAsync(CancellationToken.None).GetAwaiter().GetResult().CustomAdbPath));

        services.AddSingleton<IScrcpyLocator, ScrcpyLocator>();

        // ADB et appareils.
        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<DevicePairingService>();
        services.AddSingleton<DeviceReconnectService>();
        services.AddSingleton<Core.Users.AndroidUserService>();

        // Applications.
        services.AddSingleton<IAppLabelProvider, ScrcpyAppLabelProvider>();
        services.AddSingleton<AppDiscoveryService>();
        services.AddSingleton<IAppLauncher, AndroidAppLauncher>();

        // Profils, sessions, fenêtres, raccourcis.
        services.AddSingleton<ProfileService>();
        services.AddSingleton<ScrcpySessionManager>();
        services.AddSingleton<IWindowController, Win32WindowController>();
        services.AddSingleton<WindowManagerService>();
        services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();

        // Interface.
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<DiagnosticsService>();
        services.AddSingleton<SessionOrchestrator>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<DevicesViewModel>();
        services.AddSingleton<AppsViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }

    /// <summary>
    /// Construit un dépôt JSON pour un document donné. Le chemin est résolu à
    /// partir des emplacements de l'application.
    /// </summary>
    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(Func<IAppPaths, string> path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path(provider.GetRequiredService<IAppPaths>()),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));

    /// <summary>Nom de produit exposé à l'interface.</summary>
    public static string ProductName => ProductInfo.Name;
}
