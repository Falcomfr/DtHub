using System.Globalization;
using DtHub.Core.Adb;
using DtHub.Core.Android;

namespace DtHub.Core.Users;

/// <summary>
/// Lists a phone's Android users and profiles. The list changes
/// rarely, so it is cached and refreshed on request.
/// </summary>
public sealed class AndroidUserService
{
    /// <summary>User present on every Android device.</summary>
    public static readonly AndroidUser PrimaryFallback = new()
    {
        Id = 0,
        Name = string.Empty,
        Type = AndroidUserType.Primary,
        IsRunning = true,
    };

    /// <summary>
    /// True if this list is the fallback, for lack of having been able
    /// to query the device, and not a list that was actually read.
    ///
    /// The distinction matters: a device whose overlay restricts
    /// <c>pm list users</c> and a device that truly has only one
    /// profile used to show exactly the same thing on screen, a single
    /// instance and no explanation.
    /// </summary>
    public static bool IsFallback(IReadOnlyList<AndroidUser> users) =>
        users is { Count: 1 } && ReferenceEquals(users[0], PrimaryFallback);

    private readonly Dictionary<string, IReadOnlyList<AndroidUser>> _cache = new(StringComparer.Ordinal);
    private readonly IAdbClient _adb;

    public AndroidUserService(IAdbClient adb) => _adb = adb;

    /// <summary>
    /// Users of the phone. If the command fails, returns the primary
    /// user alone rather than nothing: the phone stays usable for its
    /// ordinary applications.
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
            // A phone that refuses the command still keeps its
            // primary user, without which the application would
            // become unusable for it.
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

    /// <summary>User matching an identifier, or <c>null</c>.</summary>
    public async Task<AndroidUser?> FindAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var users = await GetUsersAsync(serial, refresh: false, cancellationToken).ConfigureAwait(false);

        return users.FirstOrDefault(u => u.Id == userId);
    }

    /// <summary>
    /// Starts a stopped user. An application cannot open on a profile
    /// that is not running.
    /// </summary>
    /// <returns>True if the user is running once the call returns.</returns>
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
            // Silence assumed: false says "the profile did not start",
            // and the caller turns it into a named refusal, "Open it
            // once on the phone." The technical reason itself goes to
            // the ADB log.
            return false;
        }
    }

    /// <summary>
    /// Number of profiles the device accepts in total, or <c>null</c>
    /// if it does not say. The primary user counts in this total.
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
            // Silence assumed: empty means "this phone does not state
            // its limit", which is also true of those that do not
            // know the command. Adding an account is then not refused.
            return null;
        }
    }

    /// <summary>
    /// Creates an Android profile attached to
    /// <paramref name="parentUserId"/> and returns its identifier.
    ///
    /// This is the mechanism the phone itself uses for its multiple
    /// accounts: nothing is copied, nothing is modified, the
    /// application stays the publisher's own, signed by them. The
    /// profile is born empty, with its own data space.
    ///
    /// The profile is <b>attached</b>, not detached. The distinction
    /// decides everything: measured on a Xiaomi running Android 16, an
    /// attached profile displays on a virtual display while the other
    /// accounts are open, whereas a full user, the kind that
    /// <c>pm create-user</c> alone used to produce, never displays. It
    /// still answered <c>Status: ok</c>, then hung without showing
    /// anything.
    ///
    /// See <see cref="AndroidUserHosting"/> for the measurement and
    /// its detail.
    /// </summary>
    /// <returns>
    /// The profile's identifier, or <c>null</c> if creation was refused.
    /// </returns>
    /// <summary>
    /// The type name as Android expects it on the command line.
    ///
    /// Found in the help of <c>pm create-user</c> on the reference
    /// phone: "--managed is shorthand for --user-type
    /// android.os.usertype.profile.MANAGED". The shorthand is
    /// therefore avoided in favor of the full name, which says which
    /// of the two is being requested.
    /// </summary>
    private static string TypeName(AndroidUserType type) => type switch
    {
        AndroidUserType.CloneProfile => "android.os.usertype.profile.CLONE",
        _ => "android.os.usertype.profile.MANAGED",
    };

    public async Task<int?> TryCreateUserAsync(
        string serial,
        string name,
        int parentUserId,
        AndroidUserType type = AndroidUserType.ManagedProfile,
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
                        "--user-type",
                        TypeName(type),
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
            // Silence assumed: empty means "no profile created", and
            // the caller turns it into the refusal "The phone refused
            // to create a profile", which names the overlays that
            // forbid it.
            return null;
        }
    }

    /// <summary>Forces users to be reread on the next call.</summary>
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
    /// Refines the classification with <c>dumpsys user</c>, which
    /// tells a clone profile apart from a managed profile where the
    /// flags cannot. The absence of a response is not an error.
    /// </summary>
    private async Task<IReadOnlyList<AndroidUser>> RefineTypesAsync(
        string serial,
        IReadOnlyList<AndroidUser> users,
        CancellationToken cancellationToken)
    {
        // No point querying dumpsys if no type is ambiguous.
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
            // Silence assumed: the nature of the profiles is an
            // enrichment. Without "dumpsys user" the list is returned
            // as is, and the caller recognizes this fallback for what
            // it is.
            return users;
        }
    }
}
