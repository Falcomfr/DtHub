using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core.Adb;
using DtHub.Core.Android;
using DtHub.Core.Devices;
using DtHub.Core.Localization;
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

    /// <summary>
    /// Phones a completed account search has already covered.
    ///
    /// Only a phone absent from here announces that its accounts are being
    /// looked for. Keying that on "has no account yet" instead would leave a
    /// phone that really has no game announcing a search every time the
    /// periodic one runs, which is the flicker this set exists to prevent.
    /// </summary>
    private readonly HashSet<string> _searched = new(StringComparer.Ordinal);

    private DateTimeOffset _discoveredAt;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _problem;

    /// <summary>
    /// Le même signalement, mais entier : tous les constats, un par ligne.
    ///
    /// Le bandeau tient sur une ligne et ne montre donc que le plus grave.
    /// Ce qu'il laisse de côté se lit au survol, faute de quoi il faudrait
    /// corriger le premier problème pour apprendre l'existence du second.
    /// </summary>
    [ObservableProperty]
    private string? _problemDetail;

    /// <summary>
    /// Ce que montre le bandeau : tous les constats s'il y en a plusieurs, la
    /// ligne seule sinon.
    ///
    /// **Un constat par ligne, et non le pire suivi d'une bulle.** Un
    /// téléphone a porté trois constats en même temps, verrou, encombrement
    /// et batterie non préparée : un seul paraissait, et il fallait corriger
    /// le premier pour apprendre l'existence du second. C'est exactement le
    /// défaut que D120 refusait en gardant le texte visible plutôt que caché
    /// au survol ; le cacher par le nombre plutôt que par le survol revenait
    /// au même. Chaque ligne reste tronquée à une ligne, et le survol donne
    /// le texte entier.
    ///
    /// Le repli sur <see cref="Problem" /> compte : plusieurs chemins posent
    /// un signalement sans détail, une erreur attrapée par exemple.
    /// </summary>
    public string? AllProblems => string.IsNullOrEmpty(ProblemDetail) ? Problem : ProblemDetail;

    partial void OnProblemChanged(string? value) => OnPropertyChanged(nameof(AllProblems));

    partial void OnProblemDetailChanged(string? value) => OnPropertyChanged(nameof(AllProblems));

    /// <summary>
    /// Vrai quand ce qui est signalé coupera la séance, par opposition à un
    /// simple désagrément. Seule la couleur du sigle en dépend : le texte, lui,
    /// reste le même.
    /// </summary>
    [ObservableProperty]
    private bool _problemIsSerious;

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

    /// <summary>
    /// Faux tant qu'aucun balayage n'a eu lieu.
    ///
    /// Une liste vide avant le premier balayage ressemble en tout point à une
    /// liste vide après : dans les deux cas rien n'est là. Seule la différence
    /// entre « je n'ai pas regardé » et « j'ai regardé, il n'y a rien »
    /// autorise à l'écrire à l'écran, et elle ne se lit nulle part ailleurs.
    /// </summary>
    private bool _scanned;

    /// <summary>
    /// L'état de chaque appareil vu, pour le verdict de connexion. Les
    /// appareils seulement mémorisés y figurent hors ligne, ce qui est juste :
    /// un téléphone qu'on a connu et qui ne répond pas n'est pas un téléphone
    /// détecté.
    /// </summary>
    public IReadOnlyList<AdbDeviceState> DeviceStates => [.. _devices.Values.Select(d => d.State)];

    /// <summary>Vrai si au moins un téléphone répond.</summary>
    public bool HasConnectedDevice => !HasNoConnectedDevice;

    /// <summary>Vrai s'il y a plus d'une instance à ordonner.</summary>
    public bool CanReorder => Rows.Count > 1;

    /// <summary>Vrai s'il y a au moins une instance à montrer.</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>Vrai s'il y a au moins un appareil sans instance à signaler.</summary>
    public bool HasInactiveDevices => InactiveDevices.Count > 0;

    /// <summary>
    /// Vrai quand la carte « aucun appareil détecté » a lieu d'être.
    ///
    /// Elle s'affichait dès qu'aucun appareil ne répondait, y compris quand un
    /// téléphone était nommé juste au-dessus, en orange, avec la mention « à
    /// autoriser sur le téléphone ». L'écran se contredisait alors dans la
    /// même colonne. Un appareil vu, même muet, vaut mieux que le mot
    /// « aucun » : la ligne qui le nomme dit déjà ce qui manque.
    /// </summary>
    public bool ShowsNoDeviceCard => _scanned && HasNoConnectedDevice && !HasInactiveDevices;

    /// <summary>
    /// Vrai quand les astuces sur les fenêtres de jeu ont un objet.
    ///
    /// Elles parlent d'une fenêtre qui se fige et d'une fenêtre où la souris ne
    /// fait rien. Sans téléphone joignable il n'y a pas de fenêtre, et ces deux
    /// lignes ne sont plus que du texte de plus sur un écran qui n'a rien à
    /// dire. Comme pour les autres blocs, rien n'est affirmé avant le premier
    /// balayage.
    /// </summary>
    public bool ShowsWindowHelp => _scanned && !HasNoConnectedDevice;

    /// <summary>
    /// Vrai tant que le premier balayage n'a rien rendu.
    ///
    /// **La liste ne reste pas vide sans rien dire.** Au premier lancement,
    /// l'application tente de rejoindre chaque téléphone mémorisé à sa
    /// dernière adresse, et un téléphone éteint fait attendre le système
    /// plusieurs secondes. Pendant ce temps, l'écran n'avait ni appareil, ni
    /// carte « aucun appareil », qu'on se garde bien d'afficher avant de
    /// savoir : rien du tout, donc, et rien ne disait que ça travaillait.
    /// </summary>
    public bool ShowsSearching => !_scanned;

    /// <summary>Nombre d'instances cochées pour le lancement.</summary>
    public int EnabledCount => Rows.Count(i => i.IsEnabled);

    // Profils de lancement

    /// <summary>Les profils enregistrés, tels qu'ils paraissent dans le panneau.</summary>
    public ObservableCollection<LaunchProfileRowViewModel> Profiles { get; } = [];

    /// <summary>Vrai s'il y a au moins un profil à montrer.</summary>
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>
    /// Nom du profil retenu pour le démarrage, vide s'il n'y en a pas.
    ///
    /// Pris sur la ligne elle-même et non sur le réglage : c'est ainsi que le
    /// bouton dit exactement ce que la liste montre en accent, sans qu'une
    /// différence de casse ou d'espaces puisse les faire diverger.
    /// </summary>
    public string ActiveProfileName { get; private set; } = string.Empty;

    /// <summary>Vrai quand un profil est retenu pour le démarrage.</summary>
    public bool HasActiveProfile => ActiveProfileName.Length > 0;

    /// <summary>Ce que porte le bouton : le nom du profil, ou le mot générique.</summary>
    public string ProfilesButtonText => LaunchProfiles.ButtonLabel(ActiveProfileName);

    /// <summary>
    /// L'info-bulle du bouton. Elle nomme le profil en entier quand il y en a
    /// un, puisque le bouton, lui, coupe les noms longs.
    /// </summary>
    public string ProfilesTooltip => HasActiveProfile
        ? Strings.Format("ProfilesActiveTip", ActiveProfileName)
        : Strings.Get("ProfilesTip");

    /// <summary>
    /// Reprend les profils enregistrés.
    ///
    /// Rien n'est sélectionné ici : chaque ligne porte ses propres gestes, et
    /// c'est le bouton qui ouvre, non le fait de désigner la ligne. La liste
    /// peut donc se reconstruire à chaque balayage sans rien déclencher, ce
    /// qui n'était pas le cas quand choisir valait ouvrir.
    /// </summary>
    private async Task SyncProfilesAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);
        var byDefault = LaunchProfiles.Normalize(settings.DefaultLaunchProfile);

        Profiles.Clear();

        foreach (var profile in settings.LaunchProfiles)
        {
            Profiles.Add(new LaunchProfileRowViewModel(
                profile.Name,
                LaunchProfiles.Describe(profile, settings.Instances),
                string.Equals(profile.Name, byDefault, StringComparison.OrdinalIgnoreCase)));
        }

        ActiveProfileName = Profiles.FirstOrDefault(p => p.IsDefault)?.Name ?? string.Empty;

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(ActiveProfileName));
        OnPropertyChanged(nameof(HasActiveProfile));
        OnPropertyChanged(nameof(ProfilesButtonText));
        OnPropertyChanged(nameof(ProfilesTooltip));
    }

    /// <summary>
    /// Ouvre un profil : ce qui n'en fait pas partie se ferme, ce qui y
    /// manque s'ouvre.
    /// </summary>
    [RelayCommand]
    private async Task OpenProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        // Fermer des fenêtres de jeu ne se fait pas sans le dire : on peut être
        // en pleine partie, et un clic n'est pas un consentement.
        if (_launcher.ActiveSessions.Count > 0
            && !_dialogs.Confirm(
                Strings.Format("OpenProfileQuestion", profile.Name)
                + "\n\n" + Strings.Get("ProfileClosesOthers"),
                Strings.Get("OpenProfileTitle")))
        {
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
    private async Task CreateProfileAsync()
    {
        // La géométrie est relevée avant l'instantané, sans quoi le profil
        // retiendrait les positions de l'ouverture et non celles du moment :
        // déplacer une fenêtre puis créer n'aurait rien retenu.
        await _launcher.CaptureGeometriesAsync().ConfigureAwait(true);

        var settings = await _settings.GetAsync().ConfigureAwait(true);

        var open = _launcher.ActiveSessions
            .Select(s => s.Target.Key)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // À défaut de fenêtre ouverte, l'ensemble de démarrage fait foi : c'est
        // lui qui rouvrira, et c'est donc lui que l'on enregistre.
        if (open.Count == 0)
        {
            open = [.. settings.Instances.Where(i => i.IsEnabled).Select(i => i.Key)];
        }

        if (open.Count == 0)
        {
            _dialogs.ShowWarning(
                Strings.Get("NothingToRemember"),
                Strings.Get("CreateProfile"));

            return;
        }

        var tabbed = settings.Instances.Count(
            i => i.IsTabbed && open.Contains(i.Key, StringComparer.Ordinal));

        if (_dialogs.PromptText(
                Strings.Get("ProfileNameQuestion"),
                null,
                Strings.Get("CreateProfile"),
                LaunchProfiles.Announce(
                    open.Count,
                    settings.Quality,
                    settings.GameZoom,
                    tabbed,
                    settings.AudioEnabled),
                Strings.Get("Create")) is not { } typed)
        {
            return;
        }

        if (!await _settings.SaveLaunchProfileAsync(typed, open).ConfigureAwait(true))
        {
            _dialogs.ShowWarning(Strings.Get("ProfileNeedsName"), Strings.Get("CreateProfile"));
            return;
        }

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Désigne le profil du démarrage, ou le retire.</summary>
    [RelayCommand]
    private async Task ToggleDefaultProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null)
        {
            return;
        }

        await _settings
            .SetDefaultLaunchProfileAsync(profile.IsDefault ? null : profile.Name)
            .ConfigureAwait(true);

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Supprime un profil, après confirmation.</summary>
    [RelayCommand]
    private async Task DeleteProfileAsync(LaunchProfileRowViewModel? profile)
    {
        if (profile is null
            || !_dialogs.Confirm(
                Strings.Format("DeleteProfileQuestion", profile.Name)
                + "\n\n" + Strings.Get("OnlyTheRowGoes"),
                Strings.Get("DeleteProfileTitle")))
        {
            return;
        }

        await _settings.DeleteLaunchProfileAsync(profile.Name).ConfigureAwait(true);

        await SyncProfilesAsync().ConfigureAwait(true);
    }

    /// <summary>Balaye les téléphones et reconstruit la liste.</summary>
    /// <summary>Vrai si ce numéro de série est celui de l'appareil de cette instance.</summary>
    private static bool Carries(DeviceDiscoveryResult discovery, string serial, string deviceId) =>
        discovery.Devices.Any(d =>
            string.Equals(d.Id, deviceId, StringComparison.Ordinal)
            && string.Equals(d.Serial, serial, StringComparison.Ordinal));

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

            // Lister les appareils est bon marché ; redécouvrir les instances
            // ne l'est pas, chaque profil de chaque appareil demandant deux
            // commandes au téléphone. On ne le refait donc que si l'ensemble
            // des appareils a changé, ou après un long moment.
            var signature = string.Join(
                "|",
                discovery.Devices.Select(d => $"{d.Id}:{d.State}").Order(StringComparer.Ordinal));

            var looking = _instances is null
                || !string.Equals(signature, _signature, StringComparison.Ordinal)
                || DateTimeOffset.UtcNow - _discoveredAt >= _launcher.Quality.InstanceRediscovery;

            if (looking)
            {
                // The phones are shown before their accounts are looked for.
                // That search was measured at 2.9 seconds, it is the longest
                // thing the sweep does, and none of it says which phones are
                // there: they are known already.
                //
                // Until it answers, a phone with no row is a phone we have not
                // asked about, not a phone without the game. Saying otherwise
                // would put "game not installed" in orange under a phone that
                // has it, which is the whole reason this state exists.
                //
                // Only a phone never searched before says it. The search also
                // runs on a timer, to catch a profile added on the phone, and
                // announcing that one made every phone flip to "looking" for
                // three seconds every thirty-five seconds, for a refresh that
                // used to be invisible and has nothing to show for itself.
                foreach (var (id, view) in _devices)
                {
                    view.IsLookingForGames = !_searched.Contains(id);
                }

                ShowList(discovery, _instances ?? []);

                _instances = await _launcher.RefreshInstancesAsync(cancellationToken).ConfigureAwait(true);
                _signature = signature;
                _discoveredAt = DateTimeOffset.UtcNow;

                foreach (var id in _devices.Keys)
                {
                    _ = _searched.Add(id);
                }
            }

            // The looking branch above has just filled it; the fallback is
            // there because the compiler cannot see that and a list is a
            // saner answer than a crash.
            var instances = _instances ?? [];

            ShowList(discovery, instances);

            // Dropped only once the rows are in place, so that the phones
            // which turn out to have no game say so from the same frame,
            // rather than passing through a state where nothing is claimed.
            foreach (var view in _devices.Values)
            {
                view.IsLookingForGames = false;
            }

            // Les résumés de sessions citent les noms des comptes : ils se
            // refont ici, après que la liste a été reconstruite.
            await SyncProfilesAsync(cancellationToken).ConfigureAwait(true);

            RequestIcons();

            // What discovery found is on screen now. The readings kept from the
            // previous sweep are applied straight away, so a phone that already
            // had a gauge does not lose it while the new one is fetched.
            ApplyHealth(discovery, instances);

            // Then the slow pass, off the critical path. It asks each phone six
            // questions in turn, measured at 2.2 seconds for two devices, and
            // it used to run before any of the above: the list waited on it for
            // nothing, since none of its answers say which phones are there.
            // Awaiting here hands the dispatcher back, so the list is painted
            // before the questions are asked.
            await _launcher.RefreshHealthAsync(discovery, cancellationToken).ConfigureAwait(true);

            ApplyHealth(discovery, instances);
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

    /// <summary>
    /// Puts on screen what is known of the phones and of their accounts.
    ///
    /// Called twice on a sweep that has to look for accounts: once with the
    /// phones alone, so that they appear without waiting on a search measured
    /// at 2.9 seconds, and once with what that search found.
    /// </summary>
    private void ShowList(
        DeviceDiscoveryResult discovery,
        IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        // Seules les instances des téléphones joignables ont une ligne.
        // Les autres appareils ne disparaissent pas pour autant : ils
        // sont rappelés à part, avec la raison.
        var connected = discovery.Devices
            .Where(d => d.IsConnected)
            .ToDictionary(d => d.Id, StringComparer.Ordinal);

        SyncRows([.. instances.Where(i => connected.ContainsKey(i.DeviceId))]);
        RefreshBusyState();
        SyncInactiveDevices(discovery.Devices, instances);
        RefreshDeviceHeaders();

        _scanned = true;

        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasInactiveDevices));
        OnPropertyChanged(nameof(HasNoConnectedDevice));
        OnPropertyChanged(nameof(ShowsNoDeviceCard));
        OnPropertyChanged(nameof(ShowsSearching));
        OnPropertyChanged(nameof(ShowsWindowHelp));
        OnPropertyChanged(nameof(HasConnectedDevice));
        OnPropertyChanged(nameof(EnabledCount));
    }

    /// <summary>
    /// Puts the health readings on screen: the banner, the bubble behind it,
    /// and the gauge and findings under each phone.
    ///
    /// Called twice per sweep, once with whatever the previous pass left and
    /// once with the fresh readings. The questions put to the phones take
    /// seconds, and none of their answers say which phones are there, so the
    /// list is shown first and this fills it in.
    /// </summary>
    private void ApplyHealth(
        DeviceDiscoveryResult discovery,
        IReadOnlyList<Core.Dofus.DofusInstance> instances)
    {
        // Les incidents de la découverte d'appareils et ceux du balayage
        // d'instances partagent le même bandeau : un profil illisible est
        // aussi utile à savoir qu'un appareil injoignable.
        var warnings = discovery.Warnings.Concat(_launcher.InstanceWarnings).ToList();

        // Deux listes des mêmes faits : celle du bandeau, qui s'arrête
        // au plus grave, et celle de la bulle, qui les porte tous.
        List<string> everything = [.. warnings];

        // Le bilan de chaque appareil s'affiche sous son nom, plus dans
        // ce bandeau. Sauf pour un appareil qui n'a aucune ligne dans la
        // liste : il n'a pas d'en-tête où loger son constat, et le perdre
        // serait pire que de le mettre au mauvais endroit. Celui-là est
        // nommé, puisque rien autour ne le nomme.
        var homeless = _launcher.HealthByDevice
            .Where(pair => !instances.Any(i => Carries(discovery, pair.Key, i.DeviceId)))
            .Select(pair => pair.Value.Device is { Length: > 0 } named
                ? Strings.Format("NamedFinding", named, pair.Value.Text)
                : pair.Value.Text)
            .ToList();

        warnings.AddRange(homeless);
        everything.AddRange(homeless);

        ProblemIsSerious = _launcher.HealthIsSerious;

        // La reprise d'une fenêtre perdue se dit au même endroit, et pour
        // la même raison : elle explique ce qui vient de se passer.
        if (_launcher.RecoveryNotice is { Length: > 0 } recovery)
        {
            warnings.Add(recovery);
            everything.Add(recovery);
        }

        // Le jeu resté ouvert sur le téléphone se dit là aussi : sa fenêtre
        // a disparu, et plus rien d'autre à l'écran ne peut le signaler.
        if (_launcher.StopFailedNotice is { Length: > 0 } left)
        {
            warnings.Add(left);
            everything.Add(left);
        }

        Problem = warnings.Count > 0 ? string.Join(" ", warnings) : null;
        ProblemDetail = everything.Count > 0
            ? string.Join(Environment.NewLine, everything)
            : null;

        foreach (var device in discovery.Devices)
        {
            if (!_devices.TryGetValue(device.Id, out var view))
            {
                continue;
            }

            view.SetBattery(_launcher.Batteries.GetValueOrDefault(device.Serial));

            var found = _launcher.HealthByDevice.GetValueOrDefault(device.Serial);

            view.SetProblems(found?.Text, found?.Serious ?? false);
            view.SetNeedsPairing(_launcher.NeedsPairing.Contains(device.Id));
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
        ArgumentNullException.ThrowIfNull(args);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(() =>
        {
            // Le verrou vient de se prendre : il sait mieux que nous, et
            // surtout il se rendra plus tôt. L'engagement ne couvrait que
            // l'attente avant lui, et n'a plus rien à dire.
            if (args.IsBusy)
            {
                _ = _engages.Remove(args.DeviceId);
            }

            RefreshBusyState();
        });
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
            row.IsDeviceBusy = _launcher.IsDeviceBusy(row.DeviceId) || _engages.Contains(row.DeviceId);
        }
    }

    /// <summary>
    /// Appareils sur lesquels une action vient d'être demandée, mais dont le
    /// verrou d'ouverture n'est pas encore pris.
    ///
    /// Le verrou ne se prend qu'au bout du préambule d'ouverture : relecture
    /// des comptes, découverte des appareils, lecture de la liaison. Mesuré sur
    /// le poste, deux secondes et trois dixièmes entre le clic et la prise.
    /// Pendant tout ce temps, l'appareil n'était officiellement pas occupé, et
    /// les boutons des autres comptes restaient donc cliquables alors qu'une
    /// ouverture était déjà en route.
    ///
    /// L'engagement est pris à l'instant du clic, sans rien attendre, et rendu
    /// quand l'action se termine. Le verrou reste seul juge de qui passe : ceci
    /// ne fait que dire à l'écran ce qui est déjà décidé.
    /// </summary>
    private readonly HashSet<string> _engages = new(StringComparer.Ordinal);

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
        ActOnAsync(row, instance => _launcher.LaunchAsync([instance]), engageDevice: true);

    /// <summary>Ferme le jeu sur l'appareil puis le rouvre.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RestartAsync(InstanceRowViewModel? row) =>
        ActOnAsync(row, instance => _launcher.RestartAsync(instance), engageDevice: true);

    /// <summary>
    /// Ferme la fenêtre d'une instance.
    ///
    /// Sans engager l'appareil : fermer ne passe pas par le verrou
    /// d'ouverture, deux fermetures ne se gênent pas, et griser les voisines
    /// obligerait à fermer un compte à la fois.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StopAsync(InstanceRowViewModel? row) =>
        ActOnAsync(
            row,
            async instance =>
            {
                await _launcher.StopAsync(instance).ConfigureAwait(true);
                return new LaunchReport(0, []);
            },
            engageDevice: false);

    /// <summary>
    /// Exécute une action sur une instance et en répercute le résultat sur la
    /// liste. Le garde-fou est le même pour les trois boutons : ils parlent à
    /// l'appareil, et deux actions concurrentes laisseraient l'état affiché en
    /// désaccord avec les fenêtres réellement ouvertes.
    /// </summary>
    /// <param name="engageDevice">
    /// Vrai pour les actions qui prendront le verrou d'ouverture. Elles seules
    /// grisent les voisines du même téléphone, et seulement jusqu'à ce que le
    /// verrou prenne le relais.
    /// </param>
    private async Task ActOnAsync(
        InstanceRowViewModel? row,
        Func<Core.Dofus.DofusInstance, Task<LaunchReport>> action,
        bool engageDevice)
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

        // Avant le premier await : c'est tout l'intérêt. Les voisines du même
        // téléphone se grisent dans le même coup de peinture que la ligne
        // cliquée, et non deux secondes plus tard.
        if (engageDevice)
        {
            _ = _engages.Add(row.DeviceId);
            RefreshBusyState();
        }

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
            _ = _engages.Remove(row.DeviceId);

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
                row.TabbedChanged += OnTabbedChanged;
                row.QualityChanged += OnQualityChanged;
                row.ZoomChanged += OnZoomChanged;
                row.NameChanged += OnNameChanged;
                Rows.Add(row);
            }
            else
            {
                row.Update(instance, _launcher.IsOpen(instance));
            }

            row.Device = _devices.GetValueOrDefault(instance.DeviceId);

            // La distance avec laquelle sa fenêtre tourne, pour qu'un réglage
            // qui attend la prochaine ouverture le dise au lieu de paraître
            // mort. Rien à montrer quand la fenêtre est fermée.
            row.RunningZoom = row.IsRunning ? _launcher.ZoomInUse(instance.Key) : null;
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

        // Every phone is answered for, not only the ones without a row. This
        // used to live inside the loop below, which walks the inactive ones
        // alone: a phone whose accounts were found after a pass that had none
        // kept the "no game" given to it earlier, and said so in orange right
        // above its own accounts.
        foreach (var device in devices)
        {
            if (_devices.TryGetValue(device.Id, out var known))
            {
                known.HasNoGame = !withGame.Contains(device.Id);
            }
        }

        foreach (var id in inactive)
        {
            var view = _devices[id];

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
                Strings.Format("AddAccountQuestion", name, device.Name)
                + "\n\n" + Strings.Get("AddAccountConsequence"),
                Strings.Get("AddAccountTitle")))
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
                _dialogs.ShowInformation(result.Message, Strings.Get("AddAccountTitle"));
            }
            else
            {
                _dialogs.ShowWarning(result.Message, Strings.Get("AddAccountTitle"));
            }
        }
        finally
        {
            device.IsBusy = false;
        }

        // Le cache est jeté avant de rafraîchir : sans cela le balayage reprend
        // ce qu'il connaît déjà, et le compte tout juste créé n'apparaît qu'au
        // bout de l'intervalle de redécouverte, quinze à soixante secondes
        // selon le palier. Le même geste existe déjà dans ActOnAsync.
        _instances = null;

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
            var candidate = Strings.Format("DefaultAccountName", n);

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
                Strings.Format("ForgetDeviceQuestion", device.Name)
                + "\n\n" + Strings.Get("ForgetDeviceConsequence"),
                Strings.Get("ForgetDeviceTitle")))
        {
            return;
        }

        await _launcher.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);
        await _settings.ForgetDeviceAsync(device.DeviceId).ConfigureAwait(true);

        // **L'appareil quitte la vue ici, et non au prochain balayage.**
        //
        // Il fallait cliquer deux fois pour l'enlever, et il s'affichait entre
        // les deux « Jeu non installé », ce qui était faux. Les deux symptômes
        // ont la même cause : « RefreshAsync » rend la main sans rien faire
        // quand un balayage est déjà en cours, et il en part un toutes les deux
        // secondes. L'appel ci-dessous ne s'exécutait donc presque jamais, et
        // l'appelant croyait pourtant avoir rafraîchi.
        //
        // Le balayage en vol, lui, finissait avec la liste d'appareils d'avant
        // la rupture mais les instances déjà effacées : l'appareil reparaissait
        // sans aucun compte, donc marqué comme dépourvu du jeu. Une absence
        // d'information montrée comme un constat.
        //
        // On sait ce qu'on vient de faire : on le retire, sans rien attendre
        // de personne.
        Forget(device);

        // Le cache est jeté, sans quoi le balayage reprend ce qu'il connaît
        // déjà et les lignes de l'appareil restent à l'écran jusqu'à
        // l'intervalle de redécouverte. Même geste que pour l'ajout d'un compte.
        _instances = null;

        await RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Retire de la vue tout ce qui appartenait à un appareil.
    ///
    /// Les trois collections, parce qu'un appareil peut être dans n'importe
    /// laquelle : ses comptes dans <see cref="Rows" />, sa fiche dans
    /// <see cref="InactiveDevices" /> quand il n'en a aucun, et son entrée dans
    /// l'index qui sert à les retrouver.
    /// </summary>
    private void Forget(DeviceGroupViewModel device)
    {
        foreach (var row in Rows.Where(r => r.DeviceId == device.DeviceId).ToList())
        {
            _ = Rows.Remove(row);
        }

        _ = InactiveDevices.Remove(device);
        _ = _devices.Remove(device.DeviceId);
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

    /// <summary>
    /// Le compte entre dans le cadre à onglets ou en sort.
    ///
    /// Le lanceur s'occupe de tout : il écrit le réglage, puis loge ou ressort
    /// la fenêtre si elle est ouverte. Rien n'est rouvert.
    /// </summary>
    /// <summary>
    /// Écrit le palier propre à un compte.
    ///
    /// Le nouveau palier ne vaudra qu'à la prochaine ouverture de la fenêtre :
    /// la définition et le débit sont fixés au lancement de scrcpy, et une
    /// session en cours ne se renégocie pas.
    /// </summary>
    private async void OnQualityChanged(object? sender, InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            await _settings.SetInstanceQualityAsync(row.Key, row.Quality).ConfigureAwait(true);

            // Même piège que pour le verrou : sans cela le balayage suivant
            // rendrait à la ligne son ancien palier.
            _instances = null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Problem = exception.Message;
        }
        finally
        {
            row.IsQualityPending = false;
        }
    }

    private async void OnTabbedChanged(object? sender, InstanceRowViewModel row)
    {
        try
        {
            await _launcher.SetTabbedAsync(row.Instance, row.IsTabbed).ConfigureAwait(true);

            // Même piège que pour le verrou : sans cela le balayage suivant
            // rendrait à la ligne son ancien état.
            _instances = null;
        }
        catch (AdbException exception)
        {
            Problem = exception.UserMessage;
        }
        finally
        {
            row.IsTabbedPending = false;
        }
    }

    private async void OnZoomChanged(object? sender, InstanceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        try
        {
            await _settings.SetInstanceZoomAsync(row.Key, row.Zoom).ConfigureAwait(true);

            // Même piège que pour le palier : sans cela le balayage suivant
            // rendrait à la ligne son ancienne distance.
            _instances = null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Problem = exception.Message;
        }
        finally
        {
            row.IsZoomPending = false;
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
