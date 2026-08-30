namespace DtHub.Core.Settings;

/// <summary>
/// Distance apparente dans le jeu : plus ou moins de terrain visible, à taille
/// de fenêtre égale.
/// </summary>
public enum GameZoom
{
    /// <summary>Le plus de terrain possible, à la limite du lisible.</summary>
    Widest,

    /// <summary>Beaucoup de terrain, l'interface petite.</summary>
    Wide,

    /// <summary>Réglage d'origine.</summary>
    Normal,

    /// <summary>Le moins de terrain, l'interface la plus grande.</summary>
    Close,
}

/// <summary>
/// Traduit un zoom en densité d'afficheur.
///
/// Android exprime les mises en page en points indépendants de la densité :
/// une définition de 1080 pixels à 240 ppp fait 720 points de haut, et c'est ce
/// nombre de points, non le nombre de pixels, qui décide de la taille de
/// l'interface du jeu et de la portion de terrain visible. Jouer sur la densité
/// change donc la distance apparente sans toucher à la finesse de l'image.
///
/// La densité est calculée à partir de la définition retenue, et non fixée une
/// fois pour toutes : la définition suit la taille de la fenêtre, et une
/// densité constante aurait fait varier le zoom avec elle, ce qui était le
/// défaut d'origine. À hauteur de points constante, une petite fenêtre montre
/// désormais la même chose qu'une grande, en plus petit.
/// </summary>
public static class ZoomProfile
{
    /// <summary>
    /// Hauteur de la mise en page, en points indépendants de la densité.
    ///
    /// La valeur normale, 720 points, est celle qu'un afficheur de 1080 pixels
    /// à 240 ppp donnait jusqu'ici : le réglage d'origine reste le réglage
    /// d'origine.
    /// </summary>
    public static int LayoutHeightFor(GameZoom zoom) => zoom switch
    {
        GameZoom.Widest => 1120,
        GameZoom.Wide => 900,
        GameZoom.Close => 576,
        _ => 720,
    };

    /// <summary>
    /// Densité à demander pour une définition donnée. Bornée aux valeurs
    /// qu'Android accepte, faute de quoi l'afficheur est refusé.
    /// </summary>
    public static int DpiFor(int displayHeight, GameZoom zoom)
    {
        var layout = LayoutHeightFor(zoom);

        if (displayHeight <= 0 || layout <= 0)
        {
            return 240;
        }

        // 160 ppp est, par définition d'Android, la densité où un point vaut
        // un pixel.
        return Math.Clamp((int)Math.Round(displayHeight * 160.0 / layout), 60, 640);
    }
}
