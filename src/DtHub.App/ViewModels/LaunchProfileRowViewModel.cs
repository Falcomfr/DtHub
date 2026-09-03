using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Un profil de lancement, tel qu'il paraît dans la liste.
///
/// Le résumé accompagne le nom : « Duo pêche » ne dit pas quels comptes il
/// ouvre, et l'on ne veut pas avoir à l'ouvrir pour s'en souvenir.
/// </summary>
public sealed partial class LaunchProfileRowViewModel : ObservableObject
{
    public LaunchProfileRowViewModel(string name, string summary, bool isDefault)
    {
        Name = name;
        _summary = summary;
        _isDefault = isDefault;
    }

    /// <summary>Nom retenu. Il identifie le profil : il ne change pas.</summary>
    public string Name { get; }

    [ObservableProperty]
    private string _summary;

    /// <summary>Vrai si ce profil s'ouvre au démarrage.</summary>
    [ObservableProperty]
    private bool _isDefault;

    /// <summary>
    /// Ce que la ligne annonce d'elle-même.
    ///
    /// Sans cela, une liste nomme ses entrées d'après le type : relevées à
    /// l'automatisation, elles s'appelaient toutes
    /// « DtHub.App.ViewModels.LaunchProfileRowViewModel ». Le gabarit
    /// d'affichage ne corrige pas ce nom-là.
    /// </summary>
    public override string ToString() => $"{Name}  ·  {Summary}";
}
