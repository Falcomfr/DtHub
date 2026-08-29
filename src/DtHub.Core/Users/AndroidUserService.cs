using DtHub.Core.Adb;

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
            return false;
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
            return users;
        }
    }
}
