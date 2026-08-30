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
        _isManaged = instance.IsManaged;
        _name = instance.DisplayName;
    }

    private bool _applying;

    [ObservableProperty]
    private DofusInstance _instance;

    /// <summary>Cochée pour le lancement automatique.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>
    /// Cochée si la fenêtre suit les placements automatiques : parcours au
    /// clavier, replacement, côte à côte, changements de taille. Décochée,
    /// elle reste où elle est et le reste s'arrange sans elle.
    /// </summary>
    [ObservableProperty]
    private bool _isManaged = true;

    /// <summary>
    /// L'inverse, tel que la ligne le présente : un verrou, éteint par défaut.
    ///
    /// Le réglage enregistré dit ce que la fenêtre suit, ce qui est le bon sens
    /// pour du code ; l'interface dit ce que l'utilisateur décide, et il décide
    /// d'immobiliser une fenêtre, pas d'en libérer huit.
    /// </summary>
    public bool IsLocked
    {
        get => !IsManaged;
        set => IsManaged = !value;
    }

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
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isWorking;

    /// <summary>
    /// Vrai quand le téléphone qui porte cette instance est occupé par une
    /// autre ouverture. La ligne n'a pas été cliquée : elle attend son tour.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isDeviceBusy;

    /// <summary>
    /// Vrai quand la ligne doit montrer l'indicateur plutôt que ses boutons,
    /// que ce soit pour son propre travail ou celui d'une voisine du même
    /// téléphone.
    ///
    /// Deux champs et non un : <see cref="IsWorking"/> sert aussi de garde-fou
    /// de réentrance et est remis à faux dans un finally, qui éteindrait sinon
    /// l'indicateur d'une voisine.
    /// </summary>
    public bool IsBusy => IsWorking || IsDeviceBusy;

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

    /// <summary>Appareil qui porte cette instance. Il peint l'en-tête, quand il y en a un.</summary>
    [ObservableProperty]
    private DeviceGroupViewModel? _device;

    /// <summary>
    /// Vrai quand cette ligne ouvre une suite d'instances du même appareil, et
    /// doit donc en porter le nom.
    /// </summary>
    [ObservableProperty]
    private bool _showDeviceHeader;

    /// <summary>
    /// Vrai quand cette ligne est le premier morceau de son appareil. Ce qui
    /// vaut pour l'appareil lui-même, comme rompre l'association, ne s'affiche
    /// que là : il n'y a aucune raison de le proposer deux fois.
    /// </summary>
    [ObservableProperty]
    private bool _isFirstOfDevice;

    public bool IsDeviceConnected => Instance.IsDeviceConnected;

    /// <summary>Profil Android d'origine, affiché en second plan.</summary>
    public string UserLabel => $"profil {Instance.UserId} · {Instance.UserName}";

    /// <summary>
    /// Vrai quand le nom affiché ne dit plus de quel profil il s'agit, donc
    /// quand l'utilisateur l'a renommé. Sans renommage, le rappel répéterait
    /// le nom juste au-dessus et coûterait une ligne pour rien.
    /// </summary>
    public bool ShowUserLabel =>
        !string.Equals(Name, Instance.UserName, StringComparison.Ordinal);

    /// <summary>Signalé quand une case est cochée ou un nom modifié.</summary>
    public event EventHandler<InstanceRowViewModel>? EnabledChanged;

    /// <summary>Signalé quand la fenêtre entre ou sort des placements automatiques.</summary>
    public event EventHandler<InstanceRowViewModel>? ManagedChanged;

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

        if (IsManaged != instance.IsManaged)
        {
            // Écriture venue des réglages : la répercuter comme un choix de
            // l'utilisateur relancerait une écriture à chaque balayage.
            _applying = true;

            try
            {
                IsManaged = instance.IsManaged;
            }
            finally
            {
                _applying = false;
            }
        }

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
        OnPropertyChanged(nameof(ShowUserLabel));
    }

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this, this);

    partial void OnIsManagedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLocked));

        if (!_applying)
        {
            ManagedChanged?.Invoke(this, this);
        }
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(ShowUserLabel));

        if (_applying)
        {
            return;
        }

        IsRenaming = true;
        NameChanged?.Invoke(this, this);
    }
}
