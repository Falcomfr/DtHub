using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Profiles;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// Page Profils : créer, renommer, dupliquer, supprimer, et régler l'ordre des
/// sessions d'un profil.
/// </summary>
public sealed partial class ProfilesViewModel : PageViewModel
{
    private readonly ProfileService _profiles;
    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;

    public ProfilesViewModel(ProfileService profiles, SettingsService settings, IDialogService dialogs)
    {
        _profiles = profiles;
        _settings = settings;
        _dialogs = dialogs;
    }

    public override string Title => "Profils";

    public override string Subtitle =>
        "Un profil est une liste de sessions à ouvrir ensemble. L'ordre détermine l'empilement des fenêtres.";

    public ObservableCollection<LaunchProfile> Profiles { get; } = [];

    public ObservableCollection<LaunchTarget> Targets { get; } = [];

    [ObservableProperty]
    private LaunchProfile? _selectedProfile;

    [ObservableProperty]
    private LaunchTarget? _selectedTarget;

    [ObservableProperty]
    private string? _defaultProfileId;

    public bool HasProfile => SelectedProfile is not null;

    public bool IsSelectedProfileDefault =>
        SelectedProfile is not null && SelectedProfile.Id == DefaultProfileId;

    /// <summary>Demande de saisie d'un nom, branchée par la vue.</summary>
    public Func<string, string?>? NameRequested { get; set; }

    public override Task OnActivatedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(ReloadAsync, cancellationToken);

    [RelayCommand]
    private Task CreateAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        var name = NameRequested?.Invoke("Nouveau profil");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var created = await _profiles.CreateAsync(name, null, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == created.Id);
    }, cancellationToken);

    [RelayCommand]
    private Task RenameAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var name = NameRequested?.Invoke(profile.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await _profiles.RenameAsync(profile.Id, name, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
    }, cancellationToken);

    [RelayCommand]
    private Task DuplicateAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        var copy = await _profiles.DuplicateAsync(profile.Id, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == copy?.Id);
    }, cancellationToken);

    [RelayCommand]
    private Task DeleteAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile
            || !_dialogs.Confirm($"Supprimer le profil « {profile.Name} » ?", "Supprimer le profil"))
        {
            return;
        }

        await _profiles.DeleteAsync(profile.Id, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);
    }, cancellationToken);

    [RelayCommand]
    private Task SetAsDefaultAsync(CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        await _profiles.SetDefaultAsync(profile.Id, token).ConfigureAwait(true);
        await _settings.UpdateAsync(s => s.DefaultProfileId = profile.Id, token).ConfigureAwait(true);

        DefaultProfileId = profile.Id;
        OnPropertyChanged(nameof(IsSelectedProfileDefault));
    }, cancellationToken);

    [RelayCommand]
    private Task RemoveTargetAsync(LaunchTarget? target) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile || target is null)
        {
            return;
        }

        await _profiles.RemoveTargetAsync(profile.Id, target.Key, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
    });

    [RelayCommand]
    private Task MoveTargetUpAsync(LaunchTarget? target) => MoveTargetAsync(target, -1);

    [RelayCommand]
    private Task MoveTargetDownAsync(LaunchTarget? target) => MoveTargetAsync(target, 1);

    private Task MoveTargetAsync(LaunchTarget? target, int offset) => RunAsync(async token =>
    {
        if (SelectedProfile is not { } profile || target is null)
        {
            return;
        }

        var index = Targets.IndexOf(target);
        if (index < 0)
        {
            return;
        }

        await _profiles.MoveTargetAsync(profile.Id, target.Key, index + offset, token).ConfigureAwait(true);
        await ReloadAsync(token).ConfigureAwait(true);

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
        SelectedTarget = Targets.FirstOrDefault(t => t.Key == target.Key);
    });

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var previousId = SelectedProfile?.Id;
        var profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(true);

        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        DefaultProfileId = settings.DefaultProfileId;

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == previousId) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(LaunchProfile? value)
    {
        Targets.Clear();
        foreach (var target in value?.Targets ?? [])
        {
            Targets.Add(target);
        }

        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(IsSelectedProfileDefault));
    }
}
