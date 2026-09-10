using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DtHub.App.Services;
using DtHub.Core;
using DtHub.Core.Adb;
using DtHub.Core.Devices;
using DtHub.Core.Hotkeys;
using DtHub.Core.Localization;
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

    private readonly DiagnosticReporter _reporter;
    private bool _loading;
    private bool _movingWindows;
    private int? _pendingPercent;
    private CancellationTokenSource? _persistSize;

    public ConfiguratorViewModel(
        InstanceListViewModel instances,
        SettingsService settings,
        GameLauncher launcher,
        IDialogService dialogs,
        IAppPaths paths,
        DiagnosticReporter reporter,
        IUsbEnumerationInspector usb)
    {
        Instances = instances;
        _settings = settings;
        _launcher = launcher;
        _dialogs = dialogs;
        _paths = paths;
        _reporter = reporter;
        _usb = usb;

        // Une faute hors du fil d'interface ne s'affiche pas : elle se compte,
        // et cette ligne est le seul endroit où elle se voit.
        _reporter.IncidentRecorded += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                () => OnPropertyChanged(nameof(HasIncident)));

        // Les raccourcis écrivent la taille et l'ancrage sans passer par ici :
        // sans cet abonnement, le curseur et la grille gardaient la valeur
        // qu'ils avaient à l'ouverture et mentaient jusqu'au redémarrage.
        _settings.Changed += OnSettingsChanged;

        // Ce que la liaison encaisse dépend du nombre de fenêtres ouvertes.
        // Sans cet abonnement, la ligne annonçait le coût d'une seule fenêtre
        // alors que deux tournaient, c'est-à-dire la moitié de la vérité, et
        // justement au moment où elle sert.
        _launcher.SessionChanged += OnSessionChanged;

        // Une mise de côté ou un passage en onglet ne touche à aucune session,
        // et les deux touches de rangement doivent pourtant disparaître.
        _launcher.ArrangeableChanged += OnArrangeableChanged;
    }

    /// <summary>
    /// Vrai quand il y a de quoi ranger : au moins deux fenêtres que les
    /// placements peuvent bouger.
    ///
    /// Empiler ou mettre côte à côte n'a aucun sens à une seule fenêtre, et
    /// n'en a pas davantage sur des fenêtres mises de côté : ces placements les
    /// ignorent. Les touches se retirent donc plutôt que de ne rien faire quand
    /// on les presse.
    ///
    /// Le cadre à onglets compte pour une fenêtre, ce qu'il n'a pas toujours
    /// fait : deux comptes logés faisaient disparaître les deux touches, et un
    /// compte logé plus une fenêtre libre aussi, alors qu'il y avait bien deux
    /// fenêtres à ranger. Un cadre figé par un cadenas ne compte pas.
    /// </summary>
    public bool CanArrange => _launcher.ArrangeableCount > 1;

    private void OnArrangeableChanged(object? sender, EventArgs e) =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(
            () => OnPropertyChanged(nameof(CanArrange)));

    /// <summary>
    /// Une fenêtre s'est ouverte ou fermée : ce que la liaison porte a changé.
    ///
    /// L'événement vient d'un fil de fond, d'où le passage par le répartiteur.
    /// </summary>
    private void OnSessionChanged(object? sender, Core.Scrcpy.ScrcpySession session) =>
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(BitrateSummary));
            OnPropertyChanged(nameof(LinkSummary));
            OnPropertyChanged(nameof(DisplayFitSummary));
            OnPropertyChanged(nameof(CanArrange));
        });

    /// <summary>
    /// Reflète une écriture venue d'ailleurs, raccourci clavier compris.
    ///
    /// Le garde-fou de chargement est indispensable : sans lui, le curseur qui
    /// se remet à la bonne valeur redéclencherait un redimensionnement, et la
    /// grille un replacement, chacun réécrivant les réglages en boucle.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettingsDocument document)
    {
        RememberNames(document);

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
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    [NotifyPropertyChangedFor(nameof(DisplayFitSummary))]
    private int _customHeight = 1080;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private int _customFps = 60;

    /// <summary>
    /// Finesse d'image, en bits par pixel et par image.
    ///
    /// Et non un débit en mégabits, contrairement à ce que proposent les
    /// interfaces qui ne pilotent qu'un seul miroir : ici la définition de
    /// l'afficheur suit la taille de la fenêtre, et un débit absolu servirait
    /// grassement une petite fenêtre et affamerait une grande.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private double _customBitsPerPixel = 0.09;

    /// <summary>
    /// « h264 » ou « h265 ». Rien d'autre n'est proposé : relevé par
    /// <c>scrcpy --list-encoders</c>, ce sont les seuls que le téléphone de
    /// référence encode en matériel.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BitrateSummary))]
    [NotifyPropertyChangedFor(nameof(LinkSummary))]
    private string _customCodec = "h264";

    /// <summary>
    /// Ce que valent les quatre nombres, ramenés à la seule mesure qui compte.
    ///
    /// Un débit nu ne veut rien dire : seize mégabits sont généreux en 720p et
    /// misérables en 2160p. C'est l'erreur dans laquelle ce projet est déjà
    /// tombé, et cette phrase existe pour qu'elle ne se répète pas sous la main
    /// de l'utilisateur.
    /// </summary>
    public string BitrateSummary => CurrentPlan.Summary;

    /// <summary>
    /// Ce que la liaison recevra, toutes fenêtres confondues.
    ///
    /// C'est la ligne qui distingue ce panneau de celui d'un miroir simple :
    /// DT Hub ouvre plusieurs fenêtres sur un seul téléphone et une seule
    /// liaison. Trois comptes à vingt-cinq mégabits en demandent
    /// soixante-quinze, là où un téléphone en Wi-Fi 4 sur 2,4 GHz en rend
    /// une soixantaine.
    /// </summary>
    public string LinkSummary => CurrentPlan.LinkSummary;

    /// <summary>
    /// La définition réellement demandée, et si le plafond choisi y change
    /// quelque chose.
    ///
    /// C'est la réponse à un piège discret : le palier retenu est le premier
    /// au-dessus de la fenêtre, donc monter le plafond au-delà de la taille des
    /// fenêtres ne demande rien de plus. Sans cette ligne, on croit gagner en
    /// finesse là où l'on ne gagne rien, et l'on paie parfois cher pour s'en
    /// apercevoir.
    /// </summary>
    public string DisplayFitSummary => Core.Scrcpy.DisplayFit.Describe(
        CustomHeight,
        Core.Scrcpy.DisplayFit.FromCommandLine(
            _launcher.ActiveSessions.FirstOrDefault(s => s.IsAlive)?.CommandLine));

    private BitratePlan CurrentPlan => BitrateAdvice.Plan(
        CustomBitsPerPixel,
        CustomWidth,
        CustomHeight,
        CustomFps,
        CustomCodec,
        OpenWindowsOnBusiestDevice,
        QualityProfile.For(StreamQuality.Custom).CeilingKbps);

    /// <summary>
    /// Fenêtres ouvertes sur le téléphone qui en porte le plus.
    ///
    /// C'est ce téléphone qui décide : sa liaison et son encodeur sont les
    /// premiers à céder. Compter toutes les fenêtres, tous appareils
    /// confondus, exagérerait la charge de chacun.
    /// </summary>
    private int OpenWindowsOnBusiestDevice
    {
        get
        {
            var sessions = _launcher.ActiveSessions;

            return sessions.Count == 0
                ? 1
                : sessions
                    .GroupBy(s => s.Target.DeviceId, StringComparer.Ordinal)
                    .Max(g => g.Count());
        }
    }

    /// <summary>
    /// La largeur qui va avec la hauteur choisie. Le jeu s'affiche en paysage
    /// et l'afficheur virtuel suit le 16:9 : la demander séparément ferait un
    /// réglage de plus pour une valeur qui se déduit.
    ///
    /// C'est un <b>plafond</b>, et non la définition retenue : celle-ci suit la
    /// taille de la fenêtre et peut être moindre. Le débit annoncé est donc le
    /// plus haut que ce réglage puisse demander.
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
        new(Strings.Format("FpsChoice", 30), 30),
        new(Strings.Format("FpsChoice", 45), 45),
        new(Strings.Format("FpsChoice", 60), 60),
    ];

    /// <summary>
    /// Les finesses proposées, en bits par pixel et par image.
    ///
    /// Les nombres sont montrés : ils ne parlent pas à tout le monde, mais ils
    /// parlent à qui a choisi ce palier, et ils rendent les paliers comparables
    /// entre eux. La référence du métier pour du H.264 de bonne facture tourne
    /// autour de 0,10, ce que « Standard » vise.
    /// </summary>
    public IReadOnlyList<DoubleChoice> FinesseChoices { get; } =
    [
        new(Strings.Get("FinesseThrifty"), 0.06),
        new(Strings.Get("FinesseStandard"), 0.09),
        new(Strings.Get("FinesseFine"), 0.12),
        new(Strings.Get("FinesseVeryFine"), 0.16),
    ];

    /// <summary>
    /// Deux codecs, et pas quatre. AV1 et VP8 n'ont qu'un encodeur logiciel sur
    /// le téléphone de référence, relevé par « scrcpy --list-encoders » : les
    /// proposer coûterait bien plus qu'ils ne rendent.
    /// </summary>
    public IReadOnlyList<TextChoice> CodecChoices { get; } =
    [
        new("H.264", "h264"),
        new(Strings.Get("CodecH265"), "h265"),
    ];

    /// <summary>
    /// La langue de l'interface. La valeur vide suit la langue d'affichage de
    /// Windows, ce que fait l'application quand on ne lui dit rien.
    ///
    /// Les langues se nomment dans leur propre langue : c'est l'usage, et
    /// c'est le seul moyen d'être lu par qui ne comprend pas celle qui est
    /// affichée en ce moment.
    /// </summary>
    public IReadOnlyList<TextChoice> LanguageChoices { get; } =
    [
        new("Windows", string.Empty),
        new("English", "en"),
        new("Français", "fr"),
        new("Español", "es"),
    ];

    [ObservableProperty]
    private string _language = string.Empty;

    /// <summary>
    /// Vrai quand la langue choisie n'est pas celle qui est affichée, tant que
    /// l'application n'a pas été relancée. Les fenêtres lisent leurs textes à
    /// la construction : les retraduire à chaud demanderait de toutes les
    /// rebâtir, pour un réglage qu'on touche une fois.
    /// </summary>
    [ObservableProperty]
    private bool _languageRestartNeeded;

    /// <summary>
    /// La langue effectivement affichée, posée au démarrage et inchangée
    /// depuis : c'est elle que les fenêtres portent.
    /// </summary>
    private readonly string _languageInForce = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    partial void OnLanguageChanged(string value)
    {
        if (_loading)
        {
            return;
        }

        // Le message se levait à tout changement et ne redescendait jamais :
        // reprendre la langue du départ, donc renoncer, laissait pourtant
        // l'invitation à relancer, pour une application qui n'avait plus rien à
        // changer.
        //
        // Ce qui compte n'est pas qu'on ait touché au réglage, mais que le
        // choix s'écarte de ce qui est affiché. « Suivre Windows » est résolu
        // comme au démarrage, si bien que le choisir alors que Windows parle
        // déjà cette langue ne demande rien non plus.
        LanguageRestartNeeded = AppLanguage.NeedsRestart(
            value, CultureInfo.InstalledUICulture.Name, _languageInForce);

        _ = SaveAsync(settings => settings.Language = AppLanguage.Serves(value) ? value : string.Empty);
    }

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

    partial void OnUpdatesAutomaticChanged(bool value)
    {
        // Le garde-fou manquait ici, seul de tous les réglages : lire les
        // préférences réécrivait aussitôt le fichier avec ce qu'on venait d'y
        // trouver. Sans conséquence visible, mais c'est une écriture pour rien
        // à chaque ouverture du panneau.
        if (_loading)
        {
            return;
        }

        _ = SaveAsync(settings => settings.UpdatesAutomatic = value);
    }

    /// <summary>
    /// Vrai quand fermer une fenêtre de jeu arrête aussi le jeu sur le
    /// téléphone. Faux, le jeu survit à sa fenêtre et on la rouvre sans avoir
    /// à se reconnecter.
    /// </summary>
    [ObservableProperty]
    private bool _stopAppOnClose = true;

    partial void OnStopAppOnCloseChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _ = SaveAsync(settings => settings.StopAppOnClose = value);
    }

    /// <summary>
    /// Vrai quand le clavier est présenté au téléphone comme un clavier
    /// physique branché. Le clavier virtuel de certaines surcouches avale les
    /// caractères, et la fenêtre répond alors à la souris sans rien écrire.
    /// </summary>
    [ObservableProperty]
    private bool _simulatedPhysicalKeyboard;

    partial void OnSimulatedPhysicalKeyboardChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Le mode clavier est un argument de démarrage de scrcpy : sans
        // rouvrir, le réglage paraîtrait mort jusqu'à la session suivante.
        _ = ApplyStartupSettingAsync(() => _settings.SetSimulatedPhysicalKeyboardAsync(value));
    }

    /// <summary>
    /// Fait écrire à scrcpy sa cadence dans le journal.
    ///
    /// Diagnostic, pas confort : c'est la réponse à « ça saccade ». Zéro image
    /// par seconde n'est pas un défaut, scrcpy n'encodant que ce qui change.
    /// </summary>
    [ObservableProperty]
    private bool _fluidityDiagnostics;

    partial void OnFluidityDiagnosticsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // C'est un argument de démarrage de scrcpy, comme le mode clavier :
        // sans rouvrir, le réglage paraîtrait mort jusqu'à la session
        // suivante.
        _ = ApplyStartupSettingAsync(() => _settings.SetFluidityDiagnosticsAsync(value));
    }

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

    public string Disclaimer =>
        Strings.Get("Disclaimer");

    /// <summary>Charge l'état des réglages dans la fenêtre.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;

        try
        {
            var settings = await _settings.GetAsync(cancellationToken).ConfigureAwait(true);

            // Dès la première lecture : une faute peut survenir avant toute
            // écriture, et le rapport doit déjà savoir quoi biffer.
            RememberNames(settings);

            GameAnchor = settings.GameAnchor;

            var presets = await _settings.GetSizePresetsAsync(cancellationToken).ConfigureAwait(true);

            Quality = settings.Quality;
            Zoom = settings.GameZoom;
            UpdatesAutomatic = settings.UpdatesAutomatic;
            StopAppOnClose = settings.StopAppOnClose;
            SimulatedPhysicalKeyboard = settings.SimulatedPhysicalKeyboard;
            FluidityDiagnostics = settings.FluidityDiagnostics;
            Language = settings.Language;
            AudioEnabled = settings.AudioEnabled;
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
        RefreshConnection();
    }

    private readonly IUsbEnumerationInspector _usb;

    /// <summary>Ce que l'application peut dire de la liaison, en une phrase.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCableHelp))]
    [NotifyPropertyChangedFor(nameof(ConnectionIsHealthy))]
    [NotifyPropertyChangedFor(nameof(ShowsConnection))]
    private ConnectionVerdict _connection = ConnectionVerdict.NoDevice;

    /// <summary>
    /// Faux tant que la liaison n'a jamais été examinée.
    ///
    /// Sans lui, le bloc partait de « aucun téléphone connecté » et l'affichait
    /// à l'ouverture du panneau, avant même le premier sondage, pour se
    /// dédire une seconde plus tard. Ce n'était pas un état de la liaison,
    /// c'était l'absence de mesure présentée comme un constat.
    ///
    /// Ne rien afficher est la seule réponse honnête à « je ne sais pas
    /// encore » : un bloc vide n'apprend rien, un bloc qui se trompe défait
    /// la confiance qu'on accorde aux suivants.
    /// </summary>
    private bool _connectionKnown;

    /// <summary>La phrase elle-même.</summary>
    public string ConnectionMessage => ConnectionCheck.Describe(Connection);

    /// <summary>Vrai quand le téléphone répond : le bloc se fait alors discret.</summary>
    public bool ConnectionIsHealthy => Connection == ConnectionVerdict.Ready;

    /// <summary>
    /// Vrai quand le bloc a quelque chose à dire : la liaison a été examinée,
    /// et ce qu'on y a trouvé demande une explication.
    /// </summary>
    public bool ShowsConnection => _connectionKnown && ConnectionCheck.NeedsExplaining(Connection);

    /// <summary>Vrai quand la fiche du câble a quelque chose à apporter.</summary>
    public bool ShowsCableHelp => ConnectionCheck.NeedsCableHelp(Connection);

    /// <summary>
    /// Relit le verdict.
    ///
    /// L'avis de Windows n'est demandé que lorsqu'ADB ne voit rien : c'est le
    /// seul cas où il apporte quelque chose, et une énumération à chaque
    /// balayage coûterait sans rien rendre.
    /// </summary>
    private void RefreshConnection()
    {
        var states = Instances.DeviceStates;

        var faults = states.Contains(AdbDeviceState.Device)
            ? []
            : _usb.Faults();

        var avant = Connection;
        var connuAvant = _connectionKnown;

        Connection = ConnectionCheck.Of(states, faults, toolsReady: true);
        _connectionKnown = true;

        OnPropertyChanged(nameof(ConnectionMessage));

        // Le premier examen ne change pas forcément le verdict, mais il change
        // le droit de l'afficher.
        if (!connuAvant)
        {
            OnPropertyChanged(nameof(ShowsConnection));
        }

        if (Connection != avant)
        {
            Serilog.Log.Information(
                "Liaison : {Verdict} ({Appareils} appareil(s) vu(s), {Defauts} défaut(s) USB).",
                Connection,
                states.Count,
                faults.Count);
        }
    }

    [RelayCommand]
    private void SetAnchor(WindowAnchor anchor) => GameAnchor = anchor;

    /// <summary>
    /// Écrit les réglages dans un fichier, pour les emporter ailleurs.
    ///
    /// Le fichier lui-même, tel qu'il est sur le disque : ce qui se relit est
    /// exactement ce qui a été écrit.
    /// </summary>
    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        var path = _dialogs.AskWhereToSave(
            SettingsBackup.SuggestedFileName,
            Strings.Get("SettingsFileFilter"),
            Strings.Get("ExportSettings"));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var content = await _settings.ExportAsync().ConfigureAwait(true);

            await File.WriteAllTextAsync(path, content).ConfigureAwait(true);

            _dialogs.ShowInformation(Strings.Format("SettingsExported", path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowWarning(exception.Message);
        }
    }

    /// <summary>
    /// Reprend des réglages écrits ailleurs, après confirmation.
    ///
    /// Le geste remplace tout : les comptes, les profils, les raccourcis. Il
    /// se confirme donc, et il refuse franchement un fichier qu'il ne sait pas
    /// lire plutôt que d'en appliquer la moitié.
    /// </summary>
    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        var path = _dialogs.AskWhichFileToRead(
            Strings.Get("SettingsFileFilter"),
            Strings.Get("ImportSettings"));

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_dialogs.Confirm(Strings.Get("ImportSettingsConfirm"), Strings.Get("ImportSettings")))
        {
            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path).ConfigureAwait(true);

            var verdict = await _settings.ImportAsync(content).ConfigureAwait(true);

            var message = verdict switch
            {
                BackupVerdict.Usable => Strings.Get("SettingsImported"),
                BackupVerdict.TooNew => Strings.Get("SettingsFileTooNew"),
                BackupVerdict.Foreign => Strings.Get("SettingsFileForeign"),
                _ => Strings.Get("SettingsFileUnreadable"),
            };

            if (verdict == BackupVerdict.Usable)
            {
                _dialogs.ShowInformation(message);
            }
            else
            {
                _dialogs.ShowWarning(message);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowWarning(exception.Message);
        }
    }

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
            Instances.Problem = Strings.Get("NothingToRearrange");
        }
    }

    /// <summary>Range les fenêtres côte à côte, l'active à droite.</summary>
    [RelayCommand]
    private async Task TileAsync()
    {
        var placed = await _launcher.TileAsync().ConfigureAwait(true);

        if (placed == 0)
        {
            Instances.Problem = Strings.Get("NothingToTile");
        }
    }

    /// <summary>
    /// Donne au rapport les noms que la personne a choisis, pour qu'il les
    /// biffe. Ni les noms de comptes ni ceux des profils ne se devinent par un
    /// motif, et rien n'empêche quelqu'un d'y mettre son pseudonyme de jeu.
    /// </summary>
    private void RememberNames(AppSettingsDocument document) =>
        _reporter.Names =
        [
            .. document.LaunchProfiles.Select(p => p.Name),
            .. document.Instances.Select(i => i.UserName),
            .. document.Instances.Select(i => i.CustomName ?? string.Empty),
        ];

    /// <summary>
    /// Vrai quand une faute a été relevée sans être montrée. La ligne qui
    /// l'annonce se laisse ignorer : ce n'est pas une boîte, et une faute qui
    /// se répète ne bloque donc rien.
    /// </summary>
    public bool HasIncident => _reporter.Incidents > 0;

    /// <summary>
    /// Ouvre la fenêtre de signalement, avec ou sans incident derrière elle.
    ///
    /// Un problème n'est pas toujours un plantage : une fenêtre qui ne s'ouvre
    /// pas, un compte qui manque, un guide qui ne charge pas se racontent aussi,
    /// et il faut pouvoir le faire sans attendre une faute.
    /// </summary>
    [RelayCommand]
    private void ReportProblem()
    {
        var title = Strings.Get("ReportProblem");

        new Windows.ProblemWindow(_dialogs, title, _reporter.Compose(title), detail: null)
        {
            Logs = _paths.LogsDirectory,
        }.ShowDialog();
    }

    /// <summary>Ouvre le suivi de quêtes, ou le referme s'il est déjà là.</summary>
    [RelayCommand]
    private void Quests() => QuestsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Demandé depuis le bouton d'outil. La fenêtre est construite par
    /// l'application, pas par ce modèle : il n'a pas à connaître les fenêtres.
    /// </summary>
    public event EventHandler? QuestsRequested;

    /// <summary>Ouvre l'Almanax du jour.</summary>
    [RelayCommand]
    private void Almanax() => AlmanaxRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Demandé depuis le bouton d'outil, même raison.</summary>
    public event EventHandler? AlmanaxRequested;

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

    partial void OnCustomBitsPerPixelChanged(double value) => SaveCustomQuality();

    partial void OnCustomCodecChanged(string value) => SaveCustomQuality();

    /// <summary>
    /// Reprend les quatre valeurs fines depuis les réglages. Appelée sous le
    /// garde-fou de chargement, comme la qualité et la distance.
    /// </summary>
    private void ReadCustomQuality(AppSettingsDocument document)
    {
        var custom = document.CustomQuality.Sanitized();

        CustomHeight = custom.MaximumDisplayHeight;
        CustomFps = custom.MaxFps;
        CustomBitsPerPixel = custom.BitsPerPixel;
        CustomCodec = custom.VideoCodec;
    }

    /// <summary>
    /// Retient les quatre valeurs fines, et ne rouvre les fenêtres que si le
    /// palier personnalisé est celui en vigueur.
    ///
    /// Les régler alors qu'un autre palier est coché ne change rien à l'image :
    /// rouvrir dans ce cas ferait clignoter toutes les fenêtres pour rien.
    /// </summary>
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
            BitsPerPixel = CustomBitsPerPixel,
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
