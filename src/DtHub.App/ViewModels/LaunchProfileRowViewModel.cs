using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Un profil de lancement, tel qu'il paraît dans la liste déroulante.
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
    [NotifyPropertyChangedFor(nameof(Label))]
    private string _summary;

    /// <summary>Vrai si ce profil s'ouvre au démarrage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isDefault;

    /// <summary>
    /// Ce qui s'affiche dans la liste. L'étoile marque le profil du
    /// démarrage : sans elle, il faudrait ouvrir chaque profil pour savoir
    /// lequel est désigné.
    /// </summary>
    public string Label => IsDefault ? $"★  {Name}  ·  {Summary}" : $"{Name}  ·  {Summary}";

    /// <summary>
    /// Ce que la ligne annonce d'elle-même.
    ///
    /// Sans cela, une liste déroulante nomme ses entrées d'après le type :
    /// relevé à l'automatisation, elles s'appelaient toutes
    /// « DtHub.App.ViewModels.LaunchProfileRowViewModel ». Le gabarit d'affichage
    /// ne corrige pas ce nom-là, et c'est celui qu'un lecteur d'écran prononce.
    /// </summary>
    public override string ToString() => Label;
}
