using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Settings;

namespace DtHub.App.ViewModels;

/// <summary>
/// Liste des téléphones et de leurs instances, partagée par la fenêtre de mise
/// en route et par l'onglet Appareils du configurateur. Elle se met à jour
/// toute seule : brancher un téléphone suffit à le voir apparaître.
/// </summary>
public sealed partial class InstanceListViewModel : ObservableObject
{
    private readonly GameLauncher _launcher;
    private readonly SettingsService _settings;

    public InstanceListViewModel(GameLauncher launcher, SettingsService settings)
    {
        _launcher = launcher;
        _settings = settings;
    }

    /// <summary>Téléphones connus, chacun avec ses instances.</summary>
    public ObservableCollection<DeviceGroupViewModel> Devices { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _problem;

    /// <summary>Vrai tant qu'aucun téléphone n'est joignable.</summary>
    public bool HasNoConnectedDevice => !Devices.Any(d => d.IsConnected);

    /// <summary>Vrai si au moins un téléphone répond.</summary>
    public bool HasConnectedDevice => !HasNoConnectedDevice;

    /// <summary>Nombre d'instances cochées pour le lancement.</summary>
    public int EnabledCount => Devices.SelectMany(d => d.Instances).Count(i => i.IsEnabled);

    /// <summary>Balaye les téléphones et reconstruit la liste.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var discovery = await _launcher.RefreshDevicesAsync(cancellationToken).ConfigureAwait(true);
            var instances = await _launcher.RefreshInstancesAsync(cancellationToken).ConfigureAwait(true);

            Problem = discovery.Warnings.Count > 0 ? string.Join(" ", discovery.Warnings) : null;

            // Seuls les téléphones joignables sont montrés. Afficher un
            // appareil absent avec ses instances grisées n'apprend rien et
            // laisse croire à une panne.
            var connected = discovery.Devices
                .Where(d => d.IsConnected)
                .ToDictionary(d => d.Id, StringComparer.Ordinal);

            foreach (var (id, device) in connected)
            {
                var view = Devices.FirstOrDefault(d => d.DeviceId == id);
                if (view is null)
                {
                    view = new DeviceGroupViewModel(id, device.DisplayName);
                    Devices.Add(view);
                }

                view.Update(device);

                SyncInstances(view, [.. instances.Where(i => i.DeviceId == id)]);
            }

            foreach (var stale in Devices.Where(d => !connected.ContainsKey(d.DeviceId)).ToList())
            {
                Devices.Remove(stale);
            }

            OnPropertyChanged(nameof(HasNoConnectedDevice));
            OnPropertyChanged(nameof(HasConnectedDevice));
            OnPropertyChanged(nameof(EnabledCount));
        }
        catch (AdbException exception)
        {
            Problem = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Rafraîchit uniquement l'état ouvert ou fermé de chaque instance.</summary>
    public void RefreshRunningState()
    {
        foreach (var row in Devices.SelectMany(d => d.Instances))
        {
            row.IsRunning = _launcher.IsOpen(row.Instance);
        }
    }

    /// <summary>Ouvre une instance qui ne l'est pas encore.</summary>
    [RelayCommand]
    private Task LaunchInstanceAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.LaunchAsync([instance]));

    /// <summary>Ferme le jeu sur l'appareil puis le rouvre.</summary>
    [RelayCommand]
    private Task RestartAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.RestartAsync(instance));

    /// <summary>Ferme la fenêtre d'une instance.</summary>
    [RelayCommand]
    private Task StopAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, async instance =>
        {
            await _launcher.StopAsync(instance).ConfigureAwait(true);
            return new LaunchReport(0, []);
        });

    /// <summary>
    /// Exécute une action sur une instance et en répercute le résultat sur la
    /// liste. Le garde-fou est le même pour les trois boutons : ils parlent à
    /// l'appareil, et deux actions concurrentes laisseraient l'état affiché en
    /// désaccord avec les fenêtres réellement ouvertes.
    /// </summary>
    private async Task ActOnAsync(
        InstanceRowViewModel? row,
        Func<Core.Dofus.DofusInstance, Task<LaunchReport>> action)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var report = await action(row.Instance).ConfigureAwait(true);

            Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
        }
        catch (AdbException exception)
        {
            Problem = exception.UserMessage;
        }
        finally
        {
            IsBusy = false;

            // L'état est relu plutôt que déduit de l'action : une session peut
            // s'être arrêtée d'elle-même entre-temps.
            RefreshRunningState();
        }
    }

    private void SyncInstances(DeviceGroupViewModel group, IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        foreach (var instance in instances)
        {
            var row = group.Instances.FirstOrDefault(r => r.Key == instance.Key);

            if (row is null)
            {
                row = new InstanceRowViewModel(instance) { IsRunning = _launcher.IsOpen(instance) };
                row.EnabledChanged += OnEnabledChanged;
                row.NameChanged += OnNameChanged;
                group.Instances.Add(row);
                continue;
            }

            row.Update(instance, _launcher.IsOpen(instance));
        }

        foreach (var stale in group.Instances
            .Where(r => !instances.Any(i => i.Key == r.Key))
            .ToList())
        {
            group.Instances.Remove(stale);
        }
    }

    private async void OnEnabledChanged(object? sender, InstanceRowViewModel row)
    {
        await _settings.SetInstanceEnabledAsync(row.Key, row.IsEnabled).ConfigureAwait(true);
        OnPropertyChanged(nameof(EnabledCount));
    }

    private async void OnNameChanged(object? sender, InstanceRowViewModel row) =>
        await _settings.RenameInstanceAsync(row.Key, row.Name).ConfigureAwait(true);
}
