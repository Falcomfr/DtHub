using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Settings;
using DtHub.Core.Windows;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// Le configurateur : une fenêtre flottante, au-dessus des fenêtres de jeu,
/// que le raccourci affiche ou masque. La fermer ne quitte pas l'application.
/// </summary>
public partial class ConfiguratorWindow : Window
{
    private readonly ConfiguratorViewModel _viewModel;
    private readonly GameLauncher _launcher;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(3) };

    private bool _quitting;

    public ConfiguratorWindow(
        ConfiguratorViewModel viewModel,
        GameLauncher launcher,
        WindowPlacements placements)
    {
        _viewModel = viewModel;
        _launcher = launcher;
        _placements = placements;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);

        // Le sondage suit la visibilité, et non le chargement. Il ne s'arrêtait
        // jamais : la croix masque au lieu de fermer, donc OnClosing rendait la
        // main avant le Stop, et Toggle masque sans passer par là du tout. Un
        // panneau caché continuait donc d'interroger le téléphone.
        //
        // Ce que cela coûtait, mesuré : un tic déclenche jusqu'à sept appels à
        // adb.exe, à cinquante-cinq millisecondes pièce. Sur une soirée de
        // quatre heures avec le panneau masqué, cela fait des milliers de
        // lancements pour une fenêtre que personne ne regarde, chacun
        // réveillant le gestionnaire de paquets du téléphone.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is not true)
            {
                _poll.Stop();
                return;
            }

            _poll.Start();

            // Et tout de suite, sans attendre le premier tic : celui-ci vient
            // trois secondes plus tard, et pendant ces trois secondes le
            // panneau n'aurait rien à dire de la liaison alors que c'est la
            // première chose qu'on vient y lire.
            await PollSafelyAsync().ConfigureAwait(true);
        };

        _poll.Interval = _viewModel.Instances.PollInterval;
        _poll.Tick += async (_, _) =>
        {
            // La cadence suit le palier de qualité, qui se change en cours de
            // route : posée une fois pour toutes au démarrage, elle gardait sa
            // valeur d'origine jusqu'à la prochaine exécution, et le réglage
            // n'avait aucun effet.
            if (_poll.Interval != _viewModel.Instances.PollInterval)
            {
                _poll.Interval = _viewModel.Instances.PollInterval;
            }

            await PollSafelyAsync().ConfigureAwait(true);
        };

        // Le temps mort après un branchement absorbe la rafale : Windows
        // diffuse le changement plusieurs fois pendant qu'il énumère, et il
        // faut de toute façon le laisser finir avant qu'ADB ait quelque chose
        // à voir. Une seconde, une seule interrogation.
        _afterDeviceChange.Tick += async (_, _) =>
        {
            _afterDeviceChange.Stop();

            Log.Information("Changement de périphériques signalé par Windows : balayage.");

            await PollSafelyAsync().ConfigureAwait(true);
        };
    }

    private readonly DispatcherTimer _afterDeviceChange = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>
    /// Un balayage borné, et c'est le rythme qui l'exige. Le balayage n'attrape
    /// que les fautes d'ADB ; toute autre s'échapperait d'une lambda
    /// « async void », atteindrait le garde-fou du répartiteur, et ouvrirait
    /// une fenêtre d'erreur toutes les trois secondes. Une panne durable
    /// rendrait alors l'application inutilisable par son propre message. Le
    /// journal la retient, le tic suivant réessaie.
    /// </summary>
    private async Task PollSafelyAsync()
    {
        try
        {
            await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le balayage du panneau a échoué.");
        }
    }

    /// <summary>
    /// Handle natif, retenu une fois pour toutes. Il est consulté depuis le
    /// fil des raccourcis, qui n'a pas le droit d'interroger une fenêtre WPF.
    /// </summary>
    public nint Handle { get; private set; }

    private readonly WindowPlacements _placements;

    /// <summary>
    /// Vrai quand la fenêtre a retrouvé une place retenue. Le placement par
    /// défaut, qui l'écarte du bloc de jeu, s'efface alors devant elle.
    /// </summary>
    public bool Placed { get; private set; }

    /// <summary>Remet la fenêtre où elle était, et dit si elle l'a été.</summary>
    public bool RestorePlacement(AppSettingsDocument document)
    {
        Placed = _placements.Restore(this, WindowPlacements.Configurator, document);

        return Placed;
    }

    /// <summary>Retient où est la fenêtre.</summary>
    public Task SavePlacementAsync() =>
        _placements.SaveAsync(this, WindowPlacements.Configurator);

    /// <summary>
    /// Branche un téléphone, et le balayage part tout de suite.
    ///
    /// Le sondage périodique suit la visibilité du panneau, pour de bonnes
    /// raisons dites plus haut : un tic déclenche jusqu'à sept lancements
    /// d'adb.exe, et des milliers par soirée pour une fenêtre que personne ne
    /// regarde. Mais panneau masqué, plus rien ne regardait non plus : un câble
    /// branché n'était vu qu'au retour du panneau. Relevé sur un cas réel, un
    /// branchement n'a laissé aucune ligne de journal.
    ///
    /// Ce message-ci ne coûte rien tant qu'il ne se passe rien : Windows le
    /// diffuse, nous n'interrogeons personne. Il rend donc au panneau masqué
    /// exactement ce qui lui manquait, sans reprendre ce que la mesure avait
    /// fait retirer.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && (int)wParam == DbtDevNodesChanged)
        {
            _afterDeviceChange.Stop();
            _afterDeviceChange.Start();
        }

        return 0;
    }

    /// <summary>Affiche ou masque la fenêtre, selon son état.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    /// <summary>
    /// Pose la fenêtre dans un coin libre, à l'opposé du bloc de jeu, pour
    /// qu'elle ne recouvre pas les fenêtres du jeu.
    /// </summary>
    public void PlaceAwayFrom(WindowAnchor gameAnchor)
    {
        // Une place retenue l'emporte : elle vient d'un déplacement voulu,
        // tandis que celle-ci n'est qu'un défaut raisonnable.
        if (Placed)
        {
            return;
        }

        var work = _launcher.WorkArea();
        if (work is not { } area)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * scale.DpiScaleX);
        var height = (int)Math.Round(Height * scale.DpiScaleY);

        var rect = WindowLayoutCalculator.Place(area, width, height, WindowAnchors.Opposite(gameAnchor));

        // Une petite marge évite que la fenêtre colle au bord de l'écran.
        const int margin = 12;

        Left = (rect.X + (rect.X > area.X ? -margin : margin)) / scale.DpiScaleX;
        Top = (rect.Y + margin) / scale.DpiScaleY;
    }

    /// <summary>Ferme réellement l'application.</summary>
    /// <summary>
    /// Quitte l'application. L'état de la session est enregistré avant, et
    /// l'attente est nécessaire : l'arrêt courrait sinon contre l'écriture.
    /// </summary>
    private async void OnQuit(object sender, RoutedEventArgs e)
    {
        _quitting = true;

        await ((App)Application.Current).RequestQuitAsync().ConfigureAwait(true);
    }

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private void OnSleepHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<SleepHelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>
    /// Que faire quand l'image passe mais que rien ne répond. Le symptôme est
    /// silencieux par nature : aucune erreur n'est levée, et sans cette porte
    /// la personne n'a rien à quoi se raccrocher.
    /// </summary>
    private void OnInputHelp(object sender, RoutedEventArgs e)
    {
        var help = AppHost.Services.GetRequiredService<InputHelpWindow>();
        help.Owner = this;
        help.ShowDialog();
    }

    /// <summary>Ouvre l'éditeur de raccourcis, puis relit ce qui a changé.</summary>
    private async void OnEditHotkeys(object sender, RoutedEventArgs e)
    {
        var editor = AppHost.Services.GetRequiredService<HotkeyEditorWindow>();
        editor.Owner = this;
        editor.ShowDialog();

        await _viewModel.RefreshHotkeysAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>Ouvre la fenêtre d'ajout, puis rafraîchit la liste.</summary>
    /// <summary>
    /// Ouvre le panneau sur l'onglet des appareils. Employé au premier
    /// lancement, où c'est le seul endroit qui ait quelque chose à dire.
    /// </summary>
    public void ShowDevices()
    {
        Show();
        Activate();

        TabDevices.IsChecked = true;
    }

    /// <summary>
    /// Ouvre la fenêtre d'association, comme le ferait le bouton.
    ///
    /// Différée : appelée pendant le démarrage, elle bloquerait la suite sur sa
    /// boucle modale, et le suivi de quêtes comme la mise à jour attendraient
    /// qu'on ait fini d'associer un téléphone.
    /// </summary>
    public void BeginPairing() =>
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => OnAddDevice(this, new RoutedEventArgs()));

    private async void OnAddDevice(object sender, RoutedEventArgs e)
    {
        var dialog = AppHost.Services.GetRequiredService<AddDeviceWindow>();
        dialog.Owner = this;
        dialog.ShowDialog();

        await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Message que Windows diffuse quand l'arborescence des périphériques
    /// change. Il arrive aux fenêtres de premier niveau sans inscription
    /// préalable, et une fenêtre masquée le reçoit comme les autres.
    /// </summary>
    private const int WmDeviceChange = 0x0219;

    private const int DbtDevNodesChanged = 0x0007;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // La croix masque, elle ne quitte pas : le jeu continue de tourner.
        if (!_quitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _poll.Stop();
        base.OnClosing(e);
    }
}
