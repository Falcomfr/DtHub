using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Hotkeys;
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

    public ConfiguratorWindow(ConfiguratorViewModel viewModel, GameLauncher launcher)
    {
        _viewModel = viewModel;
        _launcher = launcher;

        InitializeComponent();
        DataContext = viewModel;

        // La capture doit voir les touches avant que WPF ne les interprète,
        // sinon Tab changerait le focus au lieu d'être enregistrée.
        PreviewKeyDown += OnPreviewKeyDown;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            _poll.Start();
        };

        _poll.Tick += async (_, _) => await _viewModel.PollAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Handle natif, retenu une fois pour toutes. Il est consulté depuis le
    /// fil des raccourcis, qui n'a pas le droit d'interroger une fenêtre WPF.
    /// </summary>
    public nint Handle { get; private set; }

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
    private void OnQuit(object sender, RoutedEventArgs e)
    {
        _quitting = true;
        Application.Current.Shutdown();
    }

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Ouvre la fenêtre d'ajout, puis rafraîchit la liste.</summary>
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

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.CapturingRow is null)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _viewModel.CancelCaptureCommand.Execute(null);
            return;
        }

        // Tant que seule une touche de modification est enfoncée, on attend la
        // suite plutôt que de refuser la saisie.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        var modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        await _viewModel.ApplyCapturedHotkeyAsync(KeyInterop.VirtualKeyFromKey(key), modifiers)
            .ConfigureAwait(true);
    }
}
