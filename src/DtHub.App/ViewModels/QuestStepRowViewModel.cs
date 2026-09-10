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
    public QuestStepRowViewModel(int index, string rank, string label)
    {
        Index = index;
        Rank = rank;
        Label = label;
    }

    /// <summary>Rang de l'étape, compté à partir de zéro comme dans la page.</summary>
    public int Index { get; }

    /// <summary>
    /// Ce qui se lit à gauche de la ligne : un numéro, ou « Départ ».
    ///
    /// Donné et non déduit du rang, parce que le départ n'est pas numéroté : il
    /// n'est pas une étape du parcours mais l'endroit où l'on se rend pour le
    /// commencer.
    /// </summary>
    public string Rank { get; }

    /// <summary>Ce qu'il y a à y faire, quand il y a quelque chose à en dire.</summary>
    public string Label { get; }

    /// <summary>Vrai pour l'étape où l'on est.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
