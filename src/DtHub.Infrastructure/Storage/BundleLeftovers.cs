using System.Diagnostics;

namespace DtHub.Infrastructure.Storage;

/// <summary>
/// The folders left in %TEMP% by previous versions.
///
/// The single file compresses its managed components and
/// decompresses them in memory, but WPF's six native libraries and
/// the WebView2 bootstrapper require a real path on disk. The host
/// therefore drops them under %TEMP%\.net\DtHub\{identifier}, where
/// the identifier is recomputed with every publish. An update thus
/// leaves the previous version's folder behind, eight megabytes
/// each, and nothing ever picks them up again: measured at a
/// hundred and sixty-one folders for one gigabyte and three
/// hundred megabytes on the development machine.
///
/// The README claimed that deleting the file left nothing behind:
/// that was true of the data folder, false of this one.
/// </summary>
public static class BundleLeftovers
{
    /// <summary>
    /// The folder where the host drops the native libraries: it is
    /// that of a library actually loaded, not a reconstructed path.
    /// </summary>
    private static readonly string Mark =
        $"{Path.DirectorySeparatorChar}.net{Path.DirectorySeparatorChar}";

    /// <summary>
    /// Extraction folder of the current version, or <c>null</c>
    /// outside the single file. In development nothing is
    /// extracted, and there is nothing to sweep.
    ///
    /// The detection does not target any particular library: they
    /// load on demand, and targeting wpfgfx_cor3.dll found nothing
    /// until the first window had been drawn.
    /// </summary>
    public static string? CurrentDirectory()
    {
        using var self = Process.GetCurrentProcess();

        foreach (ProcessModule module in self.Modules)
        {
            using (module)
            {
                if (module.FileName is { } file
                    && file.Contains(Mark, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetDirectoryName(file);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The sibling folders to remove: all except the current
    /// version's. The comparison follows Windows's file system,
    /// which is not case-sensitive.
    /// </summary>
    public static IReadOnlyList<string> Stale(IEnumerable<string> siblings, string current)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        return siblings
            .Where(directory => !string.Equals(
                directory.TrimEnd('\\'), current?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Removes what previous versions left behind. Returns the
    /// number of folders deleted.
    ///
    /// A folder still in use resists on its own: Windows refuses to
    /// delete a loaded library. The failure is therefore swallowed,
    /// it says nothing more than "not this one".
    /// </summary>
    public static int Sweep()
    {
        if (CurrentDirectory() is not { } current)
        {
            return 0;
        }

        var root = Path.GetDirectoryName(current);
        if (root is null || !Directory.Exists(root))
        {
            return 0;
        }

        var removed = 0;

        foreach (var directory in Stale(Directory.EnumerateDirectories(root), current))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Folder of an instance still alive, or locked by
                // the antivirus: it will wait for the next startup.
            }
        }

        return removed;
    }
}
