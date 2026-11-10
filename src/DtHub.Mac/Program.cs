using Avalonia;

using DtHub.Infrastructure.Storage;
using DtHub.Mac.Platform;

namespace DtHub.Mac;

internal static class Program
{
    /// <summary>Linux's claim to be the only DT Hub; null elsewhere.</summary>
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // macOS keeps one copy of an app by itself; Linux does not, and a second copy would open every account twice.
        if (OperatingSystem.IsLinux() && (Instance = SingleInstance.TryClaim(new AppPaths().Root)) is null)
        {
            return 0;
        }

        // scrcpy's SDL3 goes native Wayland where it can, and Wayland ignores the place DT Hub gives each game window.
        if (OperatingSystem.IsLinux()
            && Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 }
            && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
        {
            Environment.SetEnvironmentVariable("SDL_VIDEO_DRIVER", "x11");
        }

        // The exit code says whether startup failed (MacApp.StartAsync shuts down with 1).
        return AppBuilder.Configure<MacApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}
