namespace DtHub.Core.Guidance;

/// <summary>
/// Une étape d'un chemin de menu : ce qu'on touche, et à quelle hauteur de
/// l'écran la ligne est dessinée.
/// </summary>
/// <param name="Label">Le libellé de la ligne, tel que la surcouche l'écrit.</param>
/// <param name="Row">Rang de la ligne surlignée dans l'écran dessiné.</param>
public readonly record struct MenuStep(string Label, int Row);

/// <summary>
/// Découpe un chemin de menu en étapes, pour le montrer comme une suite
/// d'écrans plutôt que comme une ligne de texte.
///
/// Les chemins sont écrits « Paramètres › Applications › DOFUS Touch », et
/// c'est déjà la suite des écrans à parcourir : il n'y a rien à rédiger de
/// plus, seulement à la lire.
/// </summary>
public static class MenuPath
{
    /// <summary>Nombre de lignes dessinées dans chaque écran.</summary>
    public const int Rows = 5;

    private const char Separator = '›';

    /// <summary>
    /// Les étapes du chemin, dans l'ordre. Un chemin vide n'en donne aucune,
    /// et la vue n'affiche alors rien plutôt qu'un écran creux.
    /// </summary>
    public static IReadOnlyList<MenuStep> Steps(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        List<MenuStep> steps = [];
        var previous = -1;

        foreach (var part in path.Split(Separator))
        {
            var label = part.Trim();

            if (label.Length == 0)
            {
                continue;
            }

            var row = RowFor(label);

            // Deux écrans de suite surlignés à la même hauteur donnent une
            // image qui semble figée. On décale, ce qui suffit à ce que le
            // dessin ait l'air d'écrans distincts.
            if (row == previous)
            {
                row = (row + 1) % Rows;
            }

            steps.Add(new MenuStep(label, row));
            previous = row;
        }

        return steps;
    }

    /// <summary>
    /// La hauteur de la ligne surlignée, tirée du libellé lui-même.
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
