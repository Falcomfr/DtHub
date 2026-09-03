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
/// Ce qui habille une ligne muette d'un écran dessiné.
///
/// Rien de tout cela ne nomme un réglage : la fiche de marque ne donne que le
/// chemin. Ce sont les traits qu'ont toutes les listes de réglages d'Android,
/// et sans eux le dessin ressemblait à n'importe quelle liste.
/// </summary>
/// <param name="BarShare">Part de la largeur qu'occupe la barre du libellé.</param>
/// <param name="Tint">Rang de la teinte de la pastille, de 0 à cinq.</param>
/// <param name="HasSwitch">Vrai quand la ligne porte un interrupteur, non un chevron.</param>
/// <param name="HasSubtitle">Vrai quand une seconde ligne, plus courte, la suit.</param>
public readonly record struct MenuRowDecor(
    double BarShare,
    int Tint,
    bool HasSwitch,
    bool HasSubtitle);

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

    /// <summary>Nombre de teintes de pastille.</summary>
    public const int Tints = 6;

    /// <summary>
    /// L'habillage d'une ligne muette.
    ///
    /// Des barres toutes de la même longueur, toutes les pastilles de la même
    /// couleur et pas un interrupteur : le dessin se trahissait, aucune liste
    /// de réglages ne ressemble à cela. Tout est donc inégal, mais rien n'est
    /// tiré au sort : le même écran doit se dessiner pareil à chaque ouverture
    /// de la fenêtre, sans quoi l'illustration bougerait sous les yeux de qui
    /// la relit.
    /// </summary>
    /// <param name="title">Le nom de l'écran.</param>
    /// <param name="row">Le rang de la ligne.</param>
    public static MenuRowDecor Decor(string? title, int row)
    {
        var ligne = Hash((row + 1) * 7, title);

        return new MenuRowDecor(
            // Cinq largeurs, assez éloignées pour se voir, assez proches pour
            // que la liste reste une liste.
            0.5 + (ligne % 5 * 0.115),
            ligne / 5 % Tints,
            // Un seul interrupteur par écran. Un premier essai en tirait un par
            // ligne, et « Applications » se retrouvait avec quatre bascules :
            // un écran par lequel on ne fait que passer n'en porte pas quatre.
            row == Hash(0, title) % Rows,
            // Un sous-titre une fois sur trois : beaucoup de réglages annoncent
            // leur état sous leur nom, mais pas tous.
            ligne / 97 % 3 == 0);
    }

    /// <summary>
    /// Une empreinte stable d'un titre. Stable et non aléatoire : le même écran
    /// doit se dessiner pareil à chaque ouverture de la fenêtre, sans quoi
    /// l'illustration bougerait sous les yeux de qui la relit.
    /// </summary>
    private static int Hash(int seed, string? title)
    {
        var sum = seed;

        foreach (var character in title ?? string.Empty)
        {
            sum = ((sum * 31) + character) % 65536;
        }

        return sum;
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
