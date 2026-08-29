using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// Fenêtre de mise en route, montrée au tout premier lancement. Elle ne
/// demande qu'une chose : quelles instances lancer. Une fois répondu, la
/// question n'est plus jamais posée.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;

    public SetupViewModel(
        InstanceListViewModel instances,
        PairingViewModel pairing,
        SettingsService settings,
        GameLauncher launcher)
    {
        Instances = instances;
        Pairing = pairing;
        _settings = settings;
        _launcher = launcher;

        Pairing.Paired += async (_, _) => await Instances.RefreshAsync().ConfigureAwait(true);
    }

    public InstanceListViewModel Instances { get; }

    public PairingViewModel Pairing { get; }

    /// <summary>Vrai quand l'utilisateur a validé et que le lancement peut suivre.</summary>
    [ObservableProperty]
    private bool _isConfirmed;

    /// <summary>Demande la fermeture de la fenêtre.</summary>
    public event EventHandler<bool>? CloseRequested;

    public bool CanLaunch => Instances.EnabledCount > 0;

    /// <summary>Balayage périodique : un téléphone branché apparaît tout seul.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);

        if (Instances.HasNoConnectedDevice)
        {
            await Pairing.ScanAsync(cancellationToken).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(CanLaunch));
    }

    [RelayCommand]
    private async Task LaunchAsync(CancellationToken cancellationToken)
    {
        if (!CanLaunch)
        {
            return;
        }

        await _settings.UpdateAsync(s => s.SetupCompleted = true, cancellationToken).ConfigureAwait(true);

        IsConfirmed = true;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    /// <summary>Rafraîchit le compteur après une case cochée.</summary>
    public void NotifySelectionChanged() => OnPropertyChanged(nameof(CanLaunch));

    /// <summary>Instances retenues, pour le lancement qui suit.</summary>
    public Task<IReadOnlyList<Core.Dofus.DofusInstance>> GetEnabledAsync() =>
        _launcher.RefreshInstancesAsync();
}
