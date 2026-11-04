using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using DtHub.Core.Devices;
using DtHub.Core.Dofus;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace DtHub.Mac;

internal sealed class MacApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = MacServices.Build();
            var sessions = services.GetRequiredService<ScrcpySessionManager>();

            desktop.MainWindow = new MainWindow(
                services.GetRequiredService<DeviceDiscoveryService>(),
                services.GetRequiredService<DofusInstanceService>(),
                sessions,
                services.GetRequiredService<SettingsService>());

            // No job object on macOS to take the scrcpy windows down with
            // us: they are closed here, or they would outlive the app.
            desktop.Exit += (_, _) =>
            {
                sessions.StopAllAsync().GetAwaiter().GetResult();
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
