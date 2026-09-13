using DtHub.Core.Settings;

namespace DtHub.Core.Dofus;

/// <summary>
/// An instance of the game: a phone, an Android profile, an
/// installation. Two instances on the same phone correspond to the
/// main profile and a cloned profile, each with its own account.
/// </summary>
public sealed record DofusInstance
{
    /// <summary>
    /// Stable identity of the phone, independent of the connection
    /// mode.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>Name of the phone, as displayed.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Android profile. Any positive integer is valid.</summary>
    public required int UserId { get; init; }

    /// <summary>Name of the Android profile as the phone reports it.</summary>
    public required string UserName { get; init; }

    public required string PackageName { get; init; }

    /// <summary>Component to launch, resolved upon discovery.</summary>
    public string? LaunchComponent { get; init; }

    /// <summary>
    /// Name chosen by the user, shown in the window's title.
    /// </summary>
    public string? CustomName { get; init; }

    /// <summary>True if the instance is part of automatic launch.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// True if the window follows automatic placement. Unchecked, it
    /// stays where it is and the rest arranges itself around it.
    /// </summary>
    public bool IsManaged { get; init; } = true;

    /// <summary>
    /// True if this account opens in the tabbed frame rather than in
    /// a free-floating window. A tabbed account escapes automatic
    /// placement.
    /// </summary>
    public bool IsTabbed { get; init; }

    /// <summary>
    /// Quality tier specific to this account, or <c>null</c> to
    /// follow the common setting.
    /// </summary>
    public StreamQuality? Quality { get; init; }

    /// <summary>
    /// In-game distance specific to this account, or <c>null</c> to
    /// follow the common setting.
    /// </summary>
    public GameZoom? Zoom { get; init; }

    /// <summary>Play time for the week, in seconds.</summary>
    public int PlayedThisWeek { get; init; }

    /// <summary>True if the phone is reachable right now.</summary>
    public bool IsDeviceConnected { get; init; }

    /// <summary>
    /// Stable key of the instance. Used to remember it and find it
    /// again between two launches, even when the phone changes
    /// address.
    /// </summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";

    /// <summary>
    /// Editable name of the instance. The user's choice takes
    /// priority; failing that, the Android profile's name, which
    /// already distinguishes instances on the same phone. The
    /// product's name does not appear here: it is added to the game
    /// window's title, not here.
    /// </summary>
    public string DisplayName => NameOf(CustomName, UserName);

    /// <summary>
    /// The naming rule, kept on its own and named, because it is
    /// also used far from here: a window already open must be able
    /// to recompute its title from the setting that just changed,
    /// without having the instance at hand. Two copies of the rule,
    /// and a renamed account would carry one name in the list and
    /// another on its window.
    /// </summary>
    /// <param name="customName">
    /// The name chosen by the user, if there is one.
    /// </param>
    /// <param name="userName">
    /// The Android profile's name, which serves as a fallback.
    /// </param>
    public static string NameOf(string? customName, string userName) =>
        string.IsNullOrWhiteSpace(customName) ? userName : customName.Trim();
}
