using System.Text.Json.Serialization;

namespace DtHub.Core.Windows;

/// <summary>
/// Où se trouve une fenêtre de l'application, telle que Windows la retient.
///
/// Le rectangle est en pixels du bureau, et non dans les coordonnées WPF :
/// celles-ci dépendent de la mise à l'échelle de l'écran qui porte la fenêtre,
/// si bien qu'un même chiffre ne désigne pas le même endroit d'un écran à
/// l'autre. Sur deux écrans de densités différentes, c'est la seule façon de
/// retrouver la place exacte.
/// </summary>
public sealed record WindowPlacement
{
    public int Left { get; init; }

    public int Top { get; init; }

    public int Right { get; init; }

    public int Bottom { get; init; }

    /// <summary>Vrai si la fenêtre était agrandie.</summary>
    public bool Maximized { get; init; }

    /// <summary>
    /// Vrai si le rectangle a une surface. Une fenêtre jamais affichée en rend
    /// un vide, qu'il ne faut ni enregistrer ni appliquer.
    ///
    /// Hors du fichier : c'est une lecture des quatre bords, pas une valeur à
    /// retenir.
    /// </summary>
    [JsonIgnore]
    public bool IsSized => Right > Left && Bottom > Top;
}
