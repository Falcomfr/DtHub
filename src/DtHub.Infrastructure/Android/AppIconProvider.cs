using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Storage;

using Microsoft.Extensions.Logging;

namespace DtHub.Infrastructure.Android;

/// <summary>
/// Extrait l'icône d'une application depuis son archive sur le téléphone, et
/// la garde.
///
/// Trois commandes suffisent, et aucune ne transfère l'archive : <c>pm path</c>
/// dit où elle est, <c>unzip -l</c> dit ce qu'elle contient, et
/// <c>exec-out unzip -p</c> en tire la seule entrée voulue. Mesuré sur le jeu
/// visé, dix-sept kilooctets passent pour une archive de quatorze mégaoctets
/// qui, elle, ne bouge pas.
///
/// Rien de tout cela n'est indispensable, et c'est ce qui dicte la forme :
/// chaque échec rend <c>null</c> en silence.
/// </summary>
public sealed partial class AppIconProvider : IAppIconProvider
{
    private readonly IAdbClient _adb;
    private readonly IAppPaths _paths;
    private readonly ILogger<AppIconProvider> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _known = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<string?>> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset?> _absent = new(StringComparer.Ordinal);

    public AppIconProvider(IAdbClient adb, IAppPaths paths, ILogger<AppIconProvider> logger)
    {
        _adb = adb;
        _paths = paths;
        _logger = logger;
    }

    /// <summary>
    /// Délai avant de retenter une extraction qui a échoué pour une raison
    /// passagère. Un téléphone en pleine reconnexion ne doit pas condamner son
    /// icône jusqu'au prochain lancement.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Le temps laissé à chaque commande. L'archive est lue, pas transférée.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public string? Find(string deviceId, string packageName)
    {
        var key = Key(deviceId, packageName);

        lock (_gate)
        {
            if (_known.TryGetValue(key, out var path))
            {
                return path;
            }
        }

        var file = FileFor(key);

        if (!File.Exists(file))
        {
            return null;
        }

        lock (_gate)
        {
            _known[key] = file;
        }

        return file;
    }

