namespace DtHub.Core.Windows;

/// <summary>
/// Le partage de l'écran en deux moitiés, pour le rangement côte à côte.
///
/// Sorti de <see cref="WindowManagerService"/> pour que le cadre à onglets
/// puisse être rangé comme une fenêtre de jeu : le service ne connaît que des
/// sessions, et le cadre n'en est pas une. Le calcul, lui, ne dépend de rien
/// d'autre que de la zone utile et de ce qu'on range.
/// </summary>
public static class TileLayout
{
    /// <summary>
    /// La moitié d'écran qui revient à une fenêtre.
    ///
    /// La référence prend la droite, tout le reste se pose à gauche : au-delà
    /// de deux fenêtres, celles de gauche s'empilent. C'est la règle d'origine,
    /// et elle vaut ce que vaut un écran partagé en deux.
    ///
    /// La hauteur suit le rapport de la source, appliqué à la zone client :
    /// c'est elle que scrcpy remplit, et l'ignorer laisserait des bandes noires
    /// de la largeur exacte du châssis. Sans rapport connu, la moitié est prise
    /// sur toute la hauteur.
    /// </summary>
    /// <param name="work">Zone utilisable de l'écran visé.</param>
    /// <param name="onRight">Vrai pour la fenêtre de référence.</param>
    /// <param name="aspectRatio">Rapport de la source, zéro s'il est inconnu.</param>
    /// <param name="chrome">Encombrement du châssis, bordures et barres comprises.</param>
    public static ScreenRect Half(
        ScreenRect work,
        bool onRight,
        double aspectRatio,
        (int Width, int Height) chrome)
    {
        var half = work.Width / 2;

        var height = aspectRatio > 0
            ? Math.Min(
                  work.Height,
                  (int)Math.Round((half - chrome.Width) / aspectRatio) + chrome.Height)
            : work.Height;

        return new ScreenRect(
            onRight ? work.X + half : work.X,
            work.Y + ((work.Height - height) / 2),
            half,
            height);
    }
}
