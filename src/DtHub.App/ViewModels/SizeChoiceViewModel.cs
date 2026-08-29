using DtHub.Core.Windows;

namespace DtHub.App.ViewModels;

/// <summary>Une taille proposée dans le configurateur.</summary>
public sealed record SizeChoiceViewModel(int Index, string Label, string Detail)
{
    /// <summary>Construit les cinq choix à partir des tailles configurées.</summary>
    public static IReadOnlyList<SizeChoiceViewModel> From(WindowSizePresets presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        List<SizeChoiceViewModel> choices = [];

        for (var i = 0; i < presets.Count; i++)
        {
            choices.Add(presets.IsFullscreen(i)
                ? new SizeChoiceViewModel(i, "⛶", "Plein écran")
                : new SizeChoiceViewModel(
                    i,
                    (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"{presets.PercentageAt(i)} % de l'écran"));
        }

        return choices;
    }
}
