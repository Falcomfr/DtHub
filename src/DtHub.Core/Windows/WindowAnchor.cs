using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// Les neuf positions de la grille, dans l'ordre de lecture. C'est le
/// « système de flèches » : on désigne un coin ou un bord, et le bloc de
/// fenêtres s'y colle.
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

/// <summary>Aides d'affichage pour la grille de positions.</summary>
public static class WindowAnchors
{
    /// <summary>Toutes les positions, dans l'ordre d'affichage de la grille.</summary>
    public static readonly IReadOnlyList<WindowAnchor> All =
    [
        WindowAnchor.TopLeft, WindowAnchor.TopCenter, WindowAnchor.TopRight,
        WindowAnchor.MiddleLeft, WindowAnchor.Center, WindowAnchor.MiddleRight,
        WindowAnchor.BottomLeft, WindowAnchor.BottomCenter, WindowAnchor.BottomRight,
    ];

    /// <summary>Libellé court, pour les infobulles.</summary>
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
    /// Coin le plus éloigné d'une position donnée. Sert à poser le
    /// configurateur là où il ne recouvre pas les fenêtres de jeu.
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
