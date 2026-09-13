using DtHub.Core.Localization;

namespace DtHub.Core.Users;

/// <summary>
/// An Android user or profile of a phone. Each user has its own set
/// of installed applications, which allows the same application to
/// be launched twice in two separate sessions.
/// </summary>
public sealed record AndroidUser
{
    /// <summary>
    /// Android identifier. Any positive integer is valid: no
    /// particular value should be assumed.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>Name as the phone reports it, often translated.</summary>
    public required string Name { get; init; }

    /// <summary>Raw flags, kept for diagnostics.</summary>
    public int Flags { get; init; }

    public AndroidUserType Type { get; init; } = AndroidUserType.Unknown;

    /// <summary>
    /// A stopped user cannot launch an application until it has been
    /// started.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// A paused profile exists, is listed, and launches nothing. This
    /// is the work profile's switch, and the main function of
    /// Shelter and Island. Only the phone itself can turn it back
    /// on: no ADB command allows it, verified on Android 16 where
    /// <c>cmd user set-quiet-mode</c> does not exist.
    /// </summary>
    public bool IsPaused { get; init; }

    public bool IsPrimary => Type == AndroidUserType.Primary;

    /// <summary>
    /// Short label for the type, for tooltips and diagnostics.
    /// </summary>
    public string TypeLabel => Type switch
    {
        AndroidUserType.Primary => Strings.Get("UserTypePrimary"),
        AndroidUserType.ManagedProfile => Strings.Get("ManagedProfile"),
        AndroidUserType.CloneProfile => Strings.Get("UserTypeClone"),
        AndroidUserType.Secondary => Strings.Get("UserTypeSecondary"),
        AndroidUserType.Guest => Strings.Get("Guest"),
        AndroidUserType.Restricted => Strings.Get("UserTypeRestricted"),
        _ => Strings.Get("UserTypeProfile"),
    };

    /// <summary>
    /// Name shown in lists. The phone names its own profiles itself,
    /// often better than we could: "Applications dupliquées"
    /// ("Duplicate apps"), "Second espace" ("Second space"). We let
    /// it decide, except for the primary user where a stable label
    /// is clearer.
    ///
    /// This label follows the interface language. It serves as the
    /// account's first name, which the user can rename, and which is
    /// then kept as is: changing language therefore renames nothing
    /// that already exists.
    /// </summary>
    public string DisplayName => Type == AndroidUserType.Primary
        ? Strings.Get("UserTypePrimary")
        : string.IsNullOrWhiteSpace(Name) ? $"{TypeLabel} {Id}" : Name.Trim();
}
