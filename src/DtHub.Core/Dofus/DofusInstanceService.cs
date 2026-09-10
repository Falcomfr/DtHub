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
    /// Profils qui ont répondu et qui n'ont pas le jeu, par appareil.
    ///
    /// Répondu est le mot qui compte. Interroger les paquets d'un profil peut
    /// échouer, et l'échec rendait jusqu'ici une liste vide, exactement comme
    /// une réponse disant « rien ». Les deux étaient donc indiscernables, et
    /// c'est pourquoi rien ne pouvait être conclu d'une absence : effacer un
    /// compte sur cette foi l'aurait perdu au premier hoquet d'ADB.
    ///
    /// Ce relevé ne retient que les profils dont la question a abouti. Un
    /// profil qui a refusé de répondre n'y figure pas, et l'on ne conclura donc
    /// rien de son silence. Même prudence que <see cref="ScannedProfiles" />,
    /// pour la même raison.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> ProfilesWithoutGame => _without;

    /// <summary>
    /// Instances présentes sur les téléphones donnés. Un téléphone hors ligne
    /// n'est pas interrogé : ses instances mémorisées sont réinjectées par
    /// l'appelant.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<int>> _without = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<DofusInstance>> DiscoverAsync(
        IEnumerable<AndroidDevice> devices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(devices);

        List<DofusInstance> instances = [];
        _warnings.Clear();
        _profiles.Clear();
        _without.Clear();

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

        List<int> sansJeu = [];

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packages = await TryListInstalledAsync(device.Serial, user.Id, cancellationToken)
                .ConfigureAwait(false);

            // Null veut dire « pas su demander », et non « rien trouvé ». Seule
            // une réponse vide autorise à dire que ce profil n'a pas le jeu.
            if (packages is { Count: 0 })
            {
                sansJeu.Add(user.Id);
            }

            foreach (var package in packages ?? [])
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

        // Posé seulement si la liste des profils elle-même a été lue pour de
        // bon : sans elle, on ne sait même pas de quels profils on parle.
        if (_profiles.ContainsKey(device.Id))
        {
            _without[device.Id] = sansJeu;
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

        // Android plafonne chaque type de profil à un par compte principal,
        // relevé sur le téléphone de référence : « mMaxAllowedPerParent: 1 »
        // pour le cloné comme pour le professionnel. Deux places, donc, et
        // elles ne se valent pas.
        var clone = existing.FirstOrDefault(user => user.Type == AndroidUserType.CloneProfile);
        var managed = existing.FirstOrDefault(user => user.Type == AndroidUserType.ManagedProfile);

        // Le profil cloné d'abord, et de loin. C'est celui que les surcouches
        // emploient pour dupliquer une application : le téléphone n'y installe
        // presque rien, et ses icônes ne portent aucune marque particulière.
        //
        // Le professionnel, lui, est fait pour un téléphone d'entreprise :
        // Android le garnit tout seul de son environnement complet. Relevé sur
        // le téléphone de référence, 359 paquets contre 22 pour le cloné, et
        // une quinzaine d'icônes à valise apparues sur l'écran d'accueil sans
        // que personne ne les ait demandées.
        if (clone is null
            && await _users
                .TryCreateUserAsync(serial, name, parent.Id, AndroidUserType.CloneProfile, cancellationToken)
                .ConfigureAwait(false) is { } cloned)
        {
            return await FillProfileAsync(
                serial,
                cloned,
                Strings.Format("AccountAdded", name.Trim()),
                cancellationToken).ConfigureAwait(false);
        }

        // Le repli. Il marche, mais il ne se fait pas en silence : ce qu'il
        // change se voit sur l'écran d'accueil, et personne ne devinerait
        // pourquoi.
        if (managed is null
            && await _users
                .TryCreateUserAsync(serial, name, parent.Id, AndroidUserType.ManagedProfile, cancellationToken)
                .ConfigureAwait(false) is { } worked)
        {
            return await FillProfileAsync(
                serial,
                worked,
                Strings.Format("AccountAddedAsWorkProfile", name.Trim()),
                cancellationToken).ConfigureAwait(false);
        }

        // Une place restait libre et la création a pourtant échoué : c'est le
        // téléphone qui a refusé, et le dire vaut mieux que de bricoler.
        if (clone is null || managed is null)
        {
            return new AccountAddition(false, Strings.Get("ProfileCreationRefused"));
        }

        // Les deux places sont prises. Reste le cas d'un profil qui en occupe
        // une sans rien porter : son jeu a été désinstallé, il ne sert plus à
        // rien, et refuser laisserait sans recours. On le reprend alors.
        //
        // Jamais tant qu'une place est libre : créer un profil vaut mieux que
        // réquisitionner celui de quelqu'un, un Second Space existant souvent
        // pour de tout autres raisons que les nôtres.
        foreach (var idle in new[] { clone, managed })
        {
            if (!await IsInstalledAsync(serial, idle.Id, cancellationToken).ConfigureAwait(false))
            {
                return await FillProfileAsync(
                    serial,
                    idle.Id,
                    Strings.Format("ProfileReused", idle.Name),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new AccountAddition(false, Strings.Get("ProfileSlotsFull"));
    }

    /// <summary>
    /// Pose le jeu sur un profil et le démarre.
    ///
    /// Partagé par le profil qu'on vient de créer et par celui qu'on reprend :
    /// les deux ont besoin exactement de la même chose, et les tenir ensemble
    /// évite qu'une reprise oublie le démarrage ou la vérification.
    /// </summary>
    private async Task<AccountAddition> FillProfileAsync(
        string serial,
        int userId,
        string success,
        CancellationToken cancellationToken)
    {
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

        return new AccountAddition(true, success, userId);
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
        CancellationToken cancellationToken = default) =>
        await TryListInstalledAsync(serial, userId, cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>
    /// Les paquets du jeu de ce profil, ou <c>null</c> si la question n'a pas
    /// abouti.
    ///
    /// La distinction est tout l'objet de cette méthode. Une liste vide dit
    /// « ce profil a répondu, et il n'a pas le jeu », ce dont on peut conclure
    /// quelque chose. <c>null</c> dit « on n'a pas su demander », ce dont on ne
    /// conclut rien. Les confondre revenait à effacer des comptes au premier
    /// hoquet d'ADB, et c'est pourquoi l'absence du jeu ne servait jusqu'ici à
    /// rien.
    /// </summary>
    public async Task<IReadOnlyList<string>?> TryListInstalledAsync(
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
            // Un profil qui refuse la question ne fait pas échouer le balayage,
            // et ne produit aucune instance. Il n'est simplement pas déclaré
            // dépourvu du jeu : on n'en sait rien.
            return null;
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
