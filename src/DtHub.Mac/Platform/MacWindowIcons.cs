using Avalonia.Platform;

using DtHub.Core.Storage;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's WindowIcons (src/DtHub.App/Services/WindowIcons.cs): scrcpy
/// takes its window icon from SCRCPY_ICON_DIR, and DT Hub's is given there,
/// so the game windows wear DT Hub's icon rather than scrcpy's. On macOS
/// that icon is also the one each scrcpy shows in the Dock.
/// </summary>
internal static class MacWindowIcons
{
    private const string IconFile = "scrcpy.png";

    public static string? EnsureDirectory(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            var directory = Path.Combine(paths.CacheDirectory, "icons");
            Directory.CreateDirectory(directory);

            // Rewritten on every startup, as on the PC: a first copy kept
            // would freeze an old image after a rebrand.
            using var source = AssetLoader.Open(new Uri("avares://DtHub.Mac/assets/app.png"));
            using var file = File.Create(Path.Combine(directory, IconFile));
            source.CopyTo(file);

            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Deliberate silence, as on the PC: the icon is a convenience,
            // and without it the window keeps scrcpy's own.
            _ = exception;
            return null;
        }
    }
}
