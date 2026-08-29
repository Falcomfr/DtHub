using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Devices;

namespace DtHub.App.ViewModels;

/// <summary>Un téléphone et les instances du jeu qu'il porte.</summary>
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

    public ObservableCollection<InstanceRowViewModel> Instances { get; } = [];

    public bool IsConnected => State == AdbDeviceState.Device;

    public string StatusText => State switch
    {
        AdbDeviceState.Device => Connection == AdbConnectionKind.Usb ? "Connecté en USB" : "Connecté en Wi-Fi",
        AdbDeviceState.Unauthorized => "À autoriser sur le téléphone",
        AdbDeviceState.Offline => "Hors ligne",
        AdbDeviceState.NoPermissions => "Pilote USB refusé",
        _ => "Inconnu",
    };

    public string StatusBrushKey => State switch
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

    /// <summary>Marque le téléphone comme absent, ses instances restant listées.</summary>
    public void MarkOffline()
    {
        State = AdbDeviceState.Offline;

        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
    }
}
