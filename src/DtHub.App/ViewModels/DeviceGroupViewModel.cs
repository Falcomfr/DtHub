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

    [ObservableProperty]
    private AdbDeviceState _state = AdbDeviceState.Offline;

    [ObservableProperty]
    private AdbConnectionKind _connection = AdbConnectionKind.Unknown;

    public bool IsConnected => State == AdbDeviceState.Device;

    /// <summary>
    /// Vrai quand l'appareil répond mais qu'aucun profil ne porte le jeu.
    /// Il n'a alors aucune ligne dans la liste, et disparaîtrait sans un mot.
    /// </summary>
    [ObservableProperty]
    private bool _hasNoGame;

    public string StatusText => HasNoGame && IsConnected
        ? "Jeu non installé"
        : State switch
    {
        AdbDeviceState.Device => Connection == AdbConnectionKind.Usb ? "Connecté en USB" : "Connecté en Wi-Fi",
        AdbDeviceState.Unauthorized => "À autoriser sur le téléphone",
        AdbDeviceState.Offline => "Hors ligne",
        AdbDeviceState.NoPermissions => "Pilote USB refusé",
        _ => "Inconnu",
    };

    public string StatusBrushKey => HasNoGame && IsConnected ? "WarningBrush" : State switch
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
        State = device.State;
        Connection = device.ConnectionKind;

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
    }

    partial void OnHasNoGameChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
    }
}
