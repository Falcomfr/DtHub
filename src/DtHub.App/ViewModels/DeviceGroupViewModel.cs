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

    public string StatusText => HasNoGame
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

    public string StatusBrushKey => HasNoGame ? "WarningBrush" : State switch
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
