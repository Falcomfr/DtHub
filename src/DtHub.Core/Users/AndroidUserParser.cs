using System.Globalization;
using System.Text.RegularExpressions;

namespace DtHub.Core.Users;

/// <summary>
/// Lecture de <c>pm list users</c>. Le format est
/// <c>UserInfo{identifiant:nom:drapeaux}</c>, suivi éventuellement de
/// <c>running</c>. Les drapeaux sont en hexadécimal.
/// </summary>
public static partial class AndroidUserParser
{
    // Drapeaux issus de android.content.pm.UserInfo. Seuls ceux qui changent
    // notre comportement sont nommés ici.
    private const int FlagPrimary = 0x00000001;
    private const int FlagGuest = 0x00000004;
    private const int FlagRestricted = 0x00000008;
    private const int FlagManagedProfile = 0x00000020;
    private const int FlagProfile = 0x00001000;
    private const int FlagMain = 0x00004000;

    private const string Marker = "UserInfo{";

    /// <summary>
    /// Lit la liste complète. Toute ligne non conforme est ignorée plutôt que
    /// de faire échouer l'ensemble : une seule ligne exotique ne doit pas
    /// priver l'utilisateur de tous ses profils.
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

    /// <summary>Lit une ligne unique, ou rend <c>null</c> si elle n'en est pas une.</summary>
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

        // Un nom d'utilisateur peut contenir un deux-points. L'identifiant est
        // donc délimité par le premier, et les drapeaux par le dernier.
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
            IsRunning = tail.Contains("running", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Détermine la nature d'un utilisateur à partir de ses drapeaux. Aucun
    /// identifiant n'est câblé, à l'exception de 0 qui est l'utilisateur
    /// principal par définition dans Android.
    /// </summary>
    public static AndroidUserType ClassifyUser(int id, int flags)
    {
        if ((flags & FlagManagedProfile) != 0)
        {
            // Les surcouches constructeur créent leurs applications dupliquées
            // sous ce même drapeau qu'un profil d'entreprise : les distinguer
            // demande dumpsys, sinon on reste neutre.
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
    /// Affine les types à partir de <c>dumpsys user</c>, qui publie le type
    /// exact sous la forme <c>android.os.usertype.profile.CLONE</c>. Les
    /// utilisateurs dont le type n'apparaît pas conservent le classement
    /// déduit des drapeaux.
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
    /// Extrait les types déclarés par <c>dumpsys user</c>, indexés par
    /// identifiant d'utilisateur.
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
    /// Identifiant du profil que <c>pm create-user</c> vient de créer, ou
    /// <c>null</c> si la commande n'a rien créé.
    ///
    /// La réponse tient en une ligne : « Success: created user id 10 ». Tout
    /// autre chose est un refus, et le texte du refus dit pourquoi.
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
    /// Nombre de profils que l'appareil accepte, rendu par
    /// <c>pm get-max-users</c>, ou <c>null</c> s'il ne le dit pas.
    ///
    /// La réponse est « Maximum supported users: 4 ». On la lit pour refuser
    /// tôt et clairement, plutôt que de laisser la création échouer sur un
    /// message d'ADB que personne ne comprend.
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
