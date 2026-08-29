using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Storage;

namespace DtHub.Core.Apps;

/// <summary>
/// Dresse la liste des applications lançables d'un téléphone, profil Android
/// par profil Android. Le balayage coûte plusieurs secondes : le résultat est
/// donc mis en cache sur disque et n'est refait que sur demande.
/// </summary>
public sealed class AppDiscoveryService : IDisposable
{
    private readonly IAdbClient _adb;
    private readonly IAppLabelProvider _labels;
    private readonly IDocumentStore<AppCatalogDocument> _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppDiscoveryService(
        IAdbClient adb,
        IAppLabelProvider labels,
        IDocumentStore<AppCatalogDocument> cache)
    {
        _adb = adb;
        _labels = labels;
        _cache = cache;
    }

    /// <summary>
    /// Un balayage de paquets peut être long sur un téléphone chargé, d'où un
    /// délai plus généreux que pour les commandes ordinaires.
    /// </summary>
    public TimeSpan ScanTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Nombre de résolutions d'activité menées de front lorsqu'il faut passer
    /// par le repli, paquet par paquet.
    /// </summary>
    public int MaxParallelism { get; init; } = 6;

    /// <summary>
    /// Applications lançables pour un profil. Rend le contenu du cache tant
    /// qu'un rafraîchissement n'est pas demandé.
    /// </summary>
    public async Task<IReadOnlyList<AndroidApp>> GetAppsAsync(
        string deviceId,
        string serial,
        int userId,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        if (!refresh)
        {
            var cached = await ReadCacheAsync(deviceId, userId, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return cached;
            }
        }

        var apps = await ScanAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        await WriteCacheAsync(deviceId, userId, apps, cancellationToken).ConfigureAwait(false);

        return apps;
    }

    /// <summary>Date du dernier balayage pour ce profil, ou <c>null</c>.</summary>
    public async Task<DateTimeOffset?> GetLastScanAsync(
        string deviceId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);

        return document.Devices.TryGetValue(deviceId, out var device)
               && device.Users.TryGetValue(UserKey(userId), out var user)
            ? user.UpdatedUtc
            : null;
    }

    /// <summary>Oublie le catalogue d'un appareil, par exemple quand il est retiré.</summary>
    public async Task ForgetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);

            if (document.Devices.Remove(deviceId))
            {
                await _cache.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Résout l'activité à lancer pour un paquet donné. Utilisé au moment du
    /// lancement quand le catalogue ne porte pas encore le composant.
    /// </summary>
    public async Task<AppComponent?> ResolveLaunchComponentAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                [
                    "cmd", "package", "resolve-activity", "--brief",
                    "--user", UserKey(userId),
                    "-c", "android.intent.category.LAUNCHER",
                    packageName,
                ],
                ScanTimeout,
                cancellationToken).ConfigureAwait(false);

            return AppListParser.ParseComponents(output)
                .FirstOrDefault(c => string.Equals(c.PackageName, packageName, StringComparison.Ordinal));
        }
        catch (AdbException)
        {
            return null;
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<IReadOnlyList<AndroidApp>> ScanAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken)
    {
        var components = await QueryLauncherComponentsAsync(serial, userId, cancellationToken)
            .ConfigureAwait(false);

        if (components.Count == 0)
        {
            components = await ResolveComponentsOneByOneAsync(serial, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var systemPackages = await ListPackagesAsync(serial, userId, systemOnly: true, cancellationToken)
            .ConfigureAwait(false);

        var labels = await ReadLabelsAsync(serial, cancellationToken).ConfigureAwait(false);

        var apps = components
            .GroupBy(c => c.PackageName, StringComparer.Ordinal)
            .Select(group => new AndroidApp
            {
                PackageName = group.Key,
                UserId = userId,
                Label = labels.GetValueOrDefault(group.Key),
                LaunchComponent = group.First().Value,
                IsSystem = systemPackages.Contains(group.Key),
            })
            .OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return apps;
    }

    /// <summary>
    /// Interroge en une fois toutes les activités lançables du profil. C'est
    /// le chemin rapide, disponible sur les versions récentes d'Android.
    /// </summary>
    private async Task<IReadOnlyList<AppComponent>> QueryLauncherComponentsAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                [
                    "cmd", "package", "query-activities", "--brief",
                    "--user", UserKey(userId),
                    "-a", "android.intent.action.MAIN",
                    "-c", "android.intent.category.LAUNCHER",
                ],
                ScanTimeout,
                cancellationToken).ConfigureAwait(false);

            return AppListParser.ParseComponents(output);
        }
        catch (AdbException)
        {
            return [];
        }
    }

    /// <summary>
    /// Repli pour les appareils où l'interrogation groupée n'existe pas : on
    /// liste les paquets, puis on résout l'activité de chacun. C'est nettement
    /// plus lent, d'où l'exécution en parallèle borné.
    /// </summary>
    private async Task<IReadOnlyList<AppComponent>> ResolveComponentsOneByOneAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken)
    {
        var packages = await ListPackagesAsync(serial, userId, systemOnly: false, cancellationToken)
            .ConfigureAwait(false);

        if (packages.Count == 0)
        {
            return [];
        }

        var resolved = new System.Collections.Concurrent.ConcurrentBag<AppComponent>();

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, MaxParallelism),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(packages, options, async (package, token) =>
        {
            var component = await ResolveLaunchComponentAsync(serial, userId, package, token)
                .ConfigureAwait(false);

            if (component is not null)
            {
                resolved.Add(component);
            }
        }).ConfigureAwait(false);

        return [.. resolved];
    }

    private async Task<HashSet<string>> ListPackagesAsync(
        string serial,
        int userId,
        bool systemOnly,
        CancellationToken cancellationToken)
    {
        List<string> arguments = ["pm", "list", "packages", "--user", UserKey(userId)];
        if (systemOnly)
        {
            arguments.Add("-s");
        }

        try
        {
            var output = await _adb.ShellAsync(serial, arguments, ScanTimeout, cancellationToken)
                .ConfigureAwait(false);

            return [.. AppListParser.ParsePackageList(output)];
        }
        catch (AdbException)
        {
            // Sans cette liste, les applications apparaissent simplement comme
            // non système : c'est un défaut d'étiquetage, pas une panne.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadLabelsAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _labels.GetLabelsAsync(serial, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Un nom manquant n'empêche rien : le nom de paquet prend le
            // relais. On ne perd pas la liste pour autant.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private async Task<IReadOnlyList<AndroidApp>?> ReadCacheAsync(
        string deviceId,
        int userId,
        CancellationToken cancellationToken)
    {
        var document = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (!document.Devices.TryGetValue(deviceId, out var device)
            || !device.Users.TryGetValue(UserKey(userId), out var user)
            || user.Apps.Count == 0)
        {
            return null;
        }

        return [.. user.Apps.Select(app => app.ToApp(userId))];
    }

    private async Task WriteCacheAsync(
        string deviceId,
        int userId,
        IReadOnlyList<AndroidApp> apps,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await _cache.LoadAsync(cancellationToken).ConfigureAwait(false);

            if (!document.Devices.TryGetValue(deviceId, out var device))
            {
                device = new DeviceCatalog();
                document.Devices[deviceId] = device;
            }

            device.Users[UserKey(userId)] = new UserCatalog
            {
                UpdatedUtc = DateTimeOffset.UtcNow,
                Apps = [.. apps.Select(StoredApp.From)],
            };

            document.SchemaVersion = AppCatalogDocument.CurrentSchemaVersion;

            await _cache.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string UserKey(int userId) => userId.ToString(CultureInfo.InvariantCulture);
}
