using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Localization;

namespace DtHub.App.ViewModels;

/// <summary>
/// Un téléphone, tel qu'il paraît au-dessus de ses instances.
///
/// Les instances ne lui appartiennent plus : elles vivent dans une liste
/// unique où elles se trient librement. Cet objet est partagé par toutes les
/// lignes du même téléphone, si bien qu'une seule mise à jour d'état les
/// prévient toutes.
/// </summary>
public sealed partial class DeviceGroupViewModel : ObservableObject
{
    public DeviceGroupViewModel(string deviceId, string name) => (DeviceId, _name) = (deviceId, name);

    public string DeviceId { get; }

    [ObservableProperty]
    private string _name;

    /// <summary>
    /// Numéro de série ADB, qui est une adresse en sans-fil et change donc.
    /// L'identité stable est <see cref="DeviceId"/> ; celui-ci ne sert qu'à
    /// adresser une commande.
    /// </summary>
    public string Serial { get; private set; } = string.Empty;

    /// <summary>
    /// Vrai pendant l'ajout d'un compte. Créer un profil, y installer le jeu et
    /// le démarrer demande une quinzaine de secondes au téléphone : sans cette
    /// marque, le bouton restait cliquable et rien ne disait qu'il travaillait.
    /// </summary>
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private AdbDeviceState _state = AdbDeviceState.Offline;

    [ObservableProperty]
    private AdbConnectionKind _connection = AdbConnectionKind.Unknown;

    public bool IsConnected => State == AdbDeviceState.Device;

    /// <summary>
    /// Vrai quand l'appareil répond mais qu'aucun profil ne porte le jeu.
    /// Il n'a alors aucune ligne dans la liste, et disparaîtrait sans un mot.
    /// </summary>
    private bool _hasNoGame;

