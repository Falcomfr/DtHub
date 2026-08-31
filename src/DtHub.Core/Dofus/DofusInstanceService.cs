using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Users;

namespace DtHub.Core.Dofus;

/// <summary>
/// Trouve les instances du jeu sur les téléphones connectés : une par profil
/// Android où le paquet est installé. C'est tout ce que l'application a besoin
/// de savoir des applications installées, elle ne dresse aucun catalogue.
/// </summary>
public sealed class DofusInstanceService
{
    private readonly IAdbClient _adb;
    private readonly AndroidUserService _users;

    public DofusInstanceService(IAdbClient adb, AndroidUserService users)
    {
        _adb = adb;
        _users = users;
    }

    /// <summary>
    /// Paquet recherché. Réglable pour survivre à un changement côté éditeur,
    /// mais l'application est pensée pour celui-ci.
    /// </summary>
    public string PackageName { get; set; } = DofusPackages.DofusTouch;

    /// <summary>Un balayage de paquets peut traîner sur un téléphone chargé.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    private readonly List<string> _warnings = [];

    /// <summary>
    /// Incidents non bloquants du dernier balayage. Un appareil dont la liste
    /// de profils n'a pas pu être lue rend quand même une instance, celle du
    /// profil principal : sans un mot, rien ne distingue ce cas d'un appareil
    /// qui n'a réellement qu'un profil, et le second compte semble avoir
    /// disparu.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Instances présentes sur les téléphones donnés. Un téléphone hors ligne
    /// n'est pas interrogé : ses instances mémorisées sont réinjectées par
    /// l'appelant.
    /// </summary>
    public async Task<IReadOnlyList<DofusInstance>> DiscoverAsync(
        IEnumerable<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        List<DofusInstance> instances = [];
        _warnings.Clear();

        foreach (var device in devices.Where(d => d.IsConnected))
        {
            cancellationToken.ThrowIfCancellationRequested();

            instances.AddRange(
                await DiscoverOnDeviceAsync(device, cancellationToken).ConfigureAwait(false));
        }

        return instances;
    }

    /// <summary>Instances présentes sur un téléphone précis.</summary>
    public async Task<IReadOnlyList<DofusInstance>> DiscoverOnDeviceAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        List<DofusInstance> instances = [];

        var users = await _users.GetUsersAsync(device.Serial, refresh: false, cancellationToken)
            .ConfigureAwait(false);

        if (AndroidUserService.IsFallback(users))
        {
            _warnings.Add(
                $"{device.DisplayName} : la liste des profils Android n'a pas pu être lue. "
                + "Seul le profil principal est visible ; un jeu installé dans un second "
                + "espace n'apparaîtra pas.");
        }

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packages = await ListInstalledAsync(device.Serial, user.Id, cancellationToken)
                .ConfigureAwait(false);

            foreach (var package in packages)
            {
                var component = await ResolveComponentAsync(
                    device.Serial, user.Id, package, cancellationToken).ConfigureAwait(false);

                instances.Add(new DofusInstance
                {
                    DeviceId = device.Id,
                    DeviceName = device.DisplayName,
                    UserId = user.Id,
                    UserName = user.DisplayName,
                    PackageName = package,
                    LaunchComponent = component?.Value,
                    IsDeviceConnected = true,
                });
            }
        }

        return instances;
    }

    /// <summary>Vrai si le jeu est installé pour ce profil Android.</summary>
    public async Task<bool> IsInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default) =>
        (await ListInstalledAsync(serial, userId, cancellationToken).ConfigureAwait(false)).Count > 0;

    /// <summary>
    /// Paquets du jeu présents pour ce profil Android.
    ///
    /// Le nom exact d'abord, qui est le cas de très loin le plus courant : le
    /// clonage par profil, celui que l'application vise, garde le nom du paquet
    /// intact. Mais certaines surcouches installent leur copie sous un nom
    /// dérivé, et la comparaison stricte les rendait invisibles alors que la
    /// commande les avait bien rapportées. On les accepte donc en second, à
    /// condition que le nom contienne le paquet cherché ou son dernier segment.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> found;

        try
        {
            // « pm list packages » filtre par sous-chaîne : le dernier segment
            // ramène aussi bien le paquet officiel que ses copies renommées.
            var output = await _adb.ShellAsync(
                serial,
                ["pm", "list", "packages", "--user", Text(userId), BaseToken],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            found = PackageParser.ParsePackageList(output);
        }
        catch (AdbException)
        {
            // Un profil qui refuse la question est simplement considéré comme
            // dépourvu du jeu : rien ne justifie de faire échouer le balayage.
            return [];
        }

        List<string> matches = [];

        if (found.Contains(PackageName, StringComparer.Ordinal))
        {
            matches.Add(PackageName);
        }

        matches.AddRange(found.Where(IsDerived).Order(StringComparer.Ordinal));

        return matches;
    }

    /// <summary>Résout l'activité à lancer pour un profil Android.</summary>
    public async Task<AppComponent?> ResolveComponentAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default) =>
        await ResolveComponentAsync(serial, userId, PackageName, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Résout l'activité à lancer pour un profil Android et un paquet précis.
    /// Une copie renommée n'a pas le nom du paquet de référence : la résoudre
    /// sous ce nom-là ne donnerait rien.
    /// </summary>
    public async Task<AppComponent?> ResolveComponentAsync(
        string serial,
        int userId,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        try
        {
            var output = await _adb.ShellAsync(
                serial,
                [
                    "cmd", "package", "resolve-activity", "--brief",
                    "--user", Text(userId),
                    "-c", "android.intent.category.LAUNCHER",
                    packageName,
                ],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            return PackageParser.ParseComponents(output)
                .FirstOrDefault(c => string.Equals(c.PackageName, packageName, StringComparison.Ordinal));
        }
        catch (AdbException)
        {
            return null;
        }
    }

    /// <summary>
    /// Dernier segment du nom de paquet, celui qui identifie le jeu sans
    /// l'éditeur. Sert de filtre à la commande et de marque des copies.
    /// </summary>
    private string BaseToken
    {
        get
        {
            var index = PackageName.LastIndexOf('.');

            return index >= 0 && index < PackageName.Length - 1
                ? PackageName[(index + 1)..]
                : PackageName;
        }
    }

    /// <summary>Vrai pour une copie du jeu installée sous un nom dérivé.</summary>
    private bool IsDerived(string package) =>
        !string.Equals(package, PackageName, StringComparison.Ordinal)
        && (package.Contains(PackageName, StringComparison.Ordinal)
            || package.Contains(BaseToken, StringComparison.Ordinal));

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Paquets du jeu.</summary>
public static class DofusPackages
{
    /// <summary>Paquet officiel de DOFUS Touch.</summary>
    public const string DofusTouch = "com.ankama.dofustouch";
}
