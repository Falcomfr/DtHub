using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// The nine positions of the grid, in reading order. This is the
/// "arrow system": a corner or an edge is designated, and the block
/// of windows snaps to it.
/// </summary>
public enum WindowAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

/// <summary>Display helpers for the position grid.</summary>
public static class WindowAnchors
{
    /// <summary>All positions, in the grid's display order.</summary>
    public static readonly IReadOnlyList<WindowAnchor> All =
    [
        WindowAnchor.TopLeft, WindowAnchor.TopCenter, WindowAnchor.TopRight,
        WindowAnchor.MiddleLeft, WindowAnchor.Center, WindowAnchor.MiddleRight,
        WindowAnchor.BottomLeft, WindowAnchor.BottomCenter, WindowAnchor.BottomRight,
    ];

    /// <summary>Short label, for tooltips.</summary>
    public static string Describe(WindowAnchor anchor) => anchor switch
    {
        WindowAnchor.TopLeft => Strings.Get("AnchorTopLeft"),
        WindowAnchor.TopCenter => Strings.Get("AnchorTopCenter"),
        WindowAnchor.TopRight => Strings.Get("AnchorTopRight"),
        WindowAnchor.MiddleLeft => Strings.Get("AnchorMiddleLeft"),
        WindowAnchor.Center => Strings.Get("AnchorCenter"),
        WindowAnchor.MiddleRight => Strings.Get("AnchorMiddleRight"),
        WindowAnchor.BottomLeft => Strings.Get("AnchorBottomLeft"),
        WindowAnchor.BottomCenter => Strings.Get("AnchorBottomCenter"),
        WindowAnchor.BottomRight => Strings.Get("AnchorBottomRight"),
        _ => anchor.ToString(),
    };

    /// <summary>
    /// Corner farthest from a given position. Used to place the
    /// configurator where it does not overlap the game windows.
    /// </summary>
    public static WindowAnchor Opposite(WindowAnchor anchor) => anchor switch
    {
        WindowAnchor.TopLeft or WindowAnchor.MiddleLeft => WindowAnchor.TopRight,
        WindowAnchor.TopCenter => WindowAnchor.BottomRight,
        WindowAnchor.TopRight or WindowAnchor.MiddleRight => WindowAnchor.TopLeft,
        WindowAnchor.Center => WindowAnchor.TopRight,
        WindowAnchor.BottomLeft => WindowAnchor.TopRight,
        WindowAnchor.BottomCenter => WindowAnchor.TopRight,
        WindowAnchor.BottomRight => WindowAnchor.TopLeft,
        _ => WindowAnchor.TopRight,
    };
}
