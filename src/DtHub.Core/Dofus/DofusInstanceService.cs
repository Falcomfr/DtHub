using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Localization;
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

    private readonly Dictionary<string, IReadOnlyList<int>> _profiles = new(StringComparer.Ordinal);

    /// <summary>
    /// Les profils Android relevés sur chaque téléphone, par identifiant
    /// d'appareil, du dernier balayage.
    ///
    /// Seulement les téléphones dont la liste a été lue pour de bon : un
    /// appareil qui n'a pas répondu n'y figure pas, et l'on ne conclura donc
    /// rien de son absence. C'est ce qui permet de distinguer « ce profil a
    /// disparu » de « on n'a pas pu regarder ».
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> ScannedProfiles => _profiles;

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
        _profiles.Clear();

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

        // Relus à chaque balayage, et non pris au cache : un profil supprimé
        // sur le téléphone restait sinon connu indéfiniment, et sa ligne ne
        // quittait jamais la liste. La commande est légère au regard du reste
        // du balayage, qui interroge les paquets de chaque profil.
        var users = await _users.GetUsersAsync(device.Serial, refresh: true, cancellationToken)
            .ConfigureAwait(false);

        if (AndroidUserService.IsFallback(users))
        {
            _warnings.Add(
                Strings.Format("ProfileListUnreadable", device.DisplayName));
        }
        else
        {
            // Lue pour de bon : on saura dire qu'un profil a disparu, et non
            // seulement qu'on ne l'a pas vu.
            _profiles[device.Id] = [.. users.Select(u => u.Id)];
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
    /// <summary>
    /// Ajoute un compte : un profil Android neuf, le jeu dedans, et le profil
    /// démarré pour qu'on puisse l'ouvrir tout de suite.
    ///
    /// C'est le mécanisme des comptes multiples d'Android, celui-là même que la
    /// surcouche du téléphone emploie pour son « espace secondaire ». Rien
    /// n'est recopié ni modifié : <c>install-existing</c> rend au nouveau
    /// profil l'application déjà présente, signée par son éditeur. Le profil
    /// naît en revanche avec son propre espace de données, vide : le jeu y
    /// redemandera ses ressources et sa connexion.
    ///
    /// La place est vérifiée d'abord. Un téléphone plafonne le nombre de
    /// profils, quatre sur celui de référence, et laisser la création échouer
    /// rendrait un message d'ADB que personne ne comprend.
    /// </summary>
    public async Task<AccountAddition> AddAccountAsync(
        string serial,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = await _users.GetUsersAsync(serial, refresh: true, cancellationToken)
            .ConfigureAwait(false);

        var maximum = await _users.GetMaxUsersAsync(serial, cancellationToken).ConfigureAwait(false);

        if (maximum is { } limit && existing.Count >= limit)
        {
            return new AccountAddition(
                false,
                Strings.Format("ProfileLimitReached", limit));
        }

        // Le profil se rattache à l'utilisateur principal. Aucun identifiant
        // n'est supposé : c'est le téléphone qui dit lequel de ses comptes est
        // le principal.
        if (existing.FirstOrDefault(user => user.IsPrimary) is not { } parent)
        {
            return new AccountAddition(
                false,
                Strings.Get("NoPrimaryProfile"));
        }

        // Android n'accepte qu'un seul profil géré par compte principal,
        // vérifié sur Android 16 : « Cannot add more profiles of type
        // android.os.usertype.profile.MANAGED for user 0 ». Le dire ici évite
        // de rendre le refus brut d'ADB.
        if (existing.Any(user => user.Type == AndroidUserType.ManagedProfile))
        {
            return new AccountAddition(
                false,
                Strings.Get("OneManagedProfileOnly"));
        }

        if (await _users.TryCreateUserAsync(serial, name, parent.Id, cancellationToken)
                .ConfigureAwait(false)
            is not { } userId)
        {
            return new AccountAddition(
                false,
                Strings.Get("ProfileCreationRefused"));
        }

        try
        {
            await _adb.ShellAsync(
                serial,
                [
                    "pm",
                    "install-existing",
                    "--user",
                    userId.ToString(CultureInfo.InvariantCulture),
                    PackageName,
                ],
                Timeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (AdbException exception)
        {
            // L'erreur remonte : le refus d'ADB devient le message que la
            // personne lit, « Le profil est créé mais le jeu n'a pas pu y être
            // installé », suivi de sa raison.
            return new AccountAddition(
                false,
                Strings.Format("ProfileMadeGameNotInstalled", exception.UserMessage),
                userId);
        }

        if (!await IsInstalledAsync(serial, userId, cancellationToken).ConfigureAwait(false))
        {
            return new AccountAddition(
                false,
                Strings.Get("ProfileMadeGameMissing"),
                userId);
        }

        // Démarré tout de suite : une application ne s'ouvre pas sur un profil
        // qui ne tourne pas, et l'utilisateur vient de demander un compte pour
        // s'en servir.
        await _users.TryStartUserAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        return new AccountAddition(
            true,
            Strings.Format("AccountAdded", name.Trim()),
            userId);
    }

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
            // Silence assumé : le composant de lancement est une commodité, et
            // l'absence de réponse se traite comme une absence de composant.
            // L'appelant a son propre message pour le dire.
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
