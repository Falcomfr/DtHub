// Development probe: passes a real settings file through the shipped
// reading and migration logic, and reports what becomes of it.
//
// It serves to put to the test a file that cannot be committed to
// the repository: the user's file carries device identifiers, and
// none of that belongs in a test.
//
// To run: dotnet run --project build/sonde-reglages -- <file path>
using DtHub.Core.Settings;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging.Abstractions;

if (args.Length == 0)
{
    Console.WriteLine("Usage : sonde-reglages <chemin du fichier de réglages>");

    return 1;
}

var source = args[0];

if (!File.Exists(source))
{
    Console.WriteLine($"Fichier introuvable : {source}");

    return 1;
}

// A copy, in a folder to be discarded: reading rewrites the migrated
// file, and there is no question of touching the original.
var directory = Path.Combine(Path.GetTempPath(), "dthub-sonde-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);

var copy = Path.Combine(directory, "settings.json");
File.Copy(source, copy);

try
{
    using var store = new JsonDocumentStore<AppSettingsDocument>(copy, NullLogger.Instance);
    using var service = new SettingsService(store);

    var settings = await service.GetAsync(CancellationToken.None);

    Console.WriteLine($"schéma        : {settings.SchemaVersion}");
    Console.WriteLine($"zoom          : {settings.GameZoom}");
    Console.WriteLine($"qualité       : {settings.Quality}");
    Console.WriteLine($"taille libre  : {settings.CustomSizePercent}");
    Console.WriteLine($"instances     : {settings.Instances.Count}");
    Console.WriteLine($"raccourcis    : {settings.Hotkeys.Count}");
    Console.WriteLine($"tailles       : {string.Join(", ", settings.SizePercentages)}");

    var quarantined = Directory.GetFiles(directory, "*.corrompu-*");

    Console.WriteLine($"quarantaine   : {(quarantined.Length == 0 ? "aucune" : quarantined[0])}");

    return 0;
}
finally
{
    Directory.Delete(directory, recursive: true);
}
