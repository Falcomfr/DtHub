using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;

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
    /// Vrai pendant l'ajout d'un compte. Créer un profil, y installer le jeu et
    /// le démarrer demande une quinzaine de secondes au téléphone : sans cette
    /// marque, le bouton restait cliquable et rien ne disait qu'il travaillait.
    /// </summary>
    /// <summary>
    /// Numéro de série ADB, qui est une adresse en sans-fil et change donc.
    /// L'identité stable est <see cref="DeviceId"/> ; celui-ci ne sert qu'à
    /// adresser une commande.
    /// </summary>
    public string Serial { get; private set; } = string.Empty;

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
        ? "Jeu non installé"
        : State switch
    {
        AdbDeviceState.Device => Connection == AdbConnectionKind.Usb ? "Connecté en USB" : "Connecté en Wi-Fi",
        AdbDeviceState.Unauthorized => "À autoriser sur le téléphone",
        AdbDeviceState.Offline => "Hors ligne",
        AdbDeviceState.NoPermissions => "Pilote USB refusé",
        _ => "Inconnu",
    };

    public string StatusBrushKey => HasNoGame ? "WarningBrush" : State switch
    {
        AdbDeviceState.Device => "SuccessBrush",
        AdbDeviceState.Unauthorized => "WarningBrush",
        AdbDeviceState.NoPermissions => "DangerBrush",
        _ => "TextMutedBrush",
    };

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
    }

}
