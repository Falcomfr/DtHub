namespace DtHub.Core.Windows;

/// <summary>
/// La règle de forme d'une fenêtre qu'on étire à la souris : quel bord commande
/// quoi, et lequel ne bouge pas.
///
/// Ici plutôt que dans la fenêtre elle-même : c'est un calcul, et un calcul se
/// vérifie sans ouvrir d'interface. La fenêtre se contente de le brancher sur le
/// message que Windows lui envoie pendant l'étirement.
///
/// Corriger pendant l'étirement plutôt qu'après : corriger après coup rendait le
/// bord du bas inerte, la hauteur étant aussitôt recalculée depuis la largeur,
/// et la fenêtre paraissait résister à la souris.
/// </summary>
public static class AspectSizing
{
    /// <summary>Le bord tiré, tel que Windows le désigne dans WM_SIZING.</summary>
    public const int Left = 1;

    public const int Right = 2;
    public const int Top = 3;
    public const int TopLeft = 4;
    public const int TopRight = 5;
    public const int Bottom = 6;
    public const int BottomLeft = 7;
    public const int BottomRight = 8;

    /// <summary>
    /// Ramène le rectangle proposé à la forme voulue.
    /// </summary>
    /// <param name="proposed">Ce que Windows propose, d'après la souris.</param>
    /// <param name="edge">Le bord tiré.</param>
    /// <param name="aspect">Rapport largeur sur hauteur de l'image à loger.</param>
    /// <param name="chrome">Ce que le châssis prend autour d'elle.</param>
    /// <param name="minimumWidth">Largeur en deçà de laquelle on ne descend pas.</param>
    public static ScreenRect Constrain(
        ScreenRect proposed,
        int edge,
        double aspect,
        (int Width, int Height) chrome,
        int minimumWidth)
    {
        if (aspect <= 0)
        {
            return proposed;
        }

        // Tirer le haut ou le bas commande la largeur : c'est la hauteur que la
        // souris vient de fixer, et la largeur qui doit suivre. Le bord gauche
        // ne bouge pas, sans quoi la fenêtre glisserait de côté en s'étirant.
        if (edge is Top or Bottom)
        {
            var large = Math.Max(
                minimumWidth,
                (int)Math.Round((proposed.Height - chrome.Height) * aspect) + chrome.Width);

            return proposed with { Width = large };
        }

        // Tout le reste, côtés et coins, commande la hauteur depuis la largeur.
        var largeur = Math.Max(minimumWidth, proposed.Width);
        var hauteur = (int)Math.Round((largeur - chrome.Width) / aspect) + chrome.Height;

        // Le bord opposé à celui que l'on tire reste où il est. Tirer un coin du
        // haut garde donc le bas, et inversement.
        return edge is TopLeft or TopRight
            ? proposed with
            {
                Y = proposed.Bottom - hauteur,
                Width = largeur,
                Height = hauteur,
            }
            : proposed with { Width = largeur, Height = hauteur };
    }
}
