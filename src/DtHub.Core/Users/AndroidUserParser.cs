using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Users;

/// <summary>
/// Reads <c>pm list users</c>. The format is
/// <c>UserInfo{id:name:flags}</c>, possibly followed by
/// <c>running</c>. The flags are in hexadecimal.
/// </summary>
public static partial class AndroidUserParser
{
    // Flags from android.content.pm.UserInfo. Only those that change
    // our behavior are named here.
    private const int FlagPrimary = 0x00000001;
    private const int FlagGuest = 0x00000004;
    private const int FlagRestricted = 0x00000008;
    private const int FlagManagedProfile = 0x00000020;

    // Paused profile. This is the work profile's on/off switch, and
    // the main function of Shelter and Island: their users toggle it
    // every day. A paused profile is listed like the others, and
    // launches nothing.
    private const int FlagQuietMode = 0x00000080;
    private const int FlagProfile = 0x00001000;
    private const int FlagMain = 0x00004000;

    private const string Marker = "UserInfo{";

    /// <summary>
    /// Reads the full list. Any non-conforming line is ignored rather
    /// than failing the whole thing: a single exotic line must not
    /// deprive the user of all their profiles.
    /// </summary>
    public static IReadOnlyList<AndroidUser> Parse(string? output)
    {
        var users = new List<AndroidUser>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return users;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            if (ParseLine(rawLine) is { } user)
            {
                users.Add(user);
            }
        }

        return [.. users.DistinctBy(u => u.Id).OrderBy(u => u.Id)];
    }

    /// <summary>
    /// Reads a single line, or returns <c>null</c> if it is not one.
    /// </summary>
    public static AndroidUser? ParseLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var start = line.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += Marker.Length;
        var end = line.IndexOf('}', start);
        if (end <= start)
        {
            return null;
        }

        var content = line[start..end];

        // A user name can contain a colon. The identifier is
        // therefore delimited by the first one, and the flags by the
        // last one.
        var firstSeparator = content.IndexOf(':', StringComparison.Ordinal);
        var lastSeparator = content.LastIndexOf(':');

        if (firstSeparator <= 0 || lastSeparator <= firstSeparator)
        {
            return null;
        }

        if (!int.TryParse(content[..firstSeparator], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id < 0)
        {
            return null;
        }

        var name = content[(firstSeparator + 1)..lastSeparator].Trim();
        var flags = ParseFlags(content[(lastSeparator + 1)..]);

        var tail = line[(end + 1)..];

        return new AndroidUser
        {
            Id = id,
            Name = name,
            Flags = flags,
            Type = ClassifyUser(id, flags),
            IsPaused = (flags & FlagQuietMode) != 0,
            IsRunning = tail.Contains("running", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Determines a user's type from its flags. No identifier is
    /// hardcoded, except for 0 which is the primary user by
    /// definition in Android.
    /// </summary>
    public static AndroidUserType ClassifyUser(int id, int flags)
    {
        if ((flags & FlagManagedProfile) != 0)
        {
            // Manufacturer overlays create their duplicated apps under
            // this same flag as a work profile: telling them apart
            // requires dumpsys, otherwise we stay neutral.
            return AndroidUserType.ManagedProfile;
        }

        if ((flags & FlagProfile) != 0)
        {
            return AndroidUserType.CloneProfile;
        }

        if ((flags & FlagGuest) != 0)
        {
            return AndroidUserType.Guest;
        }

        if ((flags & FlagRestricted) != 0)
        {
            return AndroidUserType.Restricted;
        }

        if (id == 0 || (flags & FlagPrimary) != 0 || (flags & FlagMain) != 0)
        {
            return AndroidUserType.Primary;
        }

        return AndroidUserType.Secondary;
    }

    /// <summary>
    /// Refines the types from <c>dumpsys user</c>, which publishes the
    /// exact type in the form <c>android.os.usertype.profile.CLONE</c>.
    /// Users whose type does not appear keep the classification
    /// deduced from the flags.
    /// </summary>
    public static IReadOnlyList<AndroidUser> ApplyUserTypes(
        IReadOnlyList<AndroidUser> users,
        string? dumpsysOutput)
    {
        ArgumentNullException.ThrowIfNull(users);

        var types = ParseUserTypes(dumpsysOutput);
        if (types.Count == 0)
        {
            return users;
        }

        return [.. users.Select(user => types.TryGetValue(user.Id, out var type)
            ? user with { Type = type }
            : user)];
    }

    /// <summary>
    /// Extracts the types declared by <c>dumpsys user</c>, indexed by
    /// user identifier.
    /// </summary>
    public static IReadOnlyDictionary<int, AndroidUserType> ParseUserTypes(string? dumpsysOutput)
    {
        var types = new Dictionary<int, AndroidUserType>();
        if (string.IsNullOrWhiteSpace(dumpsysOutput))
        {
            return types;
        }

        int? currentUser = null;

        foreach (var rawLine in dumpsysOutput.Split('\n'))
        {
            var line = rawLine.Trim();

            var marker = line.IndexOf(Marker, StringComparison.Ordinal);
            if (marker >= 0)
            {
                var start = marker + Marker.Length;
                var separator = line.IndexOf(':', start);

                currentUser = separator > start
                    && int.TryParse(line[start..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                        ? id
                        : null;

                continue;
            }

            if (currentUser is not { } user || !line.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var declared = line["Type:".Length..].Trim();

            if (declared.EndsWith(".CLONE", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.CloneProfile;
            }
            else if (declared.EndsWith(".MANAGED", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.ManagedProfile;
            }
            else if (declared.EndsWith(".SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.Primary;
            }
            else if (declared.EndsWith(".GUEST", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.Guest;
            }
            else if (declared.EndsWith(".RESTRICTED", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.Restricted;
            }
            else if (declared.EndsWith(".SECONDARY", StringComparison.OrdinalIgnoreCase))
            {
                types[user] = AndroidUserType.Secondary;
            }
        }

        return types;
    }

    /// <summary>
    /// Identifier of the profile that <c>pm create-user</c> has just
    /// created, or <c>null</c> if the command created nothing.
    ///
    /// The response fits in one line: "Success: created user id 10".
    /// Anything else is a refusal, and the refusal text says why.
    /// </summary>
    public static int? ParseCreatedUserId(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = CreatedUserPattern().Match(output);

        return match.Success
               && int.TryParse(match.Groups["id"].Value, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    /// <summary>
    /// Number of profiles the device accepts, returned by
    /// <c>pm get-max-users</c>, or <c>null</c> if it does not say.
    ///
    /// The response is "Maximum supported users: 4". We read it to
    /// refuse early and clearly, rather than letting the creation
    /// fail on an ADB message that nobody understands.
    /// </summary>
    public static int? ParseMaxUsers(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var match = MaxUsersPattern().Match(output);

        return match.Success
               && int.TryParse(match.Groups["count"].Value, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
    }

    [GeneratedRegex(@"created\s+user\s+id\s+(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CreatedUserPattern();

    [GeneratedRegex(@"Maximum\s+supported\s+users\s*:\s*(?<count>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex MaxUsersPattern();

    private static int ParseFlags(string raw)
    {
        var text = raw.Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flags)
            ? flags
            : 0;
    }
}
