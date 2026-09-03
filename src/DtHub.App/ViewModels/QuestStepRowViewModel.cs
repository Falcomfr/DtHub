using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Une étape du guide, telle qu'elle paraît dans la liste où on la choisit.
///
/// Les deux flèches avancent d'une étape à la fois : sur un guide qui en compte
/// douze, revenir à la troisième demandait neuf clics, et rien ne disait ce
/// qu'on trouverait en chemin.
/// </summary>
public sealed partial class QuestStepRowViewModel : ObservableObject
{
    public QuestStepRowViewModel(int index, string label)
    {
        Index = index;
        Label = label;
    }

    /// <summary>Rang de l'étape, compté à partir de zéro comme dans la page.</summary>
    public int Index { get; }

    /// <summary>Le numéro montré, compté à partir de un comme sur le site.</summary>
    public string Rank => (Index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Ce qu'il y a à y faire, résumé comme dans le bandeau.</summary>
    public string Label { get; }

    /// <summary>Vrai pour l'étape où l'on est.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
