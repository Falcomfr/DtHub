using CommunityToolkit.Mvvm.ComponentModel;

using DtHub.Core.Adb;
using DtHub.Core.Apps;
using DtHub.Core.Devices;
using DtHub.Core.Profiles;
using DtHub.Core.Scrcpy;
using DtHub.Core.Users;

namespace DtHub.App.ViewModels;

/// <summary>Un téléphone dans une liste, avec son état lisible.</summary>
public sealed partial class DeviceItemViewModel : ObservableObject
{
    public DeviceItemViewModel(AndroidDevice device) => _device = device;

    [ObservableProperty]
    private AndroidDevice _device;

    [ObservableProperty]
    private bool _isSelected;

    public string Id => Device.Id;

    public string DisplayName => Device.DisplayName;

    /// <summary>Ligne secondaire : connexion, adresse, version d'Android.</summary>
    public string Details
    {
        get
        {
            List<string> parts = [StatusText];

            if (Device.ConnectionKind == AdbConnectionKind.Wireless && Device.ReconnectAddress is { } address)
            {
                parts.Add(address);
            }
            else if (Device.ConnectionKind == AdbConnectionKind.Usb)
            {
                parts.Add("USB");
            }

            if (Device.AndroidVersion is { Length: > 0 } version)
            {
                parts.Add($"Android {version}");
            }

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Libellé d'état, tel qu'affiché dans la pastille.</summary>
    public string StatusText => Device.State switch
    {
        AdbDeviceState.Device => "Connecté",
        AdbDeviceState.Unauthorized => "À autoriser",
        AdbDeviceState.Offline => "Hors ligne",
        AdbDeviceState.Connecting => "Connexion…",
        AdbDeviceState.Authorizing => "Autorisation…",
        AdbDeviceState.NoPermissions => "Pilote refusé",
        _ => "Inconnu",
    };

    /// <summary>Clé de couleur de la pastille, résolue dans le thème.</summary>
    public string StatusBrushKey => Device.State switch
    {
        AdbDeviceState.Device => "SuccessBrush",
        AdbDeviceState.Unauthorized or AdbDeviceState.Connecting or AdbDeviceState.Authorizing => "WarningBrush",
        AdbDeviceState.NoPermissions => "DangerBrush",
        _ => "TextMutedBrush",
    };

    public bool IsConnected => Device.IsConnected;

    /// <summary>Remplace l'appareil et rafraîchit tout ce qui en dépend.</summary>
    public void Update(AndroidDevice device)
    {
        Device = device;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
        OnPropertyChanged(nameof(IsConnected));
    }
}

/// <summary>Une application dans le sélecteur, pour un profil Android donné.</summary>
public sealed partial class AppItemViewModel : ObservableObject
{
    public AppItemViewModel(AndroidApp app, AndroidUser user, AndroidDevice device)
    {
        App = app;
        User = user;
        Device = device;
    }

    public AndroidApp App { get; }

    public AndroidUser User { get; }

    public AndroidDevice Device { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFavorite;

    public string DisplayName => App.DisplayName;

    public string PackageName => App.PackageName;

    public string UserLabel => User.DisplayName;

    public bool IsSystem => App.IsSystem;

    /// <summary>Initiale affichée dans la pastille qui tient lieu d'icône.</summary>
    public string Monogram => string.IsNullOrWhiteSpace(DisplayName)
        ? "?"
        : DisplayName.Trim()[..1].ToUpperInvariant();

    /// <summary>
    /// Teinte déterministe dérivée du nom de paquet : deux applications se
    /// distinguent d'un coup d'œil, sans avoir besoin de la vraie icône.
    /// </summary>
    public string MonogramColor
    {
        get
        {
            var hash = 17;
            foreach (var c in PackageName)
            {
                hash = (hash * 31) + c;
            }

            var hue = Math.Abs(hash) % 360;
            var (r, g, b) = HsvToRgb(hue, 0.45, 0.75);

            return $"#FF{r:X2}{g:X2}{b:X2}";
        }
    }

    /// <summary>Cible de lancement correspondante.</summary>
    public LaunchTarget ToTarget() => new()
    {
        DeviceId = Device.Id,
        UserId = User.Id,
        PackageName = App.PackageName,
        LaunchComponent = App.LaunchComponent,
        AppLabel = App.DisplayName,
        DeviceLabel = Device.DisplayName,
        UserLabel = User.DisplayName,
    };

    /// <summary>Vrai si l'entrée correspond au texte cherché.</summary>
    public bool Matches(string? query) =>
        string.IsNullOrWhiteSpace(query)
        || DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || PackageName.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static (int R, int G, int B) HsvToRgb(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return ((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }
}

/// <summary>Une session ouverte, telle que présentée sur l'accueil.</summary>
public sealed partial class SessionItemViewModel : ObservableObject
{
    public SessionItemViewModel(ScrcpySession session) => _session = session;

    [ObservableProperty]
    private ScrcpySession _session;

    public string DisplayName => Session.DisplayName;

    public string StatusText => Session.State switch
    {
        ScrcpySessionState.Starting => "Ouverture…",
        ScrcpySessionState.Running => "En cours",
        ScrcpySessionState.Failed => Session.FailureMessage ?? "Erreur",
        _ => "Fermée",
    };

    public string StatusBrushKey => Session.State switch
    {
        ScrcpySessionState.Running => "SuccessBrush",
        ScrcpySessionState.Starting => "WarningBrush",
        ScrcpySessionState.Failed => "DangerBrush",
        _ => "TextMutedBrush",
    };

    public void Refresh()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrushKey));
    }
}