    public Task<string?> GetAsync(AppIconRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId)
            || string.IsNullOrWhiteSpace(request.Serial)
            || string.IsNullOrWhiteSpace(request.PackageName))
        {
            return Task.FromResult<string?>(null);
        }

        if (Find(request.DeviceId, request.PackageName) is { } known)
        {
            return Task.FromResult<string?>(known);
        }

        var key = Key(request.DeviceId, request.PackageName);

        lock (_gate)
        {
            // Une absence définitive ne se retente pas : le téléphone ne
            // changera pas d'avis sur une archive sans icône matricielle.
            if (_absent.TryGetValue(key, out var since))
            {
                if (since is null || DateTimeOffset.UtcNow - since.Value < RetryDelay)
                {
                    return Task.FromResult<string?>(null);
                }

                _absent.Remove(key);
            }

            // Deux profils du même téléphone demandent la même icône au même
            // instant : une seule extraction part, les deux l'attendent.
            if (_running.TryGetValue(key, out var running))
            {
                return running;
            }

            var work = ExtractAsync(request, key, cancellationToken);
            _running[key] = work;

            return work;
        }
    }

    private async Task<string?> ExtractAsync(
        AppIconRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = await ExtractCoreAsync(request, key, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                if (path is not null)
                {
                    _known[key] = path;
                }
            }

            return path;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Y compris une annulation : l'appelant ne l'attend pas, et rien
            // ne doit remonter d'une tâche que personne ne surveille.
            LogFailed(request.PackageName, exception.Message);

            lock (_gate)
            {
                _absent[key] = DateTimeOffset.UtcNow;
            }

            return null;
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(key);
            }
        }
    }

    private async Task<string?> ExtractCoreAsync(
        AppIconRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var user = request.UserId.ToString(CultureInfo.InvariantCulture);

        var paths = ApkListing.ParsePaths(await _adb.ShellAsync(
            request.Serial,
            ["pm", "path", "--user", user, request.PackageName],
            Timeout,
            cancellationToken).ConfigureAwait(false));

        if (paths.Count == 0)
        {
            Forget(key, "le paquet n'est pas installé sur ce profil", request.PackageName);

            return null;
        }

        foreach (var apk in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var quoted = AndroidShell.Quote(apk);

            // Le listage est restreint aux dossiers d'icônes : une archive de
            // quatorze mégaoctets en compte des milliers d'entrées, et les
            // faire toutes transiter pour en garder six serait absurde.
            var listing = await _adb.ShellAsync(
                request.Serial,
                ["unzip", "-l", quoted, "'res/mipmap*'"],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            if (LauncherIconChoice.Choose(ApkListing.ParseEntries(listing)) is not { } entry)
            {
                continue;
            }

            // Sans citation, contrairement au listage juste au-dessus, et ce
            // n'est pas un oubli. « adb shell » fait passer la commande par un
            // shell qui retire les citations ; « adb exec-out » remet les
            // arguments tels quels, si bien qu'une apostrophe ajoutée devient
            // une partie du nom de fichier. Mesuré : citée, l'archive rendait
            // « couldn't open ... : I/O error » ; nue, elle rend ses cinquante
            // et un kilooctets. C'est aussi plus sûr, rien n'étant réinterprété.
            var bytes = await _adb.ExecOutAsync(
                request.Serial,
                ["unzip", "-p", apk, entry],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            if (!bytes.Succeeded || !IsPng(bytes.StandardOutput))
            {
                continue;
            }

            var file = Write(key, bytes.StandardOutput);

            if (file is not null)
            {
                LogExtracted(request.PackageName, entry, bytes.StandardOutput.Length);
            }

            return file;
        }

        Forget(key, "aucune icône matricielle dans l'archive", request.PackageName);

        return null;
    }

    /// <summary>
    /// Retient une absence définitive : ni le paquet ni son archive ne
    /// changeront tant que l'application tourne.
    /// </summary>
    private void Forget(string key, string reason, string packageName)
    {
        LogAbsent(packageName, reason);

        lock (_gate)
        {
            _absent[key] = null;
        }
    }

    /// <summary>
    /// Un PNG et rien d'autre. Sans ce contrôle, le message d'erreur qu'un
    /// <c>unzip</c> écrit sur sa sortie standard finirait dans un fichier
    /// nommé « .png » que l'affichage refuserait ensuite en silence.
    /// </summary>
    private static bool IsPng(byte[] content) =>
        content.Length > 8
        && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47
        && content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A;

    private string? Write(string key, byte[] content)
    {
        try
        {
            var file = FileFor(key);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, content);

            return file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(key, exception.Message);

            return null;
        }
    }

    private string FileFor(string key) =>
        Path.Combine(_paths.CacheDirectory, "icons", "apps", Readable(key) + ".png");

    private static string Key(string deviceId, string packageName) =>
        deviceId + "|" + packageName;

    /// <summary>
    /// Un nom de fichier lisible, suivi d'une empreinte de la clef exacte.
    ///
    /// Lisible pour qu'on sache de quoi il s'agit en ouvrant le dossier ;
    /// suivi d'une empreinte parce que l'identité d'un appareil sans fil qui
    /// n'a jamais répondu est son adresse, « 192.168.1.25:5555 », dont les
    /// deux-points sont interdits dans un nom de fichier Windows. Les remplacer
    /// ferait se confondre deux appareils voisins ; l'empreinte les sépare.
    /// </summary>
    private static string Readable(string key)
    {
        var cleaned = new StringBuilder(key.Length);

        foreach (var character in key)
        {
            cleaned.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '_');
        }

        var hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

        var head = cleaned.ToString();

        return (head.Length > 60 ? head[^60..] : head) + "-" + hash;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Icône de {package} extraite de {entry} : {bytes} octets.")]
    private partial void LogExtracted(string package, string entry, int bytes);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Pas d'icône pour {package} : {reason}.")]
    private partial void LogAbsent(string package, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Icône de {package} non extraite : {reason}")]
    private partial void LogFailed(string package, string reason);
}
