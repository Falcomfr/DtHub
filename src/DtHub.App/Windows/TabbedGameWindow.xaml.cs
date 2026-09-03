using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using DtHub.App.ViewModels;
using DtHub.Core.Windows;

namespace DtHub.App.Windows;

/// <summary>
/// Le cadre à onglets : plusieurs fenêtres de jeu logées dans un seul châssis,
/// dont une seule paraît à la fois.
///
/// Les fenêtres ne sont ni recréées ni redessinées ici : elles sont arrimées
/// telles quelles par <see cref="IWindowController.Dock"/>, et le cadre ne fait
/// que les placer et les montrer. Mesuré sur une vraie session, une fenêtre
/// scrcpy arrimée continue de rendre l'image et reçoit le pointeur.
/// </summary>
public partial class TabbedGameWindow : Window
{
    private readonly IWindowController _windows;

    private GameTabViewModel? _pressed;
    private Point _origin;

    public TabbedGameWindow(IWindowController windows)
    {
        _windows = windows;

        InitializeComponent();

        Tabs.ItemsSource = Items;
        Items.CollectionChanged += (_, _) => Retitle();
    }

    /// <summary>Les onglets, dans l'ordre où ils paraissent.</summary>
    public ObservableCollection<GameTabViewModel> Items { get; } = [];

    /// <summary>Signalé quand l'utilisateur a réordonné les onglets.</summary>
    public event EventHandler<(string Moved, string Onto, bool Before)>? Reordered;

    /// <summary>Handle du cadre, une fois la fenêtre créée.</summary>
    public nint Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Loge une fenêtre de jeu et lui ajoute son onglet. Sans effet si elle
    /// s'y trouve déjà.
    /// </summary>
    public bool Attach(string key, string title, string? iconPath, nint window)
    {
        if (Find(key) is not null || window == 0)
        {
            return false;
        }

        if (!_windows.Dock(window, Handle))
        {
            return false;
        }

        var tab = new GameTabViewModel(key, title, iconPath, window);
        tab.PropertyChanged += OnTabChanged;
        Items.Add(tab);

        // Le premier arrivé se montre : un cadre ouvert sur du vide n'aurait
        // aucun sens.
        Select(Items.Count == 1 ? tab : Items.First(t => t.IsSelected));

        return true;
    }

    /// <summary>Ressort une fenêtre du cadre et lui rend son état d'avant.</summary>
    public bool Detach(string key)
    {
        if (Find(key) is not { } tab)
        {
            return false;
        }

        tab.PropertyChanged -= OnTabChanged;
        Items.Remove(tab);
        _windows.SetVisible(tab.Window, true);
        _windows.Undock(tab.Window);

        if (tab.IsSelected && Items.Count > 0)
        {
            Select(Items[0]);
        }

        return true;
    }

    /// <summary>Ressort toutes les fenêtres. Appelé à la fermeture.</summary>
    public void DetachAll()
    {
        foreach (var key in Items.Select(t => t.Key).ToList())
        {
            Detach(key);
        }
    }

    /// <summary>Vrai si ce compte est logé ici.</summary>
    public bool Holds(string key) => Find(key) is not null;

    /// <summary>Montre l'onglet de ce compte, s'il est logé.</summary>
    public void Show(string key)
    {
        if (Find(key) is { } tab)
        {
            Select(tab);
        }
    }

    /// <summary>Range les onglets dans cet ordre, celui de la liste des comptes.</summary>
    public void Reorder(IReadOnlyList<string> keys)
    {
        for (var wanted = 0; wanted < keys.Count; wanted++)
        {
            var present = Items.ToList().FindIndex(t => string.Equals(t.Key, keys[wanted], StringComparison.Ordinal));

            if (present >= 0 && present != wanted && wanted < Items.Count)
            {
                Items.Move(present, wanted);
            }
        }
    }

