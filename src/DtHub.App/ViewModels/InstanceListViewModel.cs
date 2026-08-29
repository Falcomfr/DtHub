using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

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

            var devicesById = discovery.Devices.ToDictionary(d => d.Id, StringComparer.Ordinal);

            foreach (var group in instances.GroupBy(i => i.DeviceId, StringComparer.Ordinal))
            {
                var name = devicesById.TryGetValue(group.Key, out var device)
                    ? device.DisplayName
                    : group.First().DeviceName;

                var view = Devices.FirstOrDefault(d => d.DeviceId == group.Key);
                if (view is null)
                {
                    view = new DeviceGroupViewModel(group.Key, name);
                    Devices.Add(view);
                }

                if (device is not null)
                {
                    view.Update(device);
                }
                else
                {
                    view.MarkOffline();
                }

                SyncInstances(view, [.. group]);
            }

            // Un téléphone dont plus aucune instance n'est connue disparaît.
            foreach (var stale in Devices
                .Where(d => !instances.Any(i => i.DeviceId == d.DeviceId))
                .ToList())
            {
                Devices.Remove(stale);
            }

            // Un téléphone branché mais sans le jeu installé mérite d'être vu :
            // sinon l'utilisateur ne comprend pas pourquoi rien n'apparaît.
            foreach (var device in discovery.Devices.Where(
                d => d.IsConnected && !Devices.Any(g => g.DeviceId == d.Id)))
            {
                var view = new DeviceGroupViewModel(device.Id, device.DisplayName);
                view.Update(device);
                Devices.Add(view);
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
