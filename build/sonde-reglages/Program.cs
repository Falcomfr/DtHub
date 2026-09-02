// Sonde de développement : passe un vrai fichier de réglages par la lecture et
// les migrations livrées, et dit ce qu'il devient.
//
// Elle sert à éprouver un fichier qu'on ne peut pas verser au dépôt : celui de
// l'utilisateur porte des identifiants d'appareil, et rien de tout cela n'a à
// figurer dans un test.
//
// À lancer : dotnet run --project build/sonde-reglages -- <chemin du fichier>
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

// Une copie, dans un dossier à jeter : la lecture réécrit le fichier migré, et
// il n'est pas question de toucher à l'original.
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
    Console.WriteLine($"mise en route : {settings.SetupCompleted}");
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