    private GameTabViewModel? Find(string key) =>
        Items.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameTabViewModel.IsSelected)
            && sender is GameTabViewModel { IsSelected: true } tab)
        {
            Select(tab);
        }
    }

    /// <summary>
    /// Montre une fenêtre et cache les autres.
    ///
    /// Cacher plutôt que détacher : revenir sur un onglet doit être immédiat,
    /// et une fenêtre détachée puis rattachée perdrait sa place à chaque fois.
    /// </summary>
    private void Select(GameTabViewModel tab)
    {
        foreach (var other in Items)
        {
            other.IsSelected = ReferenceEquals(other, tab);
            _windows.SetVisible(other.Window, other.IsSelected);
        }

        Place(tab);
        Retitle();
    }

    private void OnHostResized(object sender, SizeChangedEventArgs e)
    {
        if (Items.FirstOrDefault(t => t.IsSelected) is { } tab)
        {
            Place(tab);
        }
    }

    /// <summary>
    /// Pose la fenêtre logée sur toute la zone d'accueil.
    ///
    /// Les coordonnées sont converties en pixels : WPF raisonne en unités
    /// indépendantes de la densité, et <c>SetWindowPos</c> non. Sur un écran à
    /// cent cinquante pour cent, l'oublier laisserait la fenêtre aux deux tiers
    /// de la zone.
    /// </summary>
    private void Place(GameTabViewModel tab)
    {
        if (Handle == 0 || Accueil.ActualWidth <= 0)
        {
            return;
        }

        // La zone client est demandée à Windows, non calculée : convertir
        // soi-même les unités de WPF en pixels s'est révélé faux sur un écran
        // à cent cinquante pour cent, et la fenêtre se posait cent pixels trop
        // bas et trop à droite.
        if (_windows.GetClientRect(Handle) is not { } client)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var barre = (int)Math.Round(Tabs.ActualHeight + 9 * dpi.DpiScaleY);

        var y = Math.Min(barre, client.Height);
        var hauteur = Math.Max(1, client.Height - y);

        _windows.MoveWindow(tab.Window, new ScreenRect(0, y, client.Width, hauteur));

        // scrcpy verrouille le rapport de son image : la fenêtre ressort donc
        // plus petite que demandé, et collée en haut à gauche. On la recentre
        // sur ce qu'elle a réellement pris, faute de quoi la bande noire se
        // retrouve tout entière d'un seul côté.
        if (_windows.GetWindowRect(tab.Window) is not { } pris
            || (pris.Width >= client.Width && pris.Height >= hauteur))
        {
            return;
        }

        _windows.MoveWindow(
            tab.Window,
            new ScreenRect(
                (client.Width - pris.Width) / 2,
                y + ((hauteur - pris.Height) / 2),
                pris.Width,
                pris.Height));
    }

    private void Retitle() =>
        Title = Items.FirstOrDefault(t => t.IsSelected) is { } tab
            ? $"{Core.ProductInfo.Name}  ·  {tab.Title}"
            : $"{Core.ProductInfo.Name}  ·  onglets";

    // Glissement d'un onglet, sur le modèle de la liste des comptes.

    private void OnTabPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = (sender as FrameworkElement)?.DataContext as GameTabViewModel;
        _origin = e.GetPosition(this);
    }

    private void OnTabMoved(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var bouge = e.GetPosition(this) - _origin;

        if (Math.Abs(bouge.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(bouge.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var porte = _pressed;
        _pressed = null;

        DragDrop.DoDragDrop(this, porte, DragDropEffects.Move);
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is not GameTabViewModel onto
            || e.Data.GetData(typeof(GameTabViewModel)) is not GameTabViewModel moved
            || ReferenceEquals(moved, onto))
        {
            return;
        }

        // Avant ou après, selon le côté du survol : le même geste que dans la
        // liste des comptes, en horizontal.
        var avant = e.GetPosition((IInputElement)sender).X < ((FrameworkElement)sender).ActualWidth / 2;

        Reordered?.Invoke(this, (moved.Key, onto.Key, avant));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Les fenêtres logées ne doivent pas mourir avec le cadre : elles
        // reprennent leur vie de fenêtres libres.
        DetachAll();

        base.OnClosing(e);
    }
}
