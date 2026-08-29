using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Devices;
using DtHub.Core.Profiles;
using DtHub.Core.Scrcpy;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// Écran principal : les téléphones, le profil choisi, les sessions qu'il
/// ouvrira, et un bouton pour tout lancer.
/// </summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private readonly DeviceDiscoveryService _discovery;
    private readonly ProfileService _profiles;
    private readonly SettingsService _settings;
    private readonly SessionOrchestrator _orchestrator;
    private readonly IDialogService _dialogs;

    public HomeViewModel(
        DeviceDiscoveryService discovery,
        ProfileService profiles,
        SettingsService settings,
        SessionOrchestrator orchestrator,
        IDialogService dialogs)
    {
        _discovery = discovery;
        _profiles = profiles;
        _settings = settings;
        _orchestrator = orchestrator;
        _dialogs = dialogs;

        _orchestrator.SessionChanged += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(RefreshSessions);
    }

    public override string Title => "Accueil";

    public override string Subtitle =>
        "Choisissez un profil, vérifiez les téléphones, puis lancez toutes les sessions d'un coup.";

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public ObservableCollection<LaunchProfile> Profiles { get; } = [];

    /// <summary>Sessions que le profil sélectionné ouvrira.</summary>
    public ObservableCollection<LaunchTarget> PlannedTargets { get; } = [];

    /// <summary>Sessions actuellement ouvertes.</summary>
    public ObservableCollection<SessionItemViewModel> OpenSessions { get; } = [];

    [ObservableProperty]
    private LaunchProfile? _selectedProfile;

    /// <summary>Vrai tant qu'aucun téléphone n'a jamais été connecté.</summary>
    [ObservableProperty]
    private bool _showWelcome;

    public bool HasSessions => OpenSessions.Count > 0;

    public string ProfileSummary => SelectedProfile?.Summary ?? "Aucun profil";

    public bool CanLaunch => SelectedProfile is { SessionCount: > 0 } && !IsBusy;

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(RefreshCoreAsync, cancellationToken);

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) =>
        RunAsync(RefreshCoreAsync, cancellationToken);

    [RelayCommand]
    private Task LaunchAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var report = await _orchestrator.LaunchAsync(profile, token).ConfigureAwait(true);
        await _profiles.TouchAsync(profile.Id, token).ConfigureAwait(true);

        RefreshSessions();

        if (report.Messages.Count > 0)
        {
            StatusMessage = report.AnyStarted
                ? $"{report.Started} session(s) ouverte(s). " + string.Join(" ", report.Messages)
                : string.Join(" ", report.Messages);
        }
    }, cancellationToken);

    [RelayCommand]
    private Task CloseAllAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (OpenSessions.Count > 1
            && !_dialogs.Confirm($"Fermer les {OpenSessions.Count} sessions ouvertes ?"))
        {
            return;
        }

        await _orchestrator.CloseAllAsync(token).ConfigureAwait(true);
        RefreshSessions();
    }, cancellationToken);

    [RelayCommand]
    private Task RecenterAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        var moved = await _orchestrator.RecenterAsync(token).ConfigureAwait(true);

        if (moved == 0)
        {
            StatusMessage = "Aucune fenêtre à recentrer.";
        }
    }, cancellationToken);

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var discovery = await _discovery.RefreshAsync(cancellationToken).ConfigureAwait(true);

        Devices.Clear();
        foreach (var device in discovery.Devices)
        {
            Devices.Add(new DeviceItemViewModel(device));
        }

        ShowWelcome = discovery.Devices.Count == 0;

        if (discovery.Warnings.Count > 0)
        {
            StatusMessage = string.Join(" ", discovery.Warnings);
        }

        var profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(true);
        var previousId = SelectedProfile?.Id;

        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == previousId)
                          ?? Profiles.FirstOrDefault(p => p.Id == settings.DefaultProfileId)
                          ?? await _profiles.GetDefaultAsync(cancellationToken).ConfigureAwait(true)
                          ?? Profiles.FirstOrDefault();

        RefreshSessions();
    }

    private void RefreshSessions()
    {
        OpenSessions.Clear();
        foreach (var session in _orchestrator.ActiveSessions)
        {
            OpenSessions.Add(new SessionItemViewModel(session));
        }

        OnPropertyChanged(nameof(HasSessions));
    }

    partial void OnSelectedProfileChanged(LaunchProfile? value)
    {
        PlannedTargets.Clear();

        foreach (var target in value?.Targets ?? [])
        {
            PlannedTargets.Add(target);
        }

        OnPropertyChanged(nameof(ProfileSummary));
        OnPropertyChanged(nameof(CanLaunch));
        LaunchCommand.NotifyCanExecuteChanged();
    }

    protected override void OnBusyChanged(bool isBusy)
    {
        OnPropertyChanged(nameof(CanLaunch));
        LaunchCommand.NotifyCanExecuteChanged();
    }
}
