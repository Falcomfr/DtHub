using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Une session nommée, telle qu'elle paraît dans la liste déroulante.
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

    /// <summary>Vrai si cette session s'ouvre au démarrage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isDefault;

    /// <summary>
    /// Ce qui s'affiche dans la liste. L'étoile marque la session du
    /// démarrage : sans elle, il faudrait ouvrir chaque profil pour savoir
    /// lequel est désigné.
    /// </summary>
    public string Label => IsDefault ? $"★  {Name}  ·  {Summary}" : $"{Name}  ·  {Summary}";
}
