namespace DtHub.Core.Sessions;

/// <summary>
/// What is needed to open a session: a reachable phone, an Android
/// profile, an application.
/// </summary>
public sealed record LaunchTarget
{
    public required string DeviceId { get; init; }

    /// <summary>ADB serial number to use now.</summary>
    public required string Serial { get; init; }

    public required int UserId { get; init; }

    public required string PackageName { get; init; }

    /// <summary>
    /// Remembered component. It is revalidated at launch: a game
    /// update can rename its main activity.
    /// </summary>
    public string? LaunchComponent { get; init; }

    /// <summary>Name displayed in the window title.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Stable key, identical to that of the corresponding instance.
    /// </summary>
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}
