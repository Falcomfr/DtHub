using CommunityToolkit.Mvvm.ComponentModel;

namespace DtHub.App.ViewModels;

/// <summary>
/// Un onglet du cadre : le compte qu'il montre, et la fenêtre qu'il loge.
/// </summary>
public sealed partial class GameTabViewModel : ObservableObject
{
    public GameTabViewModel(string key, string title, string? iconPath, nint window, double aspect)
    {
        Key = key;
        _title = title;
        _iconPath = iconPath;
        Window = window;
        Aspect = aspect;
    }

    /// <summary>Clé de l'instance. Identifie l'onglet : elle ne change pas.</summary>
    public string Key { get; }

    /// <summary>Fenêtre de jeu logée, telle que Windows la désigne.</summary>
    public nint Window { get; }

    /// <summary>
    /// Rapport largeur sur hauteur de l'afficheur, celui que scrcpy verrouille.
    ///
    /// C'est lui qui donne sa forme au cadre : une zone d'accueil d'une autre
    /// forme laisserait une bande noire sur les côtés.
    /// </summary>
    public double Aspect { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _iconPath;

    /// <summary>
    /// Vrai pour l'onglet montré. Un seul l'est à la fois : les autres
    /// fenêtres sont cachées, non détruites, pour qu'y revenir soit immédiat.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Où l'onglet glissé se posera, montré par un trait à gauche ou à droite
    /// de celui que l'on survole.
    ///
    /// Sans ce trait, on lâchait à l'aveugle : rien ne disait de quel côté le
    /// dépôt tomberait, et il fallait recommencer pour comprendre.
    /// </summary>
    [ObservableProperty]
    private bool _dropBefore;

    [ObservableProperty]
    private bool _dropAfter;

    /// <summary>Efface les deux repères de dépôt.</summary>
    public void ClearDropHint()
    {
        DropBefore = false;
        DropAfter = false;
    }
}
