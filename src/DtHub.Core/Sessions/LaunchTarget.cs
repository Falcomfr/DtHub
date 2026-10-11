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

    /// <summary>
    /// A link to open instead of the app's own activity, a Play Store page
    /// above all. Its target is not stopped before opening, which would
    /// cancel a download in progress; it is stopped with its window, like
    /// every tool window.
    /// </summary>
    public string? StartUri { get; init; }

    /// <summary>
    /// True for a window opened in passing: launched, never remembered. It
    /// does not reopen at the next launch and leaves no geometry, tab or
    /// play time behind.
    /// </summary>
    public bool IsTransient { get; init; }

    /// <summary>Name displayed in the window title.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Stable key, identical to that of the corresponding instance.
    /// </summary>
    /// <remarks>The same as <see cref="Dofus.DofusInstance.Key" />, a tool window's set apart.</remarks>
    public string Key => IsTransient ? $"{DeviceId}|{UserId}|{PackageName}|tool" : $"{DeviceId}|{UserId}|{PackageName}";
}
