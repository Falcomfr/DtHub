namespace DtHub.Core.Guidance;

/// <summary>
/// Un écran de réglages dessiné : son titre, la ligne qu'on y touche, et la
/// hauteur à laquelle cette ligne est tracée.
/// </summary>
/// <param name="Title">Le nom de l'écran où l'on se trouve.</param>
/// <param name="Tap">
/// La ligne à toucher pour aller plus loin. Vide sur le seul écran d'un chemin
/// qui n'en compte qu'un, où il n'y a rien à toucher.
/// </param>
/// <param name="Row">Rang de cette ligne parmi celles dessinées.</param>
public readonly record struct MenuScreen(string Title, string Tap, int Row);

/// <summary>
/// Découpe un chemin de menu en écrans, pour le montrer plutôt que le faire
/// lire.
///
/// Les fiches de marque écrivent déjà « Paramètres › Applications › DOFUS
/// Touch », et c'est la suite des écrans à parcourir : il n'y a rien à rédiger
/// de plus, seulement à la lire.
///
/// Un chemin de N segments donne N-1 écrans, et non N : on est *dans*
/// « Paramètres » et l'on y touche « Applications ». Le dernier segment est la
/// ligne à toucher du dernier écran, et non un écran de plus, qu'on
/// dessinerait vide.
/// </summary>
public static class MenuPath
{
    /// <summary>Nombre de lignes dessinées dans chaque écran.</summary>
    public const int Rows = 5;

    private const char Separator = '›';

    /// <summary>
    /// Les écrans du chemin, dans l'ordre. Un chemin vide n'en donne aucun, et
    /// la vue n'affiche alors rien plutôt qu'un cadre creux.
    /// </summary>
    public static IReadOnlyList<MenuScreen> Screens(string? path)
    {
        var segments = Segments(path);

        if (segments.Count == 0)
        {
            return [];
        }

        // Un seul segment : on montre l'écran où se rendre, sans ligne à
        // toucher, parce qu'il n'y en a pas.
        if (segments.Count == 1)
        {
            return [new MenuScreen(segments[0], string.Empty, 0)];
        }

        List<MenuScreen> screens = new(segments.Count - 1);
        var previous = -1;

        for (var i = 0; i + 1 < segments.Count; i++)
        {
            var tap = segments[i + 1];
            var row = RowFor(tap);

            // Deux écrans de suite dont la ligne est à la même hauteur donnent
            // une image qui semble figée. On décale, ce qui suffit à ce que le
            // dessin ait l'air d'écrans distincts.
            if (row == previous)
            {
                row = (row + 1) % Rows;
            }

            screens.Add(new MenuScreen(segments[i], tap, row));
            previous = row;
        }

        return screens;
    }

    private static List<string> Segments(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        List<string> segments = [];

        foreach (var part in path.Split(Separator))
        {
            var label = part.Trim();

            if (label.Length > 0)
            {
                segments.Add(label);
            }
        }

        return segments;
    }

    /// <summary>
    /// La part de la largeur qu'occupe la barre d'une ligne muette, entre un
    /// peu moins de la moitié et la presque totalité.
    ///
    /// Des barres toutes de la même longueur trahissent le dessin : aucune
    /// liste de réglages n'a cinq intitulés de même taille. Elles sont donc
    /// inégales, mais pas au hasard : le même écran doit se dessiner pareil à
    /// chaque ouverture de la fenêtre.
    /// </summary>
    /// <param name="title">Le nom de l'écran.</param>
    /// <param name="row">Le rang de la ligne.</param>
    public static double BarShare(string? title, int row)
    {
        var sum = row * 7;

        foreach (var character in title ?? string.Empty)
        {
            sum = ((sum * 31) + character) % 4096;
        }

        // Cinq largeurs, assez éloignées pour se voir, assez proches pour que
        // la liste reste une liste.
        return 0.5 + (sum % 5 * 0.115);
    }

    /// <summary>
    /// La hauteur de la ligne, tirée de son libellé.
    ///
    /// Tirée et non tirée au sort : le même écran doit se dessiner pareil à
    /// chaque ouverture de la fenêtre, sans quoi l'illustration bougerait sous
    /// les yeux de qui la relit.
    /// </summary>
    private static int RowFor(string label)
    {
        var sum = 0;

        foreach (var character in label)
        {
            sum = ((sum * 31) + character) % 1024;
        }

        return sum % Rows;
    }
}
