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
    private readonly IDialogService _dialogs;

    public InstanceListViewModel(GameLauncher launcher, SettingsService settings, IDialogService dialogs)
    {
        _launcher = launcher;
        _settings = settings;
        _dialogs = dialogs;

        // Fermer une fenêtre de jeu doit se voir tout de suite. Attendre le
        // balayage laissait jusqu'à trois secondes pendant lesquelles la liste
        // annonçait une fenêtre qui n'existait plus.
        _launcher.SessionChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, Core.Scrcpy.ScrcpySession session)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(RefreshRunningState);
    }

    /// <summary>
    /// Toutes les instances des appareils joignables, dans l'ordre voulu.
    ///
    /// Une seule liste, sans distinction d'appareil : le nom du téléphone
    /// n'apparaît qu'aux endroits où il change.
    /// </summary>
    public ObservableCollection<InstanceRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Appareils connus qui n'ont aucune ligne dans la liste : hors ligne, ou
    /// joignables mais sans le jeu. Sans ce rappel, brancher un téléphone où
    /// le jeu manque ne produirait rien du tout à l'écran.
    /// </summary>
    public ObservableCollection<DeviceGroupViewModel> InactiveDevices { get; } = [];

    /// <summary>Appareils vus, par identifiant. Chacun est partagé par ses lignes.</summary>
    private readonly Dictionary<string, DeviceGroupViewModel> _devices = new(StringComparer.Ordinal);

    /// <summary>Dernière découverte d'instances, réutilisée entre deux balayages.</summary>
    private IReadOnlyList<Core.Dofus.DofusInstance>? _instances;

    /// <summary>Empreinte des appareils vus, pour savoir quand redécouvrir.</summary>
    private string? _signature;

    private DateTimeOffset _discoveredAt;

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

    /// <summary>Rythme du balayage des appareils, selon la qualité choisie.</summary>
    public TimeSpan PollInterval => _launcher.Quality.DevicePoll;

    /// <summary>Vrai tant qu'aucun téléphone n'est joignable.</summary>
    public bool HasNoConnectedDevice => !_devices.Values.Any(d => d.IsConnected);

    /// <summary>Vrai si au moins un téléphone répond.</summary>
    public bool HasConnectedDevice => !HasNoConnectedDevice;

    /// <summary>Vrai s'il y a plus d'une instance à ordonner.</summary>
    public bool CanReorder => Rows.Count > 1;

    /// <summary>Vrai s'il y a au moins une instance à montrer.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Vrai s'il y a au moins un appareil sans instance à signaler.</summary>
    public bool HasInactiveDevices => InactiveDevices.Count > 0;

    /// <summary>Nombre d'instances cochées pour le lancement.</summary>
    public int EnabledCount => Rows.Count(i => i.IsEnabled);

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

            // Lister les appareils est bon marché ; redécouvrir les instances
            // ne l'est pas, chaque profil de chaque appareil demandant deux
            // commandes au téléphone. On ne le refait donc que si l'ensemble
            // des appareils a changé, ou après un long moment.
            var signature = string.Join(
                "|",
                discovery.Devices.Select(d => $"{d.Id}:{d.State}").Order(StringComparer.Ordinal));

            if (_instances is null
                || !string.Equals(signature, _signature, StringComparison.Ordinal)
                || DateTimeOffset.UtcNow - _discoveredAt >= _launcher.Quality.InstanceRediscovery)
            {
                _instances = await _launcher.RefreshInstancesAsync(cancellationToken).ConfigureAwait(true);
                _signature = signature;
                _discoveredAt = DateTimeOffset.UtcNow;
            }

            var instances = _instances;

            Problem = discovery.Warnings.Count > 0 ? string.Join(" ", discovery.Warnings) : null;

            // Seules les instances des téléphones joignables ont une ligne.
            // Les autres appareils ne disparaissent pas pour autant : ils
            // sont rappelés à part, avec la raison.
            var connected = discovery.Devices
                .Where(d => d.IsConnected)
                .ToDictionary(d => d.Id, StringComparer.Ordinal);

            foreach (var device in discovery.Devices)
            {
                if (!_devices.TryGetValue(device.Id, out var view))
                {
                    view = new DeviceGroupViewModel(device.Id, device.DisplayName);
                    _devices[device.Id] = view;
                }

                view.Update(device);
            }

            foreach (var gone in _devices.Keys
                .Where(id => !discovery.Devices.Any(d => string.Equals(d.Id, id, StringComparison.Ordinal)))
                .ToList())
            {
                _devices.Remove(gone);
            }

            SyncRows([.. instances.Where(i => connected.ContainsKey(i.DeviceId))]);
            SyncInactiveDevices(discovery.Devices, instances);
            RefreshDeviceHeaders();

            OnPropertyChanged(nameof(CanReorder));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasInactiveDevices));
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

    private InstanceRowViewModel? _hinted;
    private bool _hintedAbove;

    /// <summary>Efface tous les repères de dépôt.</summary>
    public void ClearDropHints()
    {
        _hinted = null;

        foreach (var row in Rows)
        {
            row.IsDragging = false;
            row.DropAbove = false;
            row.DropBelow = false;
        }
    }

    /// <summary>
    /// Marque l'endroit où le dépôt insérerait, au-dessus ou en dessous de la
    /// ligne survolée. Un seul repère est visible à la fois.
    ///
    /// Rien n'est touché quand le repère n'a pas changé de place : le survol
    /// déclenche des dizaines d'événements par seconde, et tout remettre à
    /// zéro à chacun faisait clignoter le trait.
    /// </summary>
    public void ShowDropHint(InstanceRowViewModel onto, bool above)
    {
        ArgumentNullException.ThrowIfNull(onto);

        if (ReferenceEquals(_hinted, onto) && _hintedAbove == above)
        {
            return;
        }

        ClearDropHints();

        _hinted = onto;
        _hintedAbove = above;

        onto.DropAbove = above;
        onto.DropBelow = !above;
    }

    /// <summary>Dépose une instance juste avant ou juste après une autre.</summary>
    public async Task ReorderAsync(InstanceRowViewModel dragged, InstanceRowViewModel onto, bool above)
    {
        ArgumentNullException.ThrowIfNull(dragged);
        ArgumentNullException.ThrowIfNull(onto);

        if (ReferenceEquals(dragged, onto))
        {
            return;
        }

        if (await _settings.MoveInstanceAsync(dragged.Key, onto.Key, above).ConfigureAwait(true))
        {
            await RefreshAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Rafraîchit uniquement l'état ouvert ou fermé de chaque instance.</summary>
    public void RefreshRunningState()
    {
        foreach (var row in Rows)
        {
            row.IsRunning = _launcher.IsOpen(row.Instance);
        }
    }

    /// <summary>Ouvre une instance qui ne l'est pas encore.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LaunchInstanceAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.LaunchAsync([instance]));

    /// <summary>Ferme le jeu sur l'appareil puis le rouvre.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RestartAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.RestartAsync(instance));

    /// <summary>Ferme la fenêtre d'une instance.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
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
        // Le garde-fou est propre à la ligne, et non à la liste entière. Un
        // verrou global avalait le clic quand une autre instance travaillait,
        // ou simplement pendant le balayage périodique : il fallait alors
        // cliquer une seconde fois.
        if (row is null || row.IsWorking)
        {
            return;
        }

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

            // L'état est relu plutôt que déduit de l'action : une session peut
            // s'être arrêtée d'elle-même entre-temps.
            RefreshRunningState();

            // Une action a pu changer ce que porte l'appareil : le prochain
            // balayage redécouvre plutôt que de reprendre le cache.
            _instances = null;
        }
    }

    private void SyncRows(IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        foreach (var instance in instances)
        {
            var row = Rows.FirstOrDefault(r => r.Key == instance.Key);

            if (row is null)
            {
                row = new InstanceRowViewModel(instance) { IsRunning = _launcher.IsOpen(instance) };
                row.EnabledChanged += OnEnabledChanged;
                row.ManagedChanged += OnManagedChanged;
                row.NameChanged += OnNameChanged;
                Rows.Add(row);
            }
            else
            {
                row.Update(instance, _launcher.IsOpen(instance));
            }

            row.Device = _devices.GetValueOrDefault(instance.DeviceId);
        }

        // Les lignes déjà présentes ne bougeaient pas : l'ordre enregistré ne
        // se voyait donc qu'au prochain démarrage. Déplacer plutôt que vider :
        // un Clear casserait un glisser-déposer en cours.
        for (var position = 0; position < instances.Count; position++)
        {
            var row = Rows.FirstOrDefault(
                r => string.Equals(r.Key, instances[position].Key, StringComparison.Ordinal));

            if (row is not null && Rows.IndexOf(row) is var current
                && current != position && position < Rows.Count)
            {
                Rows.Move(current, position);
            }
        }

        foreach (var stale in Rows.Where(r => !instances.Any(i => i.Key == r.Key)).ToList())
        {
            Rows.Remove(stale);
        }
    }

    /// <summary>
    /// Rappelle les appareils qui n'ont aucune ligne, avec la raison : hors
    /// ligne, ou joignable mais sans le jeu.
    /// </summary>
    private void SyncInactiveDevices(
        IReadOnlyList<Core.Devices.AndroidDevice> devices,
        IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        var withGame = instances
            .Select(i => i.DeviceId)
            .ToHashSet(StringComparer.Ordinal);

        var inactive = devices
            .Where(d => !d.IsConnected || !withGame.Contains(d.Id))
            .Select(d => d.Id)
            .ToList();

        foreach (var id in inactive)
        {
            var view = _devices[id];
            view.HasNoGame = !withGame.Contains(id);

            if (!InactiveDevices.Contains(view))
            {
                InactiveDevices.Add(view);
            }
        }

        foreach (var stale in InactiveDevices
            .Where(d => !inactive.Contains(d.DeviceId, StringComparer.Ordinal))
            .ToList())
        {
            InactiveDevices.Remove(stale);
        }
    }

    /// <summary>
    /// Pose le nom d'appareil là où l'appareil change, et ce qui ne vaut qu'une
    /// fois par téléphone sur son premier morceau.
    /// </summary>
    private void RefreshDeviceHeaders()
    {
        var ids = Rows.Select(r => r.DeviceId).ToList();

        var headers = DeviceHeaders.For(ids);
        var firsts = DeviceHeaders.FirstOccurrences(ids);

        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].ShowDeviceHeader = headers[i];
            Rows[i].IsFirstOfDevice = firsts[i];
        }
    }

    /// <summary>
    /// Rompt l'association d'un appareil. Ses fenêtres se ferment, ses
    /// instances et leurs réglages sont effacés : il faudra l'associer de
    /// nouveau pour s'en resservir.
    /// </summary>
    [RelayCommand]
    private async Task ForgetDeviceAsync(DeviceGroupViewModel? device)
    {
        if (device is null
            || !_dialogs.Confirm(
                $"Rompre l'association avec {device.Name} ?\n\nSes instances et leurs réglages seront effacés.",
                "Rompre l'association"))
        {
            return;
        }

        await _launcher.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);
        await _settings.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);
        await RefreshAsync().ConfigureAwait(true);
    }

    private async void OnEnabledChanged(object? sender, InstanceRowViewModel row)
    {
        await _settings.SetInstanceEnabledAsync(row.Key, row.IsEnabled).ConfigureAwait(true);
        OnPropertyChanged(nameof(EnabledCount));
    }

    private async void OnManagedChanged(object? sender, InstanceRowViewModel row)
    {
        await _settings.SetInstanceManagedAsync(row.Key, row.IsManaged).ConfigureAwait(true);

        // Le lanceur relit la liste des mises de côté au prochain placement.
        await _launcher.RefreshRanksAsync().ConfigureAwait(true);
    }

    private async void OnNameChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _settings.RenameInstanceAsync(row.Key, row.Name).ConfigureAwait(true);
        }
        finally
        {
            // Le nom est écrit : le balayage peut de nouveau faire foi.
            row.IsRenaming = false;
        }
    }
}
