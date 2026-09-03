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
    private bool _fitting;
    private bool _closing;

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
    public bool Attach(string key, string title, string? iconPath, nint window, double aspect)
    {
        if (Find(key) is not null || window == 0)
        {
            return false;
        }

        if (!_windows.Dock(window, Handle))
        {
            return false;
        }

        var tab = new GameTabViewModel(key, title, iconPath, window, aspect);
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

        CloseIfEmpty();

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

    /// <summary>
    /// Ne garde que les onglets de cette liste, et ressort les autres.
    ///
    /// Une session peut mourir sans passer par nous : la fenêtre du jeu fermée
    /// à la main, le téléphone débranché. L'onglet restait alors dans la barre
    /// et désignait une fenêtre disparue ; pire, rouvrir le compte ne le
    /// relogeait plus, l'onglet fantôme faisant croire qu'il y était déjà.
    /// </summary>
    public void KeepOnly(IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in Items.Select(t => t.Key).Where(k => !keys.Contains(k)).ToList())
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

    /// <summary>
    /// Referme le cadre dès qu'il ne loge plus rien.
    ///
    /// Un cadre vide n'a rien à montrer et ne dit pas ce qu'il attend. Sa
    /// position n'est pas retenue de toute façon : le rouvrir ne coûte rien.
    /// </summary>
    private void CloseIfEmpty()
    {
        if (Items.Count > 0 || _closing)
        {
            return;
        }

        _closing = true;
        Close();
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

        Fit(tab);
        Place(tab);
        Retitle();
    }

    private void OnHostResized(object sender, SizeChangedEventArgs e)
    {
        if (Items.FirstOrDefault(t => t.IsSelected) is { } tab)
        {
            Fit(tab);
            Place(tab);
        }
    }

    /// <summary>
    /// La zone d'accueil, en pixels et dans les coordonnées de la zone client
    /// du cadre, c'est-à-dire celles qu'attend une fenêtre logée.
    ///
    /// Les points sont demandés à WPF plutôt que calculés : convertir soi-même
    /// les unités indépendantes de la densité s'est révélé faux sur un écran à
    /// cent cinquante pour cent, et la fenêtre se posait cent pixels trop bas
    /// et trop à droite.
    /// </summary>
    private ScreenRect? HostArea()
    {
        if (Handle == 0 || !IsVisible || Accueil.ActualWidth <= 0 || Accueil.ActualHeight <= 0)
        {
            return null;
        }

        var racine = PointToScreen(new Point(0, 0));
        var coin = Accueil.PointToScreen(new Point(0, 0));
        var fin = Accueil.PointToScreen(new Point(Accueil.ActualWidth, Accueil.ActualHeight));

        return new ScreenRect(
            (int)Math.Round(coin.X - racine.X),
            (int)Math.Round(coin.Y - racine.Y),
            (int)Math.Round(fin.X - coin.X),
            (int)Math.Round(fin.Y - coin.Y));
    }

    /// <summary>
    /// Donne au cadre la forme de l'image.
    ///
    /// scrcpy verrouille le rapport de ce qu'il rend : une zone d'accueil d'une
    /// autre forme lui laisse une bande noire, que le centrage se contentait de
    /// répartir de part et d'autre. En donnant au cadre le rapport du jeu, il
    /// n'y a plus de bande à répartir. C'est déjà la règle des fenêtres libres,
    /// dans <see cref="Core.Windows.WindowManagerService.EnforceAspect"/>.
    /// </summary>
    private void Fit(GameTabViewModel tab)
    {
        if (_fitting
            || tab.Aspect <= 0
            || WindowState != WindowState.Normal
            || HostArea() is not { } zone
            || zone.Width <= 0
            || zone.Height <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);

        // L'encombrement du cadre : bordures, barre de titre et barre
        // d'onglets réunies. Il ne change pas avec la taille, ce qui permet de
        // raisonner sur la seule zone de jeu.
        var chromeH = Height - (zone.Height / dpi.DpiScaleY);
        var chromeW = Width - (zone.Width / dpi.DpiScaleX);

        var largeurJeu = zone.Width / dpi.DpiScaleX;
        var voulue = (largeurJeu / tab.Aspect) + chromeH;

        // Deux pixels de tolérance : l'arrondi du rapport ne justifie pas de
        // redimensionner la fenêtre à chaque passage, ce qui la ferait
        // trembler sans jamais se poser.
        if (Math.Abs(voulue - Height) <= 2)
        {
            return;
        }

        var place = WorkAreaHeight(dpi);

        _fitting = true;

        try
        {
            if (voulue <= place)
            {
                Height = voulue;
                return;
            }

            // Plus de place en hauteur : c'est la largeur qui cède. Rogner
            // encore la hauteur donnerait un cadre écrasé, et il a déjà été
            // trouvé trop court une fois.
            Height = place;
            Width = Math.Max(MinWidth, ((place - chromeH) * tab.Aspect) + chromeW);
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>
    /// Hauteur utilisable de l'écran qui porte le cadre, barre des tâches
    /// exclue, en unités de WPF.
    /// </summary>
    private double WorkAreaHeight(DpiScale dpi)
    {
        var monitors = _windows.GetMonitors();

        if (monitors.Count == 0 || _windows.GetWindowRect(Handle) is not { } outer)
        {
            return SystemParameters.WorkArea.Height;
        }

        var ecran = WindowLayoutCalculator.ChooseMonitor(monitors, outer.CenterX, outer.CenterY);

        return ecran.WorkArea.Height / dpi.DpiScaleY;
    }

    /// <summary>Pose la fenêtre logée sur toute la zone d'accueil.</summary>
    private void Place(GameTabViewModel tab)
    {
        if (HostArea() is not { } zone || zone.Width <= 0 || zone.Height <= 0)
        {
            return;
        }

        _windows.MoveWindow(tab.Window, zone);

        // Filet : le cadre a normalement déjà pris la forme de l'image, mais
        // il reste l'arrondi, et le cas où il est agrandi ou trop petit pour
        // s'y conformer. La fenêtre ressort alors plus petite que demandé et
        // collée en haut à gauche ; on la recentre, faute de quoi le reste
        // noir se retrouve tout entier d'un seul côté.
        if (_windows.GetWindowRect(tab.Window) is not { } pris
            || (pris.Width >= zone.Width && pris.Height >= zone.Height))
        {
            return;
        }

        _windows.MoveWindow(
            tab.Window,
            new ScreenRect(
                zone.X + ((zone.Width - pris.Width) / 2),
                zone.Y + ((zone.Height - pris.Height) / 2),
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
        _closing = true;
        DetachAll();

        base.OnClosing(e);
    }
}