    /// <summary>Vrai seulement pour un appareil joignable sans le jeu.</summary>
    public bool HasNoGame
    {
        get => _hasNoGame && IsConnected;
        set
        {
            if (_hasNoGame == value)
            {
                return;
            }

            _hasNoGame = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrushKey));
        }
    }

    /// <summary>
    /// Vrai quand cet appareil s'annonce sur le réseau et refuse ce PC.
    ///
    /// C'est le seul cas où « hors ligne » induit en erreur : le téléphone est
    /// là, allumé, son débogage sans fil est actif, et il ne manque qu'une
    /// nouvelle association. Sans ce mot, on cherche du côté du réseau, on
    /// rallume ce qui est déjà allumé, et on n'a aucune raison de penser à
    /// réassocier puisqu'on n'a rien désassocié.
    /// </summary>
    private bool _needsPairing;

    /// <summary>Pose le doute sur l'association, et prévient l'affichage.</summary>
    public void SetNeedsPairing(bool needed)
    {
        if (_needsPairing == needed)
        {
            return;
        }

        _needsPairing = needed;

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTip));
        OnPropertyChanged(nameof(StatusBrushKey));
    }

    public string StatusText => _needsPairing && !IsConnected
        ? Strings.Get("ToPairAgain")
        : HasNoGame
        ? Strings.Get("GameNotInstalled")
        : State switch
        {
            AdbDeviceState.Device => Strings.Get(
                Connection == AdbConnectionKind.Usb ? "ConnectedByUsb" : "ConnectedByWifi"),
            AdbDeviceState.Unauthorized => Strings.Get("ToAuthorizeOnPhone"),
            AdbDeviceState.Offline => Strings.Get("Offline"),
            AdbDeviceState.NoPermissions => Strings.Get("UsbDriverRefused"),
            _ => Strings.Get("UnknownState"),
        };

    /// <summary>
    /// Ce que le survol explique, quand l'état seul ne suffit pas.
    ///
    /// Deux mots dans une liste ne peuvent pas dire quoi faire. « À
    /// réassocier » dit ce qui manque, et la bulle dit par où.
    ///
    /// **« Hors ligne » a droit à la sienne, et c'est le cas qui manquait.**
    /// L'application ne sait pas toujours pourquoi un téléphone ne répond
    /// pas : il peut être éteint, sur un autre réseau, avoir son débogage
    /// sans fil coupé, ou avoir oublié la clé de ce PC. Elle ne peut pas
    /// trancher, mais elle peut dire dans quel ordre chercher, au lieu de
    /// laisser deviner. Un utilisateur a passé une soirée sur exactement
    /// cette question, et la réponse était la dernière de la liste.
    /// </summary>
    public string? StatusTip => !IsConnected
        ? Strings.Get(_needsPairing ? "ToPairAgainTip" : "OfflineTip")
        : null;

    public string StatusBrushKey => _needsPairing && !IsConnected
        ? "WarningBrush"
        : HasNoGame ? "WarningBrush" : State switch
        {
            AdbDeviceState.Device => "SuccessBrush",
            AdbDeviceState.Unauthorized => "WarningBrush",
            AdbDeviceState.NoPermissions => "DangerBrush",
            _ => "TextMutedBrush",
        };

    /// <summary>
    /// La dernière lecture de batterie, ou <c>null</c> tant qu'on ne sait pas.
    ///
    /// Elle était déjà faite toutes les minutes, et seule l'alerte des vingt
    /// pour cent en sortait. Le niveau lui-même vaut mieux : il se regarde
    /// avant de lancer cinq comptes, pas une fois qu'il est trop tard.
    /// </summary>
    private BatteryReading? _battery;

    /// <summary>Largeur intérieure de la jauge, en pixels de mise en page.</summary>
    private const double GaugeWidth = 14;

    /// <summary>Vrai quand il y a un niveau à montrer.</summary>
    public bool HasBattery => _battery is not null && IsConnected;

    /// <summary>Le niveau seul, « 84 % ».</summary>
    public string BatteryText => _battery?.Label ?? string.Empty;

    /// <summary>Le niveau en une phrase, avec la charge s'il y a lieu.</summary>
    public string BatterySummary => _battery?.Summary ?? string.Empty;

    /// <summary>Vrai quand l'appareil est branché, ce que dit l'éclair.</summary>
    public bool IsCharging => _battery?.Charging ?? false;

    /// <summary>
    /// La part remplie de la jauge. Jamais tout à fait nulle : à trois pour
    /// cent, une jauge vide se lit comme une jauge en panne.
    /// </summary>
    public double BatteryFill => _battery is null
        ? 0
        : Math.Max(2, Math.Round(GaugeWidth * _battery.Percent / 100.0));

    /// <summary>
    /// La couleur du remplissage. Discrète tant que rien ne presse : une
    /// jauge qui crie à quatre-vingts pour cent n'apprend rien.
    /// </summary>
    public string BatteryBrushKey => _battery?.Concern switch
    {
        HealthSeverity.Serious => "DangerBrush",
        HealthSeverity.Warning => "WarningBrush",
        _ => "TextMutedBrush",
    };

    /// <summary>
    /// Ce que cet appareil a de travers, un constat par ligne, ou <c>null</c>
    /// quand il n'a rien.
    ///
    /// Sous son nom plutôt que dans un bandeau commun : réunis en bas de
    /// liste, les constats semblaient parler du dernier appareil affiché.
    /// </summary>
    private string? _problems;

    private bool _problemsAreSerious;

    /// <summary>Vrai quand cet appareil a quelque chose à signaler.</summary>
    public bool HasProblems => !string.IsNullOrEmpty(_problems);

    /// <summary>Les constats, un par ligne, du plus grave au plus anodin.</summary>
    public string Problems => _problems ?? string.Empty;

    /// <summary>La couleur du sigle et du texte : rouge si la séance est en jeu.</summary>
    public string ProblemsBrushKey => _problemsAreSerious ? "DangerBrush" : "WarningBrush";

    /// <summary>Pose ce que le bilan a trouvé pour cet appareil.</summary>
    public void SetProblems(string? problems, bool serious)
    {
        if (string.Equals(_problems, problems, StringComparison.Ordinal)
            && _problemsAreSerious == serious)
        {
            return;
        }

        (_problems, _problemsAreSerious) = (problems, serious);

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(ProblemsBrushKey));
    }

    /// <summary>Pose la dernière lecture, et prévient l'affichage.</summary>
    public void SetBattery(BatteryReading? battery)
    {
        if (_battery == battery)
        {
            return;
        }

        _battery = battery;

        OnPropertyChanged(nameof(HasBattery));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(BatterySummary));
        OnPropertyChanged(nameof(IsCharging));
        OnPropertyChanged(nameof(BatteryFill));
        OnPropertyChanged(nameof(BatteryBrushKey));
    }

    public void Update(AndroidDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        Name = device.DisplayName;
        Serial = device.Serial;
        State = device.State;
        Connection = device.ConnectionKind;

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
        OnPropertyChanged(nameof(HasBattery));
    }

}
