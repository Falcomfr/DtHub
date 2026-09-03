using System.Globalization;
using DtHub.Core.Adb;
using DtHub.Core.Android;

namespace DtHub.Core.Users;

/// <summary>
/// Liste les utilisateurs et profils Android d'un téléphone. La liste change
/// rarement, elle est donc mise en cache et rafraîchie sur demande.
/// </summary>
public sealed class AndroidUserService
{
    /// <summary>Utilisateur présent sur tout appareil Android.</summary>
    public static readonly AndroidUser PrimaryFallback = new()
    {
        Id = 0,
        Name = string.Empty,
        Type = AndroidUserType.Primary,
        IsRunning = true,
    };

    /// <summary>
    /// Vrai si cette liste est le repli, faute d'avoir pu interroger l'appareil,
    /// et non une liste réellement lue.
    ///
    /// La distinction compte : un appareil dont la surcouche bride
    /// <c>pm list users</c> et un appareil qui n'a vraiment qu'un profil
    /// donnaient jusqu'ici exactement la même chose à l'écran, une seule
    /// instance et aucune explication.
    /// </summary>
    public static bool IsFallback(IReadOnlyList<AndroidUser> users) =>
        users is { Count: 1 } && ReferenceEquals(users[0], PrimaryFallback);

    private readonly Dictionary<string, IReadOnlyList<AndroidUser>> _cache = new(StringComparer.Ordinal);
    private readonly IAdbClient _adb;

    public AndroidUserService(IAdbClient adb) => _adb = adb;

