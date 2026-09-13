using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

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

    /// <summary>
    /// Vrai si ce compte s'ouvre dans le cadre à onglets.
    ///
    /// Basculer n'ouvre ni ne ferme rien : c'est la même fenêtre qu'on loge
    /// dans le cadre ou qu'on en ressort.
    /// </summary>
    [ObservableProperty]
    private bool _isTabbed;

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

    /// <summary>
    /// Chemin de l'icône du jeu dans le cache, quand elle a pu être extraite.
    ///
    /// Une chaîne et non une image : aucun type d'interface n'entre dans un
    /// modèle de vue ici, et c'est un convertisseur qui décode, une fois pour
    /// toutes les lignes qui partagent le même fichier.
    ///
    /// Rien ne la remet à zéro : le balayage met les lignes à jour au lieu de
    /// les recréer, si bien qu'une icône posée y reste et que la liste ne
    /// clignote pas.
    /// </summary>
    [ObservableProperty]
    private string? _iconPath;

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
    public string UserLabel => Strings.Format("ProfileOrigin", Instance.UserId, Instance.UserName);

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

    /// <summary>Signalé quand le compte entre dans le cadre à onglets ou en sort.</summary>
    public event EventHandler<InstanceRowViewModel>? TabbedChanged;

    /// <summary>Signalé quand le compte change de palier de qualité.</summary>
    public event EventHandler<InstanceRowViewModel>? QualityChanged;

    /// <summary>Signalé quand le compte change de distance dans le jeu.</summary>
    public event EventHandler<InstanceRowViewModel>? ZoomChanged;

    /// <summary>
    /// Le temps passé cette semaine, « 3 h 20 », ou <c>null</c> s'il n'y en a
    /// pas encore.
    ///
    /// Information seulement : aucune limite, aucun rappel. Qui joue cinq
    /// comptes finit par ne plus savoir lequel il fait vraiment tourner.
    ///
    /// <c>null</c> et non vide : c'est une infobulle, et WPF n'en montre
    /// aucune sur une valeur nulle, là où une chaîne vide donnerait une bulle
    /// grise sans rien dedans.
    /// </summary>
    public string? PlaytimeLabel
    {
        get
        {
            var seconds = Instance.PlayedThisWeek;

            if (seconds < 60)
            {
                return null;
            }

            var span = TimeSpan.FromSeconds(seconds);

            return span.TotalHours >= 1
                ? Strings.Format("PlaytimeHours", (int)span.TotalHours, span.Minutes)
                : Strings.Format("PlaytimeMinutes", span.Minutes);
        }
    }

    /// <summary>
    /// Palier propre à ce compte, ou <c>null</c> pour suivre le commun.
    ///
    /// On joue un compte et on en regarde quatre : le principal mérite mieux
    /// que les mules, et ce qu'on épargne aux mules est autant de processeur,
    /// de bande passante, de chaleur et de batterie en moins.
    /// </summary>
    [ObservableProperty]
    private StreamQuality? _quality;

    /// <summary>Vrai le temps que le palier soit écrit.</summary>
    public bool IsQualityPending { get; set; }

    /// <summary>Vrai quand le compte a son propre palier, donc que ça se voit.</summary>
    public bool HasOwnQuality => Quality is not null;

    /// <summary>Ce que le bouton affiche : le palier, ou rien s'il suit le commun.</summary>
    public string QualityLabel => Quality switch
    {
        StreamQuality.Low => Strings.Get("QualityLowShort"),
        StreamQuality.Medium => Strings.Get("QualityMediumShort"),
        StreamQuality.Maximum => Strings.Get("QualityMaximumShort"),
        StreamQuality.Custom => Strings.Get("QualityCustomShort"),
        _ => string.Empty,
    };

    /// <summary>Donne son palier au compte, ou le rend au commun avec <c>null</c>.</summary>
    [RelayCommand]
    private void PickQuality(StreamQuality? quality) => Quality = quality;

    /// <summary>Rend le compte au réglage commun.</summary>
    [RelayCommand]
    private void FollowSharedQuality() => Quality = null;

    /// <summary>
    /// Distance propre à ce compte, ou <c>null</c> pour suivre la commune.
    ///
    /// Le motif n'est pas celui du palier. Le palier économise ; la distance
    /// décide de ce qu'on voit. On veut du terrain sur le compte qu'on joue,
    /// et les mules dont on ne regarde que la barre de vie n'en ont pas besoin.
    /// </summary>
    [ObservableProperty]
    private GameZoom? _zoom;

    /// <summary>Vrai le temps que la distance soit écrite.</summary>
    public bool IsZoomPending { get; set; }

    /// <summary>Vrai quand le compte a sa propre distance, donc que ça se voit.</summary>
    public bool HasOwnZoom => Zoom is not null;

    /// <summary>Ce que le bouton affiche : la distance, ou rien si elle suit la commune.</summary>
    public string ZoomLabel => Zoom switch
    {
        GameZoom.Widest => Strings.Get("ZoomVeryFar"),
        GameZoom.Wide => Strings.Get("ZoomFar"),
        GameZoom.Normal => Strings.Get("ZoomNormal"),
        GameZoom.Close => Strings.Get("ZoomClose"),
        _ => string.Empty,
    };

    /// <summary>
    /// La distance avec laquelle sa fenêtre tourne en ce moment, ou
    /// <c>null</c> quand elle est fermée.
    ///
    /// Posée par la liste, qui la tient du lanceur : la distance est un
    /// argument de démarrage de scrcpy, figé pour toute la session, et le
    /// réglage choisi peut donc différer de celui qui s'affiche.
    /// </summary>
    public GameZoom? RunningZoom
    {
        get => _runningZoom;
        set
        {
            if (_runningZoom == value)
            {
                return;
            }

            _runningZoom = value;
            OnPropertyChanged(nameof(ZoomWaitsForReopen));
        }
    }

    private GameZoom? _runningZoom;

    /// <summary>
    /// Vrai quand la fenêtre ouverte tourne encore avec une autre distance que
    /// celle choisie.
    ///
    /// **C'est la mention qui manquait.** Le réglage commun referme et rouvre
    /// les fenêtres pour se montrer tout de suite ; celui d'un compte ne le
    /// fait pas, parce que rouvrir déconnecte le personnage. Sans rien dire,
    /// le réglage paraissait mort. Il ne l'est pas : il attend.
    /// </summary>
    public bool ZoomWaitsForReopen =>
        RunningZoom is { } running && Zoom is { } wanted && running != wanted;

    /// <summary>Donne sa distance au compte, ou la rend à la commune avec <c>null</c>.</summary>
    [RelayCommand]
    private void PickZoom(GameZoom? zoom) => Zoom = zoom;

    /// <summary>Rend le compte à la distance commune.</summary>
    [RelayCommand]
    private void FollowSharedZoom() => Zoom = null;

    /// <summary>
    /// Vrai tant que le nom saisi n'est pas écrit. Le balayage périodique ne
    /// doit pas le remplacer entre-temps par l'ancien : la saisie semblerait
    /// s'annuler toute seule.
    /// </summary>
    public bool IsRenaming { get; set; }

    /// <summary>
    /// Vrai entre le clic de l'utilisateur et la fin de l'écriture.
    ///
    /// Le balayage périodique reconstruit la liste à partir des réglages, et
    /// écrasait le choix tant qu'il n'était pas enregistré : le verrou se
    /// rouvrait tout seul quelques secondes après avoir été fermé. Même
    /// mécanisme que pour le renommage, et pour la même raison.
    /// </summary>
    public bool IsManagedPending { get; set; }

    /// <summary>Vrai le temps que la bascule d'onglet soit écrite.</summary>
    public bool IsTabbedPending { get; set; }

    /// <inheritdoc cref="IsManagedPending" />
    public bool IsEnabledPending { get; set; }

    public void Update(DofusInstance instance, bool isRunning)
    {
        Instance = instance;
        IsRunning = isRunning;

        if (!IsTabbedPending && IsTabbed != instance.IsTabbed)
        {
            _applying = true;

            try
            {
                IsTabbed = instance.IsTabbed;
            }
            finally
            {
                _applying = false;
            }
        }

        OnPropertyChanged(nameof(PlaytimeLabel));

        if (!IsQualityPending && Quality != instance.Quality)
        {
            // Écriture venue des réglages : la répercuter comme un choix de
            // l'utilisateur relancerait une écriture à chaque balayage.
            _applying = true;

            try
            {
                Quality = instance.Quality;
            }
            finally
            {
                _applying = false;
            }
        }

        if (!IsZoomPending && Zoom != instance.Zoom)
        {
            // Écriture venue des réglages : la répercuter comme un choix de
            // l'utilisateur relancerait une écriture à chaque balayage.
            _applying = true;

            try
            {
                Zoom = instance.Zoom;
            }
            finally
            {
                _applying = false;
            }
        }

        if (!IsManagedPending && IsManaged != instance.IsManaged)
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

    partial void OnQualityChanged(StreamQuality? value)
    {
        OnPropertyChanged(nameof(QualityLabel));
        OnPropertyChanged(nameof(HasOwnQuality));

        if (_applying)
        {
            return;
        }

        IsQualityPending = true;
        QualityChanged?.Invoke(this, this);
    }

    partial void OnZoomChanged(GameZoom? value)
    {
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(HasOwnZoom));
        OnPropertyChanged(nameof(ZoomWaitsForReopen));

        if (_applying)
        {
            return;
        }

        IsZoomPending = true;
        ZoomChanged?.Invoke(this, this);
    }

    partial void OnIsTabbedChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        IsTabbedPending = true;
        TabbedChanged?.Invoke(this, this);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        IsEnabledPending = true;
        EnabledChanged?.Invoke(this, this);
    }

    partial void OnIsManagedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLocked));

        if (!_applying)
        {
            IsManagedPending = true;
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
