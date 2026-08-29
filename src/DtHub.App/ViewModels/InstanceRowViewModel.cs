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

    public string Key => Instance.Key;

    public string DeviceId => Instance.DeviceId;

    public bool IsDeviceConnected => Instance.IsDeviceConnected;

    /// <summary>Profil Android d'origine, affiché en second plan.</summary>
    public string UserLabel => $"profil {Instance.UserId} · {Instance.UserName}";

    /// <summary>Signalé quand une case est cochée ou un nom modifié.</summary>
    public event EventHandler<InstanceRowViewModel>? EnabledChanged;

    public event EventHandler<InstanceRowViewModel>? NameChanged;

    public void Update(DofusInstance instance, bool isRunning)
    {
        Instance = instance;
        IsRunning = isRunning;

        if (!string.Equals(Name, instance.DisplayName, StringComparison.Ordinal))
        {
            Name = instance.DisplayName;
        }

        OnPropertyChanged(nameof(IsDeviceConnected));
        OnPropertyChanged(nameof(UserLabel));
    }

    partial void OnIsEnabledChanged(bool value) => EnabledChanged?.Invoke(this, this);

    partial void OnNameChanged(string value) => NameChanged?.Invoke(this, this);
}
