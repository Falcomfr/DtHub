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

    /// <summary>
    /// Vrai pendant un glisser-déposer. Le balayage périodique s'abstient
    /// alors de reconstruire la liste, faute de quoi une carte disparaîtrait
    /// sous le curseur.
    /// </summary>
    public bool IsReordering { get; set; }

    /// <summary>Vrai tant qu'aucun téléphone n'est joignable.</summary>
    public bool HasNoConnectedDevice => !Devices.Any(d => d.IsConnected);

    /// <summary>Vrai si au moins un téléphone répond.</summary>
    public bool HasConnectedDevice => !HasNoConnectedDevice;

    /// <summary>Nombre d'instances cochées pour le lancement.</summary>
    public int EnabledCount => Devices.SelectMany(d => d.Instances).Count(i => i.IsEnabled);

    /// <summary>Balaye les téléphones et reconstruit la liste.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || IsReordering)
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

            // L'ordre vient de la liste fusionnée, qui porte celui choisi par
            // l'utilisateur. Parcourir le dictionnaire rendrait l'ordre
            // indéterminé, et le réordonnancement invisible.
            var ordered = instances
                .Select(i => i.DeviceId)
                .Where(connected.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            ordered.AddRange(connected.Keys.Where(id => !ordered.Contains(id, StringComparer.Ordinal)));

            for (var position = 0; position < ordered.Count; position++)
            {
                var id = ordered[position];
                var device = connected[id];

                var view = Devices.FirstOrDefault(d => d.DeviceId == id);
                if (view is null)
                {
                    view = new DeviceGroupViewModel(id, device.DisplayName);
                    Devices.Add(view);
                }

                var current = Devices.IndexOf(view);
                if (current != position && position < Devices.Count)
                {
                    // Déplacer plutôt que vider et reconstruire : un Clear
                    // casserait un glisser-déposer en cours.
                    Devices.Move(current, position);
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

    /// <summary>Efface tous les repères de dépôt.</summary>
    public void ClearDropHints()
    {
        foreach (var device in Devices)
        {
            device.IsDragging = false;
            device.DropAbove = false;
            device.DropBelow = false;

            foreach (var row in device.Instances)
            {
                row.IsDragging = false;
                row.DropAbove = false;
                row.DropBelow = false;
            }
        }
    }

    /// <summary>
    /// Marque l'endroit où le dépôt insérerait, au-dessus ou en dessous de
    /// l'élément survolé. Un seul repère est visible à la fois.
    /// </summary>
    public void ShowDropHint(object onto, bool above)
    {
        ClearDropHints();

        switch (onto)
        {
            case InstanceRowViewModel row:
                row.DropAbove = above;
                row.DropBelow = !above;
                break;

            case DeviceGroupViewModel group:
                group.DropAbove = above;
                group.DropBelow = !above;
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Dépose un élément sur un autre. L'écart entre les deux positions donne
    /// le déplacement, ce qui couvre aussi bien le voisin immédiat qu'un saut
    /// de plusieurs rangs.
    /// </summary>
    public Task ReorderAsync(object dragged, object onto, bool above) => (dragged, onto) switch
    {
        (InstanceRowViewModel source, InstanceRowViewModel target) =>
            MoveInstanceAsync(source, Offset(IndexOf(source), IndexOf(target), above)),

        (DeviceGroupViewModel source, DeviceGroupViewModel target) =>
            MoveDeviceAsync(source, Offset(Devices.IndexOf(source), Devices.IndexOf(target), above)),

        _ => Task.CompletedTask,
    };

    /// <summary>
    /// Déplacement à appliquer pour insérer juste avant ou juste après la
    /// cible. Retirer l'élément de sa place décale d'un rang tout ce qui le
    /// suivait, d'où la correction.
    /// </summary>
    private static int Offset(int from, int onto, bool above)
    {
        if (from < 0 || onto < 0)
        {
            return 0;
        }

        var destination = above ? onto : onto + 1;

        if (from < destination)
        {
            destination--;
        }

        return destination - from;
    }

    /// <summary>Position d'une instance dans son appareil, ou -1.</summary>
    private int IndexOf(InstanceRowViewModel row) =>
        Devices.FirstOrDefault(d => d.Instances.Contains(row))?.Instances.IndexOf(row) ?? -1;

    private async Task MoveInstanceAsync(InstanceRowViewModel? row, int offset)
    {
        if (row is null)
        {
            return;
        }

        if (await _settings.MoveInstanceAsync(row.Key, offset).ConfigureAwait(true))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    private async Task MoveDeviceAsync(DeviceGroupViewModel? group, int offset)
    {
        if (group is null)
        {
            return;
        }

        if (await _settings.MoveDeviceAsync(group.DeviceId, offset).ConfigureAwait(true))
        {
            await RefreshAsync().ConfigureAwait(true);
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
        row.IsWorking = true;

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
            row.IsWorking = false;
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

        // Les lignes déjà présentes ne bougeaient pas : l'ordre enregistré ne
        // se voyait donc qu'au prochain démarrage.
        for (var position = 0; position < instances.Count; position++)
        {
            var row = group.Instances.FirstOrDefault(
                r => string.Equals(r.Key, instances[position].Key, StringComparison.Ordinal));

            if (row is not null && group.Instances.IndexOf(row) is var current
                && current != position && position < group.Instances.Count)
            {
                group.Instances.Move(current, position);
            }
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
