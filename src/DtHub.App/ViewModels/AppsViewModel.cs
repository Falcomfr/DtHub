using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Apps;
using DtHub.Core.Devices;
using DtHub.Core.Profiles;
using DtHub.Core.Settings;
using DtHub.Core.Users;

namespace DtHub.App.ViewModels;

/// <summary>Vue proposée dans le sélecteur d'applications.</summary>
public enum AppFilterMode
{
    Favorites,
    Applications,
    System,
}

/// <summary>
/// Page Applications : pour chaque téléphone et chaque profil Android, les
/// applications lançables, avec recherche, favoris et sélection.
/// </summary>
public sealed partial class AppsViewModel : PageViewModel
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly AndroidUserService _users;
    private readonly AppDiscoveryService _apps;
    private readonly SettingsService _settings;
    private readonly ProfileService _profiles;
    private readonly IDialogService _dialogs;

    private readonly List<AppItemViewModel> _allApps = [];

    public AppsViewModel(
        DeviceDiscoveryService discovery,
        AndroidUserService users,
        AppDiscoveryService apps,
        SettingsService settings,
        ProfileService profiles,
        IDialogService dialogs)
    {
        _discovery = discovery;
        _users = users;
        _apps = apps;
        _settings = settings;
        _profiles = profiles;
        _dialogs = dialogs;
    }

    public override string Title => "Applications";

    public override string Subtitle =>
        "Cochez les applications à ouvrir. Une application clonée apparaît une fois par profil Android.";

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public ObservableCollection<AppItemViewModel> VisibleApps { get; } = [];

    public ObservableCollection<LaunchProfile> Profiles { get; } = [];

    [ObservableProperty]
    private DeviceItemViewModel? _selectedDevice;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AppFilterMode _filterMode = AppFilterMode.Applications;

    [ObservableProperty]
    private LaunchProfile? _targetProfile;

    /// <summary>Date du dernier balayage, affichée à côté du bouton Actualiser.</summary>
    [ObservableProperty]
    private string? _lastScanText;

    public int SelectedCount => _allApps.Count(a => a.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(async token =>
        {
            await LoadDevicesAsync(token).ConfigureAwait(true);
            await LoadProfilesAsync(token).ConfigureAwait(true);
            await LoadAppsAsync(false, token).ConfigureAwait(true);
        }, cancellationToken);

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) =>
        RunAsync(token => LoadAppsAsync(true, token), cancellationToken);

    [RelayCommand]
    private void SetFilter(string mode) =>
        FilterMode = Enum.TryParse<AppFilterMode>(mode, out var parsed) ? parsed : AppFilterMode.Applications;

    [RelayCommand]
    private Task ToggleFavoriteAsync(AppItemViewModel? item) => RunAsync(async token =>
    {
        if (item is null)
        {
            return;
        }

        item.IsFavorite = await _settings
            .ToggleFavoriteAsync(item.Device.Id, item.User.Id, item.PackageName, token)
            .ConfigureAwait(true);

        ApplyFilter();
    });

    /// <summary>Ajoute la sélection à un profil, ou en crée un si aucun n'existe.</summary>
    [RelayCommand]
    private Task AddSelectionToProfileAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        var selected = _allApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Cochez d'abord au moins une application.";
            return;
        }

        var profile = TargetProfile;

        if (profile is null)
        {
            profile = await _profiles
                .CreateAsync("Mon profil", selected.Select(a => a.ToTarget()), token)
                .ConfigureAwait(true);

            await _profiles.SetDefaultAsync(profile.Id, token).ConfigureAwait(true);
        }
        else
        {
            foreach (var item in selected)
            {
                await _profiles.AddTargetAsync(profile.Id, item.ToTarget(), token).ConfigureAwait(true);
            }
        }

        await LoadProfilesAsync(token).ConfigureAwait(true);
        TargetProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);

        foreach (var item in selected)
        {
            item.IsSelected = false;
        }

        OnSelectionChanged();

        _dialogs.ShowInformation(
            $"{selected.Count} session(s) ajoutée(s) au profil « {TargetProfile?.Name} ».",
            "Sélection enregistrée");
    }, cancellationToken);

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in _allApps)
        {
            item.IsSelected = false;
        }

        OnSelectionChanged();
    }

    /// <summary>Appelée par la vue quand une case est cochée ou décochée.</summary>
    public void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
    }

    private async Task LoadDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await _discovery.RefreshAsync(cancellationToken).ConfigureAwait(true);
        var selectedId = SelectedDevice?.Id;

        Devices.Clear();
        foreach (var device in result.Devices.Where(d => d.IsConnected))
        {
            Devices.Add(new DeviceItemViewModel(device));
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectedId) ?? Devices.FirstOrDefault();

        if (Devices.Count == 0)
        {
            StatusMessage = "Aucun téléphone connecté. Branchez-en un ou associez-le depuis la page Appareils.";
        }
    }

    private async Task LoadProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(true);
        var previousId = TargetProfile?.Id;

        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        TargetProfile = Profiles.FirstOrDefault(p => p.Id == previousId) ?? Profiles.FirstOrDefault();
    }

    /// <summary>
    /// Charge les applications de tous les profils Android de l'appareil
    /// choisi. Chaque couple profil et application donne une entrée.
    /// </summary>
    private async Task LoadAppsAsync(bool refresh, CancellationToken cancellationToken)
    {
        _allApps.Clear();
        VisibleApps.Clear();
        LastScanText = null;

        if (SelectedDevice?.Device is not { } device || !device.IsConnected)
        {
            return;
        }

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        var users = await _users.GetUsersAsync(device.Serial, refresh, cancellationToken).ConfigureAwait(true);

        foreach (var user in users)
        {
            var apps = await _apps
                .GetAppsAsync(device.Id, device.Serial, user.Id, refresh, cancellationToken)
                .ConfigureAwait(true);

            foreach (var app in apps)
            {
                _allApps.Add(new AppItemViewModel(app, user, device)
                {
                    IsFavorite = settings.FavoriteApps.Contains(
                        SettingsService.FavoriteKey(device.Id, user.Id, app.PackageName)),
                });
            }
        }

        var lastScan = await _apps
            .GetLastScanAsync(device.Id, users[0].Id, cancellationToken)
            .ConfigureAwait(true);

        LastScanText = lastScan is null
            ? null
            : $"Dernière analyse : {lastScan.Value.ToLocalTime():dd/MM/yyyy HH:mm}";

        if (_allApps.Count == 0)
        {
            StatusMessage = "Aucune application lançable n'a été trouvée sur cet appareil.";
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchText;

        var filtered = _allApps
            .Where(a => FilterMode switch
            {
                AppFilterMode.Favorites => a.IsFavorite,
                AppFilterMode.System => a.IsSystem,
                _ => !a.IsSystem,
            })
            .Where(a => a.Matches(query))
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.User.Id);

        VisibleApps.Clear();
        foreach (var app in filtered)
        {
            VisibleApps.Add(app);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFilterModeChanged(AppFilterMode value) => ApplyFilter();

    partial void OnSelectedDeviceChanged(DeviceItemViewModel? value) =>
        _ = RunAsync(token => LoadAppsAsync(false, token));
}
