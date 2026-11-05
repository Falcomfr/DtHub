using DtHub.Infrastructure.Storage;

namespace DtHub.Tests.Storage;

public class AppPathsTests
{
    // XDG_DATA_HOME is Linux's: on a Mac, .NET answers ~/Library/Application
    // Support whatever it says.
    [LinuxFact]
    public void Un_dossier_de_donnees_absent_ne_donne_pas_un_chemin_relatif()
    {
        // Seen on 2026-11-05 on a fresh Linux home: without ~/.local/share,
        // .NET answered an empty path and DT Hub wrote its tools, logs and
        // settings into "DtHub" under whatever folder it was started from.
        var missing = Path.Combine(Path.GetTempPath(), "dthub-xdg-" + Guid.NewGuid().ToString("N"));
        var before = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", missing);

        try
        {
            var paths = new AppPaths();

            Assert.True(Path.IsPathRooted(paths.Root), paths.Root);
            Assert.StartsWith(missing, paths.Root, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", before);
            if (Directory.Exists(missing))
            {
                Directory.Delete(missing, recursive: true);
            }
        }
    }
}
