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

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            _poll.Start();
        };

        _poll.Interval = _viewModel.Instances.PollInterval;
        _poll.Tick += async (_, _) => await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;
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
