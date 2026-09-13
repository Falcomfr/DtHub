using System.Globalization;

using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Localization;
using DtHub.Core.Users;

namespace DtHub.Core.Dofus;

/// <summary>
/// Finds the game's instances on the connected phones: one per Android profile
/// where the package is installed. This is all the application needs to know
/// about installed applications; it keeps no catalogue of its own.
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
    /// The package being sought. Adjustable to survive a change on the
    /// publisher's side, but the application is designed for this one.
    /// </summary>
    public string PackageName { get; set; } = DofusPackages.DofusTouch;

    /// <summary>
    /// A package scan can drag on when a phone is under load.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);

    private readonly List<string> _warnings = [];

    /// <summary>
    /// Non-blocking issues from the last scan. A device whose profile list
    /// could not be read still yields an instance, that of the main profile:
    /// without a word about it, nothing distinguishes this case from a device
    /// that genuinely has only one profile, and the second account seems to
    /// have disappeared.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    private readonly Dictionary<string, IReadOnlyList<int>> _profiles = new(StringComparer.Ordinal);

    /// <summary>
    /// The Android profiles found on each phone, by device identifier, from
    /// the last scan.
    ///
    /// Only the phones whose list was actually read: a device that did not
    /// respond does not appear here, and nothing will therefore be concluded
    /// from its absence. This is what makes it possible to tell "this profile
    /// has disappeared" apart from "we could not look".
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> ScannedProfiles => _profiles;

    /// <summary>
    /// Profiles that responded and do not have the game, by device.
    ///
    /// Responded is the word that matters. Asking a profile for its packages
    /// can fail, and until now the failure returned an empty list, exactly
    /// like a response saying "nothing". The two were therefore
    /// indistinguishable, and that is why nothing could be concluded from an
    /// absence: erasing an account on that basis would have lost it at ADB's
    /// first hiccup.
    ///
    /// This record only keeps the profiles whose question succeeded. A profile
    /// that refused to answer does not appear here, and nothing will therefore
    /// be concluded from its silence. Same caution as
    /// <see cref="ScannedProfiles" />, for the same reason.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> ProfilesWithoutGame => _without;

    /// <summary>
    /// Instances present on the given phones. An offline phone is not queried:
    /// its remembered instances are reinjected by the caller.
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

    /// <summary>Instances present on a specific phone.</summary>
    public async Task<IReadOnlyList<DofusInstance>> DiscoverOnDeviceAsync(
        AndroidDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        List<DofusInstance> instances = [];

        // Re-read on every scan, and not taken from cache: a profile deleted
        // on the phone would otherwise stay known indefinitely, and its line
        // would never leave the list. The command is light compared to the
        // rest of the scan, which queries the packages of each profile.
        var users = await _users.GetUsersAsync(device.Serial, refresh: true, cancellationToken)
            .ConfigureAwait(false);

        if (AndroidUserService.IsFallback(users))
        {
            _warnings.Add(
                Strings.Format("ProfileListUnreadable", device.DisplayName));
        }
        else
        {
            // Read for real: we will be able to say that a profile has
            // disappeared, and not merely that we did not see it.
            _profiles[device.Id] = [.. users.Select(u => u.Id)];
        }

        List<int> sansJeu = [];

        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packages = await TryListInstalledAsync(device.Serial, user.Id, cancellationToken)
                .ConfigureAwait(false);

            // Null means "could not ask", and not "found nothing". Only an
            // empty response allows saying that this profile does not have the
            // game.
            //
            // And only from a profile that is in a state to answer. A stopped
            // or paused profile says nothing about the game, it says something
            // about itself; taken as a verdict, it had the account removed and
            // put back elsewhere in the list, stripped of the name the user had
            // given it.
            if (packages is { Count: 0 } && user is { IsRunning: true, IsPaused: false })
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

        // Only set if the profile list itself was actually read: without it,
        // we do not even know which profiles we are talking about.
        if (_profiles.ContainsKey(device.Id))
        {
            _without[device.Id] = sansJeu;
        }

        return instances;
    }

    /// <summary>
    /// True if the game is installed for this Android profile.
    /// </summary>
    /// <summary>
    /// Adds an account: a fresh Android profile, the game inside it, and the
    /// profile started so it can be opened right away.
    ///
    /// This is the mechanism behind Android's multiple accounts, the very one
    /// the phone's overlay uses for its "second space". Nothing is copied or
    /// modified: <c>install-existing</c> hands the new profile the application
    /// that is already there, signed by its publisher. The profile is born,
    /// however, with its own data space, empty: the game will ask it again for
    /// its resources and its connection.
    ///
    /// The room is checked first. A phone caps the number of profiles, four on
    /// the reference one, and letting creation fail would return an ADB
    /// message that nobody understands.
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

        // The profile attaches to the main user. No identifier is assumed: it
        // is the phone that says which of its accounts is the main one.
        if (existing.FirstOrDefault(user => user.IsPrimary) is not { } parent)
        {
            return new AccountAddition(
                false,
                Strings.Get("NoPrimaryProfile"));
        }

        // Android caps each profile type at one per main account, found on the
        // reference phone: "mMaxAllowedPerParent: 1" for the cloned as for the
        // work one. Two slots, then, and they are not equal.
        var clone = existing.FirstOrDefault(user => user.Type == AndroidUserType.CloneProfile);
        var managed = existing.FirstOrDefault(user => user.Type == AndroidUserType.ManagedProfile);

        // The cloned profile first, and by far. It is the one overlays use to
        // duplicate an application: the phone installs almost nothing into it,
        // and its icons carry no particular mark.
        //
        // The work one, meanwhile, is made for a company phone: Android fills
        // it in on its own with its full environment. Found on the reference
        // phone, 359 packages against 22 for the cloned one, and about fifteen
        // briefcase icons appeared on the home screen without anyone asking
        // for them.
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

        // The fallback. It works, but it does not happen silently: what it
        // changes is visible on the home screen, and nobody would guess why.
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

        // A slot remained free and creation nonetheless failed: it is the
        // phone that refused, and saying so is better than working around it.
        if (clone is null || managed is null)
        {
            return new AccountAddition(false, Strings.Get("ProfileCreationRefused"));
        }

        // Both slots are taken. There remains the case of a profile that
        // occupies one without carrying anything: its game was uninstalled, it
        // no longer serves any purpose, and refusing would leave no recourse.
        // It is then reclaimed.
        //
        // Never while a slot is free: creating a profile is better than
        // commandeering someone else's, a Second Space often existing for
        // entirely different reasons than ours.
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
    /// Installs the game on a profile and starts it.
    ///
    /// Shared by the profile just created and by the one being reclaimed: both
    /// need exactly the same thing, and keeping them together avoids a reclaim
    /// forgetting the start or the check.
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
            // The error propagates: ADB's refusal becomes the message the
            // person reads, "Le profil est créé mais le jeu n'a pas pu y être
            // installé", followed by its reason.
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

        // Started right away: an application does not open on a profile that
        // is not running, and the user has just asked for an account in order
        // to use it.
        await _users.TryStartUserAsync(serial, userId, cancellationToken).ConfigureAwait(false);

        return new AccountAddition(true, success, userId);
    }

    public async Task<bool> IsInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default) =>
        (await ListInstalledAsync(serial, userId, cancellationToken).ConfigureAwait(false)).Count > 0;

    /// <summary>
    /// Game packages present for this Android profile.
    ///
    /// The exact name first, which by far is the most common case: cloning by
    /// profile, the one the application targets, keeps the package name
    /// intact. But some overlays install their copy under a derived name, and
    /// a strict comparison made them invisible even though the command had
    /// reported them correctly. They are therefore accepted second, provided
    /// the name contains the sought package or its last segment.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default) =>
        await TryListInstalledAsync(serial, userId, cancellationToken).ConfigureAwait(false) ?? [];

    /// <summary>
    /// The game's packages for this profile, or <c>null</c> if the question
    /// did not succeed.
    ///
    /// The distinction is the whole point of this method. An empty list says
    /// "this profile responded, and it does not have the game", from which
    /// something can be concluded. <c>null</c> says "we could not ask", from
    /// which nothing is concluded. Confusing the two amounted to erasing
    /// accounts at ADB's first hiccup, and that is why the game's absence used
    /// to be of no use at all.
    /// </summary>
    public async Task<IReadOnlyList<string>?> TryListInstalledAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> found;

        try
        {
            // "pm list packages" filters by substring: the last segment brings
            // back the official package as well as its renamed copies.
            var output = await _adb.ShellAsync(
                serial,
                ["pm", "list", "packages", "--user", Text(userId), BaseToken],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            found = PackageParser.ParsePackageList(output);
        }
        catch (AdbException)
        {
            // A profile that refuses the question does not make the scan fail,
            // and produces no instance. It is simply not declared as lacking
            // the game: we know nothing about it.
            return null;
        }

        List<string> matches = [];

        if (found.Contains(PackageName, StringComparer.Ordinal))
        {
            matches.Add(PackageName);
        }

        matches.AddRange(found.Where(IsDerived).Order(StringComparer.Ordinal));

        // Nothing matched, so the question becomes whether the profile
        // answered at all. A stopped or paused profile returns an empty
        // list with a zero exit code, which reads exactly like a profile
        // that answered and has no game. Asking again without the filter
        // separates the two: no Android profile owns zero packages, so an
        // empty enumeration is proof the question never reached the
        // package state, whatever the reason.
        //
        // The extra call only happens on a profile that already looked
        // game-less, so at most twice per phone per sweep.
        if (matches.Count == 0
            && await TryEnumerateAsync(serial, userId, cancellationToken).ConfigureAwait(false) is null)
        {
            return null;
        }

        return matches;
    }

    /// <summary>
    /// Every package of a profile, or <c>null</c> when the profile did
    /// not answer. Used only to tell an empty answer from an absent one.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TryEnumerateAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await _adb.ShellAsync(
                serial,
                ["pm", "list", "packages", "--user", Text(userId)],
                Timeout,
                cancellationToken).ConfigureAwait(false);

            var all = PackageParser.ParsePackageList(output);

            return all.Count > 0 ? all : null;
        }
        catch (AdbException)
        {
            // Same silence as the filtered listing above, and for the same
            // reason: a profile that refuses the question tells us nothing,
            // and nothing is exactly what must be concluded from it.
            return null;
        }
    }

    /// <summary>
    /// Resolves the activity to launch for an Android profile.
    /// </summary>
    public async Task<AppComponent?> ResolveComponentAsync(
        string serial,
        int userId,
        CancellationToken cancellationToken = default) =>
        await ResolveComponentAsync(serial, userId, PackageName, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Resolves the activity to launch for an Android profile and a specific
    /// package. A renamed copy does not have the reference package's name:
    /// resolving it under that name would give nothing.
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
            // Silence assumed: the launch component is a convenience, and the
            // absence of a response is treated as the absence of a component.
            // The caller has its own message to say so.
            return null;
        }
    }

    /// <summary>
    /// Last segment of the package name, the one that identifies the game
    /// without the publisher. Used as a filter for the command and as a mark
    /// of the copies.
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

    /// <summary>
    /// True for a copy of the game installed under a derived name.
    /// </summary>
    private bool IsDerived(string package) =>
        !string.Equals(package, PackageName, StringComparison.Ordinal)
        && (package.Contains(PackageName, StringComparison.Ordinal)
            || package.Contains(BaseToken, StringComparison.Ordinal));

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Game packages.</summary>
public static class DofusPackages
{
    /// <summary>Official package of DOFUS Touch.</summary>
    public const string DofusTouch = "com.ankama.dofustouch";
}
