using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Dofus;

namespace DtHub.App.ViewModels;

/// <summary>Une instance du jeu dans une liste, avec son état et ses réglages.</summary>
public sealed partial class InstanceRowViewModel : ObservableObject
{
    public InstanceRowViewModel(DofusInstance instance)
    {
        _instance = instance;
        _isEnabled = instance.IsEnabled;
        _name = instance.DisplayName;
    }

    private bool _applying;

    [ObservableProperty]
    private DofusInstance _instance;

    /// <summary>Cochée pour le lancement automatique.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Nom affiché, modifiable.</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Vrai si une fenêtre est ouverte pour cette instance.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// Vrai pendant qu'une action est en cours sur cette instance. Une relance
    /// enchaîne l'arrêt de la session, l'arrêt forcé côté Android et le
    /// redémarrage de scrcpy : plusieurs secondes, pendant lesquelles un
    /// bouton grisé ne dit pas qu'il se passe quelque chose.
    /// </summary>
    [ObservableProperty]
    private bool _isWorking;

    public string Key => Instance.Key;

    /// <summary>Vrai pour la ligne que l'on est en train de déplacer.</summary>
    [ObservableProperty]
    private bool _isDragging;

    /// <summary>Vrai quand un dépôt ici insérerait juste au-dessus.</summary>
    [ObservableProperty]
    private bool _dropAbove;

    /// <summary>Vrai quand un dépôt ici insérerait juste en dessous.</summary>
    [ObservableProperty]
    private bool _dropBelow;

    public string DeviceId => Instance.DeviceId;

    public bool IsDeviceConnected => Instance.IsDeviceConnected;

    /// <summary>Profil Android d'origine, affiché en second plan.</summary>
    public string UserLabel => $"profil {Instance.UserId} · {Instance.UserName}";

    /// <summary>Signalé quand une case est cochée ou un nom modifié.</summary>
    public event EventHandler<InstanceRowViewModel>? EnabledChanged;

    public event EventHandler<InstanceRowViewModel>? NameChanged;

    /// <summary>
    /// Vrai tant que le nom saisi n'est pas écrit. Le balayage périodique ne
    /// doit pas le remplacer entre-temps par l'ancien : la saisie semblerait
    /// s'annuler toute seule.
    /// </summary>
    public bool IsRenaming { get; set; }

    public void Update(DofusInstance instance, bool isRunning)
    {
        Instance = instance;
        IsRunning = isRunning;

        if (!IsRenaming && !string.Equals(Name, instance.DisplayName, StringComparison.Ordinal))
        {
            // Écriture venue des réglages, pas de l'utilisateur : la
            // répercuter comme un renommage relancerait une écriture à
            // chaque balayage.
            _applying = true;

            try
            {
                Name = instance.DisplayName;
            }
            finally
            {
                _applying = false;
            }
        }

        OnPropertyChanged(nameof(IsDeviceConnected));
        OnPropertyChanged(nameof(UserLabel));
    }

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this, this);

    partial void OnNameChanged(string value)
    {
        if (_applying)
        {
            return;
        }

        IsRenaming = true;
        NameChanged?.Invoke(this, this);
    }
}
