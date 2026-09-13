namespace DtHub.Core.Windows;

/// <summary>A Windows screen and its usable area, taskbar excluded.</summary>
public sealed record MonitorInfo
{
    public required string DeviceName { get; init; }

    /// <summary>Full rectangle of the screen.</summary>
    public required ScreenRect Bounds { get; init; }

    /// <summary>
    /// Usable area, taskbar and toolbars excluded. This is what is
    /// used for percentage-based sizing: a window at 90% must not go
    /// under the taskbar.
    /// </summary>
    public required ScreenRect WorkArea { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>Name displayed in settings.</summary>
    public string DisplayName =>
        $"{(IsPrimary ? "Écran principal" : DeviceName)} ({Bounds.Width}x{Bounds.Height})";
}
