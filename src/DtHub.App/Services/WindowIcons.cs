using System.IO;
using System.Windows;

using DtHub.Core.Storage;

namespace DtHub.App.Services;

/// <summary>
/// Prepares the icon the game windows will carry.
///
/// scrcpy reads it from a folder designated by an environment
/// variable, and looks there for a file with a fixed name. The
/// image is extracted from the application's resources: in a
/// single-file publish, there is no file on disk to point to.
/// </summary>
public static class WindowIcons
{
    private const string IconFile = "scrcpy.png";

    /// <summary>
    /// Folder ready for use, or <c>null</c> if the icon could not
    /// be written. The windows then keep scrcpy's own icon: this
    /// is not a reason to refuse to start.
    /// </summary>
    public static string? EnsureDirectory(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        try
        {
            var directory = Path.Combine(paths.CacheDirectory, "icons");
            var target = Path.Combine(directory, IconFile);

            // Rewritten on every startup: keeping the first copy
            // would freeze the old image after a rebrand.
            var source = Application.GetResourceStream(new Uri("assets/app.png", UriKind.Relative));

            if (source is null)
            {
                return null;
            }

            Directory.CreateDirectory(directory);

            using var stream = source.Stream;
            using var file = File.Create(target);
            stream.CopyTo(file);

            return directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Deliberate silence: the icon is a convenience.
            // Without it the window keeps the system's own, and
            // nothing else depends on it.
            return null;
        }
    }
}
