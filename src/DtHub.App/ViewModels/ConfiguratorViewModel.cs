using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Hotkeys;
using DtHub.Core.Settings;
using DtHub.Core.Storage;
using DtHub.Core.Windows;

namespace DtHub.App.ViewModels;

/// <summary>
/// La fenêtre principale une fois le jeu lancé. Trois onglets, rien de plus.
/// Chaque modification est enregistrée immédiatement et appliquée aux fenêtres
/// déjà ouvertes.
/// </summary>
public sealed partial class ConfiguratorViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GameLauncher _launcher;
    private readonly IDialogService _dialogs;
    private readonly IAppPaths _paths;

    private bool _loading;
    private bool _movingWindows;
    private int? _pendingPercent;
    private CancellationTokenSource? _persistSize;

    public ConfiguratorViewModel(
        InstanceListViewModel instances,
        SettingsService settings,
        GameLauncher launcher,
        IDialogService dialogs,
        IAppPaths paths)
    {
        Instances = instances;
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;

        // Les raccourcis écrivent la taille et l'ancrage sans passer par ici :
        // sans cet abonnement, le curseur et la grille gardaient la valeur
        // qu'ils avaient à l'ouverture et mentaient jusqu'au redémarrage.
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>
    /// Reflète une écriture venue d'ailleurs, raccourci clavier compris.
    ///
    /// Le garde-fou de chargement est indispensable : sans lui, le curseur qui
    /// se remet à la bonne valeur redéclencherait un redimensionnement, et la
    /// grille un replacement, chacun réécrivant les réglages en boucle.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettingsDocument document)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null)
        {
            return;
        }

        // Volontairement sans await à l'intérieur : deux rappels qui
        // s'entrelaçaient sur le fil d'interface se rendaient le garde-fou
        // l'un à l'autre, et le second le rabaissait pendant que le premier
        // écrivait encore. Le curseur et la qualité partaient alors tout
        // seuls, chaque écriture en déclenchant une autre. Les paliers
        // viennent du document lui-même, il n'y a donc rien à attendre.
        dispatcher.InvokeAsync(() =>
        {
            if (_loading || _movingWindows)
            {
                return;
            }

            var presets = new WindowSizePresets { Percentages = document.SizePercentages }.Sanitized();

            _loading = true;

            try
            {
                GameAnchor = document.GameAnchor;
                Quality = document.Quality;
                Zoom = document.GameZoom;
                AudioEnabled = document.AudioEnabled;
                TurnScreenOff = document.TurnDeviceScreenOff;
                DisableAnimations = document.DisableDeviceAnimations;
                ReadCustomQuality(document);
                SizePercent = document.CustomSizePercent > 0
                    ? document.CustomSizePercent
                    : presets.PercentageAt(document.SizeIndex);
            }
            finally
            {
                _loading = false;
            }
        });
    }

    public InstanceListViewModel Instances { get; }

    // Onglet Général

    /// <summary>Les neuf positions possibles du bloc de fenêtres.</summary>
    public IReadOnlyList<WindowAnchor> Anchors { get; } = WindowAnchors.All;

    [ObservableProperty]
    private WindowAnchor _gameAnchor = WindowAnchor.MiddleLeft;

    /// <summary>
    /// Taille des fenêtres, en pourcentage de la zone utilisable de l'écran.
    /// Elle est donc proportionnelle à l'écran employé.
    /// </summary>
    [ObservableProperty]
    private int _sizePercent = 60;

    /// <summary>
    /// Compromis entre finesse de l'image et charge de la machine. L'image ne
    /// change qu'à la réouverture des fenêtres : les options sont figées au
    /// lancement de scrcpy.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomQuality))]
    private StreamQuality _quality = StreamQuality.Medium;

    /// <summary>
    /// Vrai quand le palier personnalisé est choisi. Les réglages fins ne
    /// paraissent qu'alors : les montrer en permanence chargerait le panneau de
    /// quatre lignes que la plupart des gens n'ont pas à connaître.
    /// </summary>
    public bool IsCustomQuality => Quality == StreamQuality.Custom;

    /// <summary>Hauteur de l'afficheur, au palier personnalisé.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    private int _customHeight = 1080;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    private int _customFps = 60;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    private int _customBitrateKbps = 12000;

    /// <summary>
    /// « h264 » ou « h265 ». Rien d'autre n'est proposé : relevé par
    /// <c>scrcpy --list-encoders</c>, ce sont les seuls que le téléphone de
    /// référence encode en matériel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    private string _customCodec = "h264";

    /// <summary>
    /// Ce que valent les quatre nombres, ramenés à la seule mesure qui compte.
    ///
    /// Un débit nu ne veut rien dire : seize mégabits sont généreux en 720p et
    /// misérables en 2160p. C'est l'erreur dans laquelle ce projet est déjà
    /// tombé, et cette phrase existe pour qu'elle ne se répète pas sous la main
    /// de l'utilisateur.
    /// </summary>
    public string BitrateSummary => BitrateAdvice
        .Read(CustomWidth, CustomHeight, CustomFps, CustomBitrateKbps, CustomCodec)
        .Summary;

    /// <summary>
    /// La largeur qui va avec la hauteur choisie. Le jeu s'affiche en paysage
    /// et l'afficheur virtuel suit le 16:9 : la demander séparément ferait un
    /// réglage de plus pour une valeur qui se déduit.
    /// </summary>
    private int CustomWidth => CustomHeight * 16 / 9;

    /// <summary>
    /// Les hauteurs proposées. Le jeu s'affiche en 16:9 et la largeur suit :
    /// une seule liste suffit donc à décrire la définition.
    /// </summary>
    public IReadOnlyList<IntChoice> HeightChoices { get; } =
    [
        new("1280 x 720", 720),
        new("1920 x 1080", 1080),
        new("2560 x 1440", 1440),
        new("3840 x 2160", 2160),
    ];

    /// <summary>
    /// Les cadences proposées. Pas de 120 : mesuré sur le jeu, il en rend
    /// trente-huit, et les demander ne ferait que diviser les bits accordés à
    /// chaque image qui existe vraiment.
    /// </summary>
    public IReadOnlyList<IntChoice> FpsChoices { get; } =
    [
        new("30 images par seconde", 30),
        new("45 images par seconde", 45),
        new("60 images par seconde", 60),
    ];

    public IReadOnlyList<IntChoice> BitrateChoices { get; } =
    [
        new("4 Mb/s", 4000),
        new("8 Mb/s", 8000),
        new("12 Mb/s", 12000),
        new("16 Mb/s", 16000),
        new("25 Mb/s", 25000),
        new("40 Mb/s", 40000),
    ];

    /// <summary>
    /// Deux codecs, et pas quatre. AV1 et VP8 n'ont qu'un encodeur logiciel sur
    /// le téléphone de référence, relevé par « scrcpy --list-encoders » : les
    /// proposer coûterait bien plus qu'ils ne rendent.
    /// </summary>
    public IReadOnlyList<TextChoice> CodecChoices { get; } =
    [
        new("H.264", "h264"),
        new("H.265, meilleur à débit égal", "h265"),
    ];

    // Onglet Raccourcis, en lecture seule

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];

    // Divers

    public string ProductName => ProductInfo.Name;

    public string Version => ProductInfo.Version;

    /// <summary>Raccourci d'affichage, rappelé en clair dans la fenêtre.</summary>
    [ObservableProperty]
    private string _toggleShortcutText = "Ctrl + P";

    /// <summary>Ce que le bandeau de mise à jour annonce. Vide, il ne paraît pas.</summary>
    [ObservableProperty]
    private string _updateText = string.Empty;

    /// <summary>
    /// Vrai quand l'application se met à jour toute seule. Elle télécharge alors
    /// la livraison en fond et la pose en quittant, jamais en pleine session.
    /// </summary>
    [ObservableProperty]
    private bool _updatesAutomatic = true;

    partial void OnUpdatesAutomaticChanged(bool value) =>
        _ = SaveAsync(settings => settings.UpdatesAutomatic = value);

    /// <summary>
    /// Raccourci du replacement, affiché à côté du bouton. Vide quand aucun
    /// raccourci n'est associé à l'action.
    /// </summary>
    [ObservableProperty]
    private string _rearrangeShortcutText = string.Empty;

    /// <summary>Raccourci du côte à côte, affiché à côté du bouton.</summary>
    [ObservableProperty]
    private string _tileShortcutText = string.Empty;

    /// <summary>Raccourci du suivi de quêtes, affiché à côté du bouton.</summary>
    [ObservableProperty]
    private string _questsShortcutText = string.Empty;

    /// <summary>Raccourci de sortie, affiché sous le bouton Quitter.</summary>
    [ObservableProperty]
    private string _quitShortcutText = string.Empty;

    /// <summary>Distance apparente dans le jeu, figée à l'ouverture d'une session.</summary>
    [ObservableProperty]
    private GameZoom _zoom = GameZoom.Normal;

    /// <summary>Vrai le temps que les fenêtres se referment et rouvrent.</summary>
    [ObservableProperty]
    private bool _isReopening;

    /// <summary>
    /// Sortir le son du téléphone sur le PC.
    ///
    /// C'est le son de l'appareil entier, non celui d'un compte : Android ne
    /// sait pas l'isoler par application. Une seule fenêtre par téléphone le
    /// porte donc, sans quoi le même flux arriverait en plusieurs exemplaires.
    /// </summary>
    [ObservableProperty]
    private bool _audioEnabled;

    /// <summary>Éteindre l'écran du téléphone pendant les sessions.</summary>
    [ObservableProperty]
    private bool _turnScreenOff;

    /// <summary>
    /// Couper les animations d'Android pendant les sessions.
    ///
    /// Seul réglage du panneau qui touche le téléphone plutôt que la session :
    /// ce sont trois valeurs globales, rendues à la fermeture des fenêtres.
    /// </summary>
    [ObservableProperty]
    private bool _disableAnimations;

    public string Disclaimer =>
        "Projet indépendant, sans lien avec Ankama.";

    /// <summary>Charge l'état des réglages dans la fenêtre.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            GameAnchor = settings.GameAnchor;

            var presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(true);

            Quality = settings.Quality;
            Zoom = settings.GameZoom;
            UpdatesAutomatic = settings.UpdatesAutomatic;
            AudioEnabled = settings.AudioEnabled;
            TurnScreenOff = settings.TurnDeviceScreenOff;
            DisableAnimations = settings.DisableDeviceAnimations;
            ReadCustomQuality(settings);

            SizePercent = settings.CustomSizePercent > 0
                ? settings.CustomSizePercent
                : presets.PercentageAt(settings.SizeIndex);

            await RefreshHotkeysAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }

        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Relit les raccourcis, après une modification dans l'éditeur.</summary>
    public async Task RefreshHotkeysAsync(CancellationToken cancellationToken = default)
    {
        var hotkeys = await _settings.GetHotkeysAsync(cancellationToken).ConfigureAwait(true);

        Hotkeys.Clear();
        foreach (var binding in hotkeys.Bindings)
        {
            Hotkeys.Add(new HotkeyRowViewModel(binding));
        }

        ToggleShortcutText = hotkeys.For(HotkeyAction.ToggleConfigurator)?.DisplayText ?? "Ctrl + P";

        // Les actions de la barre du bas rappellent leur raccourci, et le
        // suivent quand il est modifié dans l'éditeur.
        RearrangeShortcutText = hotkeys.For(HotkeyAction.Rearrange)?.DisplayText ?? string.Empty;
        TileShortcutText = hotkeys.For(HotkeyAction.Tile)?.DisplayText ?? string.Empty;
        QuestsShortcutText = hotkeys.For(HotkeyAction.Quests)?.DisplayText ?? string.Empty;
        QuitShortcutText = hotkeys.For(HotkeyAction.Quit)?.DisplayText ?? string.Empty;

    }

    /// <summary>Rafraîchit ce qui change tout seul : appareils et états.</summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        await Instances.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    /// <summary>
    /// Empile les fenêtres sur celle qui est active, ou sur la première.
    /// </summary>
    [RelayCommand]
    private async Task RearrangeAsync() => await _launcher.StackOnActiveAsync().ConfigureAwait(true);

    /// <summary>Ouvre l'instance choisie, sans toucher aux autres.</summary>
    [RelayCommand]
    private async Task LaunchInstanceAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.LaunchAsync([row.Instance]).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
    }

    /// <summary>Ferme puis rouvre l'instance, jeu compris.</summary>
    [RelayCommand]
    private async Task RestartAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var report = await _launcher.RestartAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();

        Instances.Problem = report.Problems.Count > 0 ? string.Join(" ", report.Problems) : null;
    }

    [RelayCommand]
    private async Task StopAsync(InstanceRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await _launcher.StopAsync(row.Instance).ConfigureAwait(true);
        Instances.RefreshRunningState();
    }

    [RelayCommand]
    private async Task ArrangeAsync(CancellationToken cancellationToken)
    {
        var moved = await _launcher.ArrangeAsync(cancellationToken).ConfigureAwait(true);

        if (moved == 0)
        {
            Instances.Problem = "Aucune fenêtre de jeu à replacer.";
        }
    }

    /// <summary>Range les fenêtres côte à côte, l'active à droite.</summary>
    [RelayCommand]
    private async Task TileAsync()
    {
        var placed = await _launcher.TileAsync().ConfigureAwait(true);

        if (placed == 0)
        {
            Instances.Problem = "Aucune fenêtre de jeu à ranger.";
        }
    }

    /// <summary>Ouvre le suivi de quêtes, ou le referme s'il est déjà là.</summary>
    [RelayCommand]
    private void Quests() => QuestsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Demandé depuis le bouton d'outil. La fenêtre est construite par
    /// l'application, pas par ce modèle : il n'a pas à connaître les fenêtres.
    /// </summary>
    public event EventHandler? QuestsRequested;

    /// <summary>Montre ce que la version en attente apporte.</summary>
    [RelayCommand]
    private void UpdateNotes() => UpdateNotesRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Demandé depuis le bandeau de mise à jour, même raison.</summary>
    public event EventHandler? UpdateNotesRequested;

    [RelayCommand]
    private void OpenLogs() => _dialogs.OpenFolder(_paths.LogsDirectory);

    private Task SaveAsync(Action<AppSettingsDocument> mutate) =>
        _loading ? Task.CompletedTask : _settings.UpdateAsync(mutate);

    partial void OnGameAnchorChanged(WindowAnchor value)
    {
        // Le replacement suit le garde-fou de chargement, comme
        // l'enregistrement : sans cela, relire les réglages au démarrage
        // replacerait toutes les fenêtres et effacerait leur géométrie
        // mémorisée avant même que l'utilisateur ait touché à quoi que ce soit.
        if (_loading)
        {
            return;
        }

        _ = ApplyAnchorAsync(value);
    }

    /// <summary>
    /// Enregistre le coin choisi, puis replace les fenêtres.
    ///
    /// L'écriture est attendue : le replacement relit les réglages pour en
    /// tirer le coin, et lancer les deux de front faisait relire l'ancienne
    /// valeur. Cliquer une case ne faisait alors rien de visible.
    /// </summary>
    private async Task ApplyAnchorAsync(WindowAnchor value)
    {
        await SaveAsync(s => s.GameAnchor = value).ConfigureAwait(true);
        await _launcher.ArrangeAsync().ConfigureAwait(true);
    }

    partial void OnSizePercentChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _ = FollowSliderAsync(value);
        _ = PersistSizeSoonAsync(value);
    }

    /// <summary>
    /// Suit le curseur au plus près. Un seul déplacement à la fois : les crans
    /// arrivent plus vite que les fenêtres ne bougent, et les empiler ferait
    /// traîner la taille derrière le curseur. Seule la dernière valeur reçue
    /// pendant un déplacement est appliquée ensuite.
    /// </summary>
    private async Task FollowSliderAsync(int percent)
    {
        if (_movingWindows)
        {
            _pendingPercent = percent;
            return;
        }

        _movingWindows = true;

        try
        {
            var next = percent;

            while (true)
            {
                await _launcher.ApplyPercentAsync(next, persist: false).ConfigureAwait(true);

                if (_pendingPercent is not { } queued)
                {
                    break;
                }

                _pendingPercent = null;
                next = queued;
            }
        }
        finally
        {
            _movingWindows = false;
        }
    }

    /// <summary>
    /// Écrit la taille une fois le curseur reposé. Chaque cran déclencherait
    /// sinon une réécriture complète du fichier de réglages.
    /// </summary>
    private async Task PersistSizeSoonAsync(int percent)
    {
        _persistSize?.Cancel();
        _persistSize?.Dispose();

        var cancellation = new CancellationTokenSource();
        _persistSize = cancellation;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellation.Token).ConfigureAwait(true);

            await _launcher.ApplyPercentAsync(percent, persist: true, cancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Le curseur a bougé de nouveau : c'est la valeur suivante qui compte.
        }
    }

    partial void OnQualityChanged(StreamQuality value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetQualityAsync(value));
    }

    partial void OnZoomChanged(GameZoom value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetZoomAsync(value));
    }

    partial void OnCustomHeightChanged(int value) => SaveCustomQuality();

    partial void OnCustomFpsChanged(int value) => SaveCustomQuality();

    partial void OnCustomBitrateKbpsChanged(int value) => SaveCustomQuality();

    partial void OnCustomCodecChanged(string value) => SaveCustomQuality();

    /// <summary>
    /// Retient les quatre valeurs fines, et ne rouvre les fenêtres que si le
    /// palier personnalisé est celui en vigueur.
    ///
    /// Les régler alors qu'un autre palier est coché ne change rien à l'image :
    /// rouvrir dans ce cas ferait clignoter toutes les fenêtres pour rien.
    /// </summary>
    /// <summary>
    /// Reprend les quatre valeurs fines depuis les réglages. Appelée sous le
    /// garde-fou de chargement, comme la qualité et la distance.
    /// </summary>
    private void ReadCustomQuality(AppSettingsDocument document)
    {
        var custom = document.CustomQuality.Sanitized();

        CustomHeight = custom.MaximumDisplayHeight;
        CustomFps = custom.MaxFps;
        CustomBitrateKbps = custom.BitrateKbps;
        CustomCodec = custom.VideoCodec;
    }

    private void SaveCustomQuality()
    {
        if (_loading)
        {
            return;
        }

        var custom = new CustomQuality
        {
            MaximumDisplayHeight = CustomHeight,
            MaxFps = CustomFps,
            BitrateKbps = CustomBitrateKbps,
            VideoCodec = CustomCodec,
        }.Sanitized();

        if (Quality == StreamQuality.Custom)
        {
            _ = ApplyStartupSettingAsync(() => _settings.SetCustomQualityAsync(custom));
            return;
        }

        _ = SaveAsync(settings => settings.CustomQuality = custom);
    }

    partial void OnAudioEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetAudioEnabledAsync(value));
    }

    partial void OnTurnScreenOffChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = ApplyStartupSettingAsync(() => _settings.SetTurnDeviceScreenOffAsync(value));
    }

    partial void OnDisableAnimationsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Rouvre comme les autres : la coupure s'applique au téléphone à
        // l'ouverture d'une session, et la restauration à la fermeture de la
        // dernière.
        _ = ApplyStartupSettingAsync(() => _settings.SetDisableDeviceAnimationsAsync(value));
    }

    /// <summary>
    /// Enregistre un réglage qui n'agit qu'à l'ouverture d'une session, puis
    /// rouvre les fenêtres pour qu'il se voie.
    ///
    /// Sans cela, changer la qualité ou la distance ne montrait rien : ce sont
    /// des arguments de démarrage de scrcpy, figés pour toute la session. Le
    /// choix de rouvrir plutôt que d'attendre la prochaine fois est celui de
    /// l'utilisateur, qui ne comprenait pas pourquoi le réglage semblait mort.
    /// </summary>
    private async Task ApplyStartupSettingAsync(Func<Task> write)
    {
        if (IsReopening)
        {
            return;
        }

        IsReopening = true;

        try
        {
            await write().ConfigureAwait(true);

            var report = await _launcher.ReopenAsync().ConfigureAwait(true);

            if (report.Problems.Count > 0)
            {
                _dialogs.ShowWarning(string.Join(Environment.NewLine, report.Problems));
            }
        }
        finally
        {
            IsReopening = false;
        }
    }


}