    /// <summary>
    /// Utilisateurs du téléphone. En cas d'échec de la commande, rend
    /// l'utilisateur principal seul plutôt que rien : le téléphone reste
    /// utilisable pour ses applications ordinaires.
    /// </summary>
    public async Task<IReadOnlyList<AndroidUser>> GetUsersAsync(
        string serial,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        if (!refresh)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(serial, out var cached))
                {
                    return cached;
                }
            }
        }

        IReadOnlyList<AndroidUser> users;

        try
        {
            var output = await _adb.ShellAsync(serial, ["pm", "list", "users"], null, cancellationToken)
                .ConfigureAwait(false);

            users = AndroidUserParser.Parse(output);
        }
        catch (AdbException)
        {
            // Un téléphone qui refuse la commande garde au moins son
            // utilisateur principal, sans quoi l'application deviendrait
            // inutilisable pour lui.
            return [PrimaryFallback];
        }

        if (users.Count == 0)
        {
            return [PrimaryFallback];
        }

        users = await RefineTypesAsync(serial, users, cancellationToken).ConfigureAwait(false);

        lock (_cache)
        {
            _cache[serial] = users;
        }

        return users;
    }

    /// <summary>Utilisateur correspondant à un identifiant, ou <c>null</c>.</summary>
    public async Task<AndroidUser?> FindAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(serial, refresh: false, cancellationToken).ConfigureAwait(false);

        return users.FirstOrDefault(u => u.Id == userId);
    }

    /// <summary>
    /// Démarre un utilisateur arrêté. Une application ne peut pas s'ouvrir sur
    /// un profil qui ne tourne pas.
    /// </summary>
    /// <returns>Vrai si l'utilisateur tourne à l'issue de l'appel.</returns>
    public async Task<bool> TryStartUserAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentOutOfRangeException.ThrowIfNegative(userId);

        try
        {
            var output = await _adb.ShellAsync(
                serial,
                ["am", "start-user", userId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                null,
                cancellationToken).ConfigureAwait(false);

            var started = output.Contains("Success", StringComparison.OrdinalIgnoreCase);

            if (started)
            {
                InvalidateCache(serial);
            }

            return started;
        }
        catch (AdbException)
        {
            // Silence assumé : le faux dit « le profil n'a pas démarré », et
            // l'appelant en fait un refus nommé, « Ouvrez-le une fois sur le
            // téléphone ». Le motif technique, lui, part au journal d'ADB.
            return false;
        }
    }

    /// <summary>
    /// Nombre de profils que l'appareil accepte en tout, ou <c>null</c> s'il ne
    /// le dit pas. Le principal compte dans ce total.
    /// </summary>
    public async Task<int?> GetMaxUsersAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        try
        {
            var output = await _adb
                .ShellAsync(serial, ["pm", "get-max-users"], null, cancellationToken)
                .ConfigureAwait(false);

            return AndroidUserParser.ParseMaxUsers(output);
        }
        catch (AdbException)
        {
            // Silence assumé : le vide dit « ce téléphone ne dit pas sa
            // limite », ce qui est aussi le cas de ceux qui ne connaissent pas
            // la commande. On ne refuse alors pas d'ajouter un compte.
            return null;
        }
    }

    /// <summary>
    /// Crée un profil Android rattaché à <paramref name="parentUserId"/> et
    /// rend son identifiant.
    ///
    /// C'est le mécanisme que le téléphone emploie lui-même pour ses comptes
    /// multiples : rien n'est recopié, rien n'est modifié, l'application reste
    /// celle de l'éditeur, signée par lui. Le profil naît vide, avec son propre
    /// espace de données.
    ///
    /// Le profil est <b>rattaché</b>, et non détaché. La distinction décide de
    /// tout : mesuré sur un Xiaomi sous Android 16, un profil rattaché
    /// s'affiche sur un afficheur virtuel pendant que les autres comptes sont
    /// ouverts, tandis qu'un utilisateur complet, celui que rendait
    /// <c>pm create-user</c> seul, ne s'affiche jamais. Il répondait pourtant
    /// <c>Status: ok</c>, puis pendait sans rien montrer.
    ///
    /// Voir <see cref="AndroidUserHosting"/> pour la mesure et son détail.
    /// </summary>
    /// <returns>L'identifiant du profil, ou <c>null</c> si la création a été refusée.</returns>
    public async Task<int?> TryCreateUserAsync(
        string serial,
        string name,
        int parentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(parentUserId);

        try
        {
            var output = await _adb
                .ShellAsync(
                    serial,
                    [
                        "pm",
                        "create-user",
                        "--profileOf",
                        parentUserId.ToString(CultureInfo.InvariantCulture),
                        "--managed",
                        AndroidShell.Quote(name.Trim()),
                    ],
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            var id = AndroidUserParser.ParseCreatedUserId(output);

            if (id is not null)
            {
                InvalidateCache(serial);
            }

            return id;
        }
        catch (AdbException)
        {
            // Silence assumé : le vide dit « aucun profil créé », et l'appelant
            // en fait le refus « Le téléphone a refusé de créer un profil »,
            // qui nomme les surcouches qui l'interdisent.
            return null;
        }
    }

    /// <summary>Force la relecture des utilisateurs au prochain appel.</summary>
    public void InvalidateCache(string? serial = null)
    {
        lock (_cache)
        {
            if (serial is null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.Remove(serial);
            }
        }
    }

    /// <summary>
    /// Complète le classement avec <c>dumpsys user</c>, qui distingue un
    /// profil de clonage d'un profil d'entreprise là où les drapeaux ne le
    /// permettent pas. L'absence de réponse n'est pas une erreur.
    /// </summary>
    private async Task<IReadOnlyList<AndroidUser>> RefineTypesAsync(
        string serial,
        IReadOnlyList<AndroidUser> users,
        CancellationToken cancellationToken)
    {
        // Inutile d'interroger dumpsys si aucun type n'est ambigu.
        if (!users.Any(u => u.Type is AndroidUserType.ManagedProfile or AndroidUserType.CloneProfile))
        {
            return users;
        }

        try
        {
            var dump = await _adb.ShellAsync(serial, ["dumpsys", "user"], null, cancellationToken)
                .ConfigureAwait(false);

            return AndroidUserParser.ApplyUserTypes(users, dump);
        }
        catch (AdbException)
        {
            // Silence assumé : la nature des profils est un enrichissement. Sans
            // « dumpsys user » on rend la liste telle quelle, et l'appelant
            // reconnaît ce repli pour ce qu'il est.
            return users;
        }
    }
}
