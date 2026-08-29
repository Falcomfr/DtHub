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

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await IsInstalledAsync(device.Serial, user.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var component = await ResolveComponentAsync(device.Serial, user.Id, cancellationToken)
                .ConfigureAwait(false);

            instances.Add(new DofusInstance
            {
                DeviceId = device.Id,
                DeviceName = device.DisplayName,
                UserId = user.Id,
                UserName = user.DisplayName,
                PackageName = PackageName,
                LaunchComponent = component?.Value,
                IsDeviceConnected = true,
            });
        }

        return instances;
    }

    /// <summary>Vrai si le jeu est installé pour ce profil Android.</summary>
    public async Task<bool> IsInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                ["pm", "list", "packages", "--user", Text(userId), PackageName],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            return PackageParser.ParsePackageList(output)
                .Contains(PackageName, StringComparer.Ordinal);
        }
        catch (AdbException)
        {
            // Un profil qui refuse la question est simplement considéré comme
            // dépourvu du jeu : rien ne justifie de faire échouer le balayage.
            return false;
        }
    }

    /// <summary>Résout l'activité à lancer pour un profil Android.</summary>
    public async Task<AppComponent?> ResolveComponentAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                [
                    "cmd", "package", "resolve-activity", "--brief",
                    "--user", Text(userId),
                    "-c", "android.intent.category.LAUNCHER",
                    PackageName,
                ],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            return PackageParser.ParseComponents(output)
                .FirstOrDefault(c => string.Equals(c.PackageName, PackageName, StringComparison.Ordinal));
        }
        catch (AdbException)
        {
            return null;
        }
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Paquets du jeu.</summary>
public static class DofusPackages
{
    /// <summary>Paquet officiel de DOFUS Touch.</summary>
    public const string DofusTouch = "com.ankama.dofustouch";
}
