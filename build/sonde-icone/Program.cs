// Sonde de développement : extrait l'icône d'une application par le code livré.
//
// Elle court-circuite la mise en place d'ADB, déjà faite sur cette machine, et
// n'éprouve donc que ce qui nous intéresse : les trois commandes et l'écriture.
//
// dotnet run --project build/sonde-icone -- <serial> [paquet]
using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Infrastructure.Adb;
using DtHub.Infrastructure.Android;
using DtHub.Infrastructure.Processes;
using DtHub.Infrastructure.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

var serial = args.Length > 0 ? args[0] : string.Empty;
var package = args.Length > 1 ? args[1] : "com.ankama.dofustouch";

var paths = new AppPaths();
var adbPath = Path.Combine(
    paths.ToolsDirectory, "platform-tools-37.0.1", "platform-tools", "adb.exe");

var adb = new AdbClient(new ProcessRunner(), new FixedLocator(adbPath), NullLogger<AdbClient>.Instance);
var provider = new AppIconProvider(adb, paths, NullLogger<AppIconProvider>.Instance);

Console.WriteLine($"adb     : {adbPath} ({(File.Exists(adbPath) ? "présent" : "ABSENT")})");
Console.WriteLine($"serial  : {serial}");
Console.WriteLine($"paquet  : {package}");

// Les trois commandes, une par une, pour voir laquelle bloque le cas échéant.
var listing = await adb.ShellAsync(serial, ["pm", "path", "--user", "0", package], null, default);
Console.WriteLine($"pm path : {ApkListing.ParsePaths(listing).Count} archive(s)");

foreach (var apk in ApkListing.ParsePaths(listing))
{
    var quoted = AndroidShell.Quote(apk);
    var entries = ApkListing.ParseEntries(
        await adb.ShellAsync(serial, ["unzip", "-l", quoted, "'res/mipmap*'"], null, default));

    Console.WriteLine($"  {apk[(apk.LastIndexOf('/') + 1)..]} : {entries.Count} entrée(s), choix = {LauncherIconChoice.Choose(entries) ?? "(aucun)"}");
}

// L'extraction elle-même, en détail.
foreach (var apk in ApkListing.ParsePaths(listing).Take(1))
{
    var quoted = AndroidShell.Quote(apk);
    var entry = LauncherIconChoice.Choose(ApkListing.ParseEntries(
        await adb.ShellAsync(serial, ["unzip", "-l", quoted, "'res/mipmap*'"], null, default)))!;

    var bytes = await adb.ExecOutAsync(serial, ["unzip", "-p", apk, entry], null, default);

    Console.WriteLine($"exec-out: code {bytes.ExitCode}, {bytes.StandardOutput.Length} octet(s), erreur « {bytes.StandardError.Trim()} »");

    if (bytes.StandardOutput.Length > 0)
    {
        Console.WriteLine($"          premiers octets : {Convert.ToHexString(bytes.StandardOutput[..8])}");
    }


}

var started = DateTimeOffset.UtcNow;
var path = await provider.GetAsync(new AppIconRequest("SONDE", serial, 0, package), default);

Console.WriteLine($"résultat: {path ?? "(rien)"}");
Console.WriteLine($"durée   : {(DateTimeOffset.UtcNow - started).TotalMilliseconds:F0} ms");

if (path is not null && File.Exists(path))
{
    Console.WriteLine($"octets  : {new FileInfo(path).Length}");
}

return path is null ? 1 : 0;

/// <summary>Un chemin d'ADB déjà connu, sans mise en place.</summary>
internal sealed class FixedLocator(string path) : IAdbLocator
{
    public string? TryGetInstalledPath() => path;

    public Task<string> GetAdbPathAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(path);
}
