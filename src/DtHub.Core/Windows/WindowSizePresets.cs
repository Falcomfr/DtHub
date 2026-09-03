using DtHub.Core.Localization;

namespace DtHub.Core.Windows;

/// <summary>
/// Les cinq tailles de fenêtre. Les quatre premières sont des pourcentages de
/// la zone utilisable de l'écran, donc proportionnelles à celui-ci : la même
/// taille 2 donne une petite fenêtre sur un écran d'ordinateur portable et une
/// grande sur un écran de bureau. La cinquième occupe l'écran entier, sans
/// bordure.
/// </summary>
public sealed record WindowSizePresets
{
    public static readonly WindowSizePresets Default = new();

    /// <summary>Pourcentages des quatre premières tailles, dans l'ordre.</summary>
    public IReadOnlyList<int> Percentages { get; init; } = [40, 60, 80, 100];

    /// <summary>Nombre total de tailles, plein écran compris.</summary>
    public int Count => Percentages.Count + 1;

    /// <summary>Indice de la taille plein écran, la dernière.</summary>
    public int FullscreenIndex => Percentages.Count;

    /// <summary>Vrai si l'indice désigne le plein écran sans bordure.</summary>
    public bool IsFullscreen(int index) => index == FullscreenIndex;

    /// <summary>
    /// Pourcentage associé à un indice. Un indice hors bornes retombe sur la
    /// valeur la plus proche : un raccourci mal configuré ne doit pas casser
    /// l'affichage.
    /// </summary>
    public int PercentageAt(int index) =>
        Percentages.Count == 0 ? 100 : Percentages[Math.Clamp(index, 0, Percentages.Count - 1)];

    /// <summary>Libellé affiché dans l'éditeur de raccourcis.</summary>
    public string LabelAt(int index) =>
        IsFullscreen(index) ? Strings.Get("ActionFullscreen") : $"Taille {index + 1} ({PercentageAt(index)} %)";

    /// <summary>
    /// Rend une copie assainie : pourcentages ramenés dans des bornes
    /// raisonnables, triés, dédoublonnés, et jamais vides.
    /// </summary>
    public WindowSizePresets Sanitized()
    {
        var cleaned = Percentages
            .Select(p => Math.Clamp(p, 20, 100))
            .Distinct()
            .Order()
            .ToList();

        return cleaned.Count == 0 ? Default : this with { Percentages = cleaned };
    }
}
