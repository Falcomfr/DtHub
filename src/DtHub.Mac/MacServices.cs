using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Processes;
using DtHub.Core.Scrcpy;
using DtHub.Core.Sessions;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Users;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Dependencies;
using DtHub.Infrastructure.Devices;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DtHub.Mac;

/// <summary>
/// The part of the Windows composition (DtHub.App/AppServices.cs) that
/// opens accounts side by side. Guides, updates, hotkeys and window
/// control are left out until the Mac has its own.
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
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton(CreateStore<AppSettingsDocument>(paths.SettingsFile));
        services.AddSingleton(CreateStore<DeviceRegistryDocument>(paths.DevicesFile));
        services.AddSingleton<SettingsService>();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();

        // Installed through Homebrew rather than downloaded: the manifest
        // only knows the Windows archives.
        services.AddSingleton<IAdbLocator>(new InstalledTool("adb", "android-platform-tools"));
        services.AddSingleton<IScrcpyLocator>(new InstalledTool("scrcpy", "scrcpy"));

        services.AddSingleton<IAdbClient, AdbClient>();
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<DeviceDiscoveryService>();
        services.AddSingleton<AndroidUserService>();
        services.AddSingleton<DofusInstanceService>();
        services.AddSingleton<IAppLauncher, AndroidAppLauncher>();
        services.AddSingleton<ScrcpySessionManager>();

        // A missing registration fails at launch, not on the first click.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static Func<IServiceProvider, IDocumentStore<T>> CreateStore<T>(string path)
        where T : class, new() =>
        provider => new JsonDocumentStore<T>(
            path,
            provider.GetRequiredService<ILoggerFactory>().CreateLogger($"Store.{typeof(T).Name}"));
}
