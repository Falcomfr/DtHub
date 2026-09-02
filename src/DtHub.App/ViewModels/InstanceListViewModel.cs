using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Adb;
using DtHub.Core.Android;
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

    public InstanceListViewModel(
        GameLauncher launcher,
        SettingsService settings,
        IDialogService dialogs,
        IAppIconProvider icons)
    {
        _launcher = launcher;
        _settings = settings;
        _dialogs = dialogs;
        _icons = icons;

        // Fermer une fenêtre de jeu doit se voir tout de suite. Attendre le
        // balayage laissait jusqu'à trois secondes pendant lesquelles la liste
        // annonçait une fenêtre qui n'existait plus.
        _launcher.SessionChanged += OnSessionChanged;
        _launcher.DeviceBusyChanged += OnDeviceBusyChanged;
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

    // Profils de lancement

    /// <summary>Les profils enregistrés, tels qu'ils paraissent.</summary>
    public ObservableCollection<LaunchProfileRowViewModel> Profiles { get; } = [];

    /// <summary>
    /// Le profil choisi dans la liste. Le choisir l'ouvre pour de bon.
    ///
    /// Le garde-fou est indispensable : la liste se reconstruit à chaque
    /// balayage, et la sélection qu'on y repose déclencherait sinon une
    /// ouverture toutes les trois secondes.
    /// </summary>
    [ObservableProperty]
    private LaunchProfileRowViewModel? _selectedProfile;

    private bool _syncingProfiles;

    /// <summary>Vrai s'il y a au moins un profil à proposer.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Vrai si un profil est choisi, donc supprimable.</summary>
    public bool HasSelectedProfile => SelectedProfile is not null;

    /// <summary>
    /// Ce que dit le champ tant que rien n'y est choisi.
    ///
    /// Un champ vide ne distingue pas « aucune session enregistrée » de
    /// « des sessions attendent d'être choisies », et c'est justement la
    /// question qu'on se pose en le voyant.
    /// </summary>
    public string ProfilePrompt => HasProfiles
        ? "Choisir un profil"
        : "Aucun profil enregistré";

    /// <summary>
    /// Reprend les profils enregistrés, en gardant la sélection courante.
    ///
    /// Reconstruire la liste repose la sélection, ce qui rejouerait l'ouverture
    /// à chaque balayage. D'où le garde-fou, sur le modèle de celui qui protège
    /// les cases de la liste.
    /// </summary>
    private async Task SyncProfilesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        var stored = settings.LaunchProfiles;
        var byDefault = LaunchProfiles.Normalize(settings.DefaultLaunchProfile);
        var chosen = SelectedProfile?.Name;

        _syncingProfiles = true;

        try
        {
            Profiles.Clear();

            foreach (var profile in stored)
            {
                Profiles.Add(new LaunchProfileRowViewModel(
                    profile.Name,
                    LaunchProfiles.Describe(profile, settings.Instances),
                    string.Equals(profile.Name, byDefault, StringComparison.OrdinalIgnoreCase)));
            }

            SelectedProfile = Profiles.FirstOrDefault(
                p => string.Equals(p.Name, chosen, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingProfiles = false;
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasSelectedProfile));
        OnPropertyChanged(nameof(ProfilePrompt));
    }

    partial void OnSelectedProfileChanged(LaunchProfileRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedProfile));

        if (_syncingProfiles || value is null)
        {
            return;
        }

        _ = OpenProfileAsync(value);
    }

    /// <summary>
    /// Ouvre un profil : ce qui n'en fait pas partie se ferme, ce qui y
    /// manque s'ouvre.
    ///
    /// Choisir ouvre pour de bon, faute de cases à cocher dans le
    /// configurateur : si la sélection se contentait d'écrire l'ensemble de
    /// démarrage, rien à l'écran ne montrerait qu'il s'est passé quelque chose.
    /// </summary>
    private async Task OpenProfileAsync(LaunchProfileRowViewModel profile)
    {
        // Fermer des fenêtres de jeu ne se fait pas sans le dire : on peut être
        // en pleine partie, et un clic dans une liste n'est pas un consentement.
        if (_launcher.ActiveSessions.Count > 0
            && !_dialogs.Confirm(
                $"Ouvrir le profil « {profile.Name} » ?\n\n"
                + "Les fenêtres de jeu qui n'en font pas partie seront fermées.",
                "Changer de profil"))
        {
            await SyncProfilesAsync().ConfigureAwait(true);
            return;
        }

        IsBusy = true;

        try
        {
            // Fermer d'abord, appliquer ensuite : la fermeture commence par
            // relever la géométrie des fenêtres ouvertes, et écraserait donc
            // les positions que le profil vient de poser.
            await _launcher.CloseAllAsync().ConfigureAwait(true);
            await _settings.ApplyLaunchProfileAsync(profile.Name).ConfigureAwait(true);

            var report = await _launcher.LaunchEnabledAsync().ConfigureAwait(true);

            if (report.Problems.Count > 0)
            {
                _dialogs.ShowWarning(string.Join(Environment.NewLine, report.Problems));
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>Retient les comptes ouverts, leurs positions et les réglages, sous un nom.</summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        // La géométrie est relevée avant l'instantané, sans quoi le profil
        // retiendrait les positions de l'ouverture et non celles du moment :
        // déplacer une fenêtre puis enregistrer n'aurait rien retenu.
        await _launcher.CaptureGeometriesAsync().ConfigureAwait(true);

        var open = _launcher.ActiveSessions.Select(s => s.Target.Key).Distinct(StringComparer.Ordinal).ToList();

        // À défaut de fenêtre ouverte, l'ensemble de démarrage fait foi : c'est
        // lui qui rouvrira, et c'est donc lui que l'on enregistre.
        if (open.Count == 0)
        {
            var settings = await _settings.GetAsync().ConfigureAwait(true);
            open = [.. settings.Instances.Where(i => i.IsEnabled).Select(i => i.Key)];
        }

        if (open.Count == 0)
        {
            _dialogs.ShowWarning(
                "Aucun compte n'est ouvert : il n'y a rien à retenir. Ouvrez les comptes "
                + "du profil, puis enregistrez.",
                "Enregistrer le profil");

            return;
        }

        if (_dialogs.PromptText(
                $"Sous quel nom retenir ce profil de {open.Count} compte(s), avec leurs positions et les réglages actuels ?",
                SelectedProfile?.Name,
                "Enregistrer le profil") is not { } typed)
        {
            return;
        }

        if (!await _settings.SaveLaunchProfileAsync(typed, open).ConfigureAwait(true))
        {
            _dialogs.ShowWarning("Un profil a besoin d'un nom.", "Enregistrer le profil");
            return;
        }

        await SyncProfilesAsync().ConfigureAwait(true);

        _syncingProfiles = true;

        try
        {
            SelectedProfile = Profiles.FirstOrDefault(
                p => string.Equals(p.Name, LaunchProfiles.Normalize(typed), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingProfiles = false;
        }
    }

    /// <summary>Désigne le profil du démarrage, ou le retire.</summary>
    [RelayCommand]
    private async Task ToggleDefaultProfileAsync()
    {
        if (SelectedProfile is not { } profile)
        {
            return;
        }

        await _settings
            .SetDefaultLaunchProfileAsync(profile.IsDefault ? null : profile.Name)
            .ConfigureAwait(true);

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Supprime le profil choisi.</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile
            || !_dialogs.Confirm(
                $"Supprimer le profil « {profile.Name} » ?\n\n"
                + "Les comptes ne sont pas touchés, seule la liste disparaît.",
                "Supprimer le profil"))
        {
            return;
        }

        await _settings.DeleteLaunchProfileAsync(profile.Name).ConfigureAwait(true);

        _syncingProfiles = true;

        try
        {
            SelectedProfile = null;
        }
        finally
        {
            _syncingProfiles = false;
        }

        await SyncProfilesAsync().ConfigureAwait(true);
    }

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

            // Les incidents de la découverte d'appareils et ceux du balayage
            // d'instances partagent le même bandeau : un profil illisible est
            // aussi utile à savoir qu'un appareil injoignable.
            var warnings = discovery.Warnings.Concat(_launcher.InstanceWarnings).ToList();

            Problem = warnings.Count > 0 ? string.Join(" ", warnings) : null;

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
            RefreshBusyState();
            SyncInactiveDevices(discovery.Devices, instances);
            RefreshDeviceHeaders();

            OnPropertyChanged(nameof(CanReorder));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasInactiveDevices));
            OnPropertyChanged(nameof(HasNoConnectedDevice));
            OnPropertyChanged(nameof(HasConnectedDevice));
            OnPropertyChanged(nameof(EnabledCount));

            // Les résumés de sessions citent les noms des comptes : ils se
            // refont ici, après que la liste a été reconstruite.
            await SyncProfilesAsync(cancellationToken).ConfigureAwait(true);

            RequestIcons();
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

    private readonly IAppIconProvider _icons;

    /// <summary>
    /// Demande l'icône des lignes qui n'en ont pas encore.
    ///
    /// Ce qui est déjà connu est posé sur-le-champ, sans rien demander au
    /// téléphone : le balayage passe toutes les trois secondes et ne doit pas
    /// s'allonger d'une seule commande. Le reste part en tâche de fond, que
    /// personne n'attend, ce que seule autorise la promesse du fournisseur de
    /// ne jamais lever.
    /// </summary>
    private void RequestIcons()
    {
        foreach (var row in Rows)
        {
            if (row.IconPath is not null)
            {
                continue;
            }

            if (_icons.Find(row.DeviceId, row.Instance.PackageName) is { } known)
            {
                row.IconPath = known;

                continue;
            }

            if (_devices.TryGetValue(row.DeviceId, out var device) && device.Serial.Length > 0)
            {
                _ = FillIconAsync(row, device.Serial);
            }
        }
    }

    private async Task FillIconAsync(InstanceRowViewModel row, string serial)
    {
        var path = await _icons.GetAsync(
            new AppIconRequest(
                row.DeviceId,
                serial,
                row.Instance.UserId,
                row.Instance.PackageName)).ConfigureAwait(true);

        if (path is not null)
        {
            row.IconPath = path;
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
            // Le cache de découverte porte l'ancien ordre : le garder ferait
            // revenir la ligne à sa place sous le curseur. Toute écriture dans
            // les réglages doit l'invalider.
            _instances = null;

            // L'ordre de la liste commande l'ordre des fenêtres : sans cela,
            // déplacer une ligne ne changeait que le parcours au clavier.
            await _launcher.RefreshRanksAsync().ConfigureAwait(true);

            await RefreshAsync().ConfigureAwait(true);

            // Les fenêtres ouvertes sont rouvertes dans le nouvel ordre.
            //
            // Remonter la pile suffisait pour Alt+Tab, mais pas pour les
            // vignettes de la barre des tâches : Windows les range dans
            // l'ordre de création et n'expose rien pour le changer. Les
            // recréer est le seul moyen, et c'est ce que l'utilisateur a
            // demandé en connaissance de cause.
            await _launcher.ReopenAsync().ConfigureAwait(true);
            await _launcher.ApplyWindowOrderAsync().ConfigureAwait(true);
        }
    }

    private void OnDeviceBusyChanged(object? sender, Core.Scrcpy.DeviceBusyChangedEventArgs args)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(RefreshBusyState);
    }

    /// <summary>
    /// Rallume l'indicateur des lignes dont le téléphone est occupé.
    ///
    /// L'état est relu du lanceur à chaque passage, jamais mémorisé ici : la
    /// liste se reconstruit toutes les quelques secondes, et une ligne neuve
    /// doit naître dans le bon état.
    /// </summary>
    public void RefreshBusyState()
    {
        foreach (var row in Rows)
        {
            row.IsDeviceBusy = _launcher.IsDeviceBusy(row.DeviceId);
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
            RefreshBusyState();

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
    /// Ajoute un compte sur ce téléphone.
    ///
    /// Un profil Android neuf, avec le jeu dedans. C'est le mécanisme des
    /// comptes multiples d'Android, celui que la surcouche du téléphone emploie
    /// elle-même : rien n'est recopié, l'application reste celle de l'éditeur.
    ///
    /// Le nom est posé d'office et se change ensuite comme celui des autres
    /// instances : demander un nom avant même de savoir si le téléphone
    /// acceptera ferait taper pour rien.
    ///
    /// Une confirmation est demandée, parce que cela touche le téléphone et
    /// que le profil naît vide : le jeu y redemandera ses ressources et la
    /// connexion, ce qui n'est pas ce qu'on attend d'un clic sur un plus.
    /// </summary>
    [RelayCommand]
    private async Task AddAccountAsync(DeviceGroupViewModel? device)
    {
        if (device is null)
        {
            return;
        }

        var name = NextAccountName(device.DeviceId);

        if (!_dialogs.Confirm(
                $"Ajouter le compte « {name} » sur {device.Name} ?\n\n"
                + "Un profil Android neuf sera créé sur le téléphone, avec le jeu dedans. "
                + "Il s'ouvrira comme une installation neuve : le jeu redemandera ses "
                + "ressources et votre connexion.",
                "Ajouter un compte"))
        {
            return;
        }

        device.IsBusy = true;

        try
        {
            var result = await _launcher
                .AddAccountAsync(device.DeviceId, name)
                .ConfigureAwait(true);

            if (result.Succeeded)
            {
                _dialogs.ShowInformation(result.Message, "Ajouter un compte");
            }
            else
            {
                _dialogs.ShowWarning(result.Message, "Ajouter un compte");
            }
        }
        finally
        {
            device.IsBusy = false;
        }

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Un nom libre pour le prochain compte de ce téléphone. Le numéro suit ce
    /// qui existe déjà, sans jamais retomber sur un nom pris : deux profils du
    /// même nom seraient indiscernables dans la liste comme sur le téléphone.
    /// </summary>
    private string NextAccountName(string deviceId)
    {
        var taken = Rows
            .Where(r => string.Equals(r.DeviceId, deviceId, StringComparison.Ordinal))
            .Select(r => r.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        for (var n = taken.Count + 1; ; n++)
        {
            var candidate = $"Compte {n.ToString(System.Globalization.CultureInfo.CurrentCulture)}";

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
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
        try
        {
            await _settings.SetInstanceEnabledAsync(row.Key, row.IsEnabled).ConfigureAwait(true);

            // Le cache de découverte porte l'ancienne valeur : le garder ferait
            // revenir la case à son état d'avant au prochain balayage.
            _instances = null;
        }
        finally
        {
            row.IsEnabledPending = false;
        }

        OnPropertyChanged(nameof(EnabledCount));
    }

    private async void OnManagedChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _settings.SetInstanceManagedAsync(row.Key, row.IsManaged).ConfigureAwait(true);

            // Même piège que pour le tri et pour les cases : toute écriture dans
            // les réglages doit invalider le cache, sans quoi le verrou se
            // rouvre tout seul au balayage suivant.
            _instances = null;

            // Le lanceur relit la liste des mises de côté au prochain placement.
            await _launcher.RefreshRanksAsync().ConfigureAwait(true);
        }
        finally
        {
            row.IsManagedPending = false;
        }
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
