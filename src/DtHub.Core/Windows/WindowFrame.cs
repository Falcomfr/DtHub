namespace DtHub.Core.Windows;

/// <summary>
/// Épaisseur du cadre que Windows dessine autour de la zone client d'une
/// fenêtre : la bordure gauche, la barre de titre, et ce que les deux ajoutent
/// à la largeur et à la hauteur.
///
/// La distinction n'est pas cosmétique. scrcpy dimensionne **et positionne** sa
/// fenêtre par l'intérieur : lui donner le coin extérieur la faisait naître une
/// bordure trop à gauche et une barre de titre trop haut, et le placement qui
/// suivait la recalait à l'écran. Mesuré sur l'appareil de développement :
/// demandé en (186, 284), le cadre paraissait en (175, 239).
/// </summary>
public readonly record struct WindowFrame(int Left, int Top, int Width, int Height)
{
    /// <summary>Aucun cadre : fenêtre sans bordure, ou plein écran.</summary>
    public static WindowFrame None => default;

    /// <summary>
    /// Rectangle à demander pour la zone client afin que la fenêtre, cadre
    /// compris, occupe exactement <paramref name="outer"/>.
    /// </summary>
    public ScreenRect ClientOf(ScreenRect outer) => new(
        outer.X + Left,
        outer.Y + Top,
        Math.Max(1, outer.Width - Width),
        Math.Max(1, outer.Height - Height));
}
