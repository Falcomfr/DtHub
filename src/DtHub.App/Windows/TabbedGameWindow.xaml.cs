using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

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
    /// <summary>Windows demande sa taille à une fenêtre qu'on étire.</summary>
    private const int WmSizing = 0x0214;

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

    /// <summary>
    /// Handle natif du cadre, retenu une fois pour toutes.
    ///
    /// Il était recalculé à chaque lecture, ce qui interroge une fenêtre WPF et
    /// n'est permis que sur son fil. Le configurateur et la fenêtre des guides
    /// avaient déjà été corrigés ainsi ; celui-ci avait été oublié, et il est
    /// désormais consulté depuis le guet du premier plan, qui ne vit pas sur ce
    /// fil. C'est exactement le chemin de la faute qui s'était répétée deux cent
    /// soixante-quatre fois.
    /// </summary>
    public nint Handle { get; private set; }

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

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Settle);

        return true;
    }

    /// <summary>Signalé à la fermeture du cadre, avec les comptes qu'il logeait.</summary>
    public event EventHandler<IReadOnlyList<string>>? CloseRequested;

    /// <summary>
    /// Ressort une fenêtre du cadre et lui rend son état d'avant.
    /// </summary>
    /// <param name="key">Le compte à ressortir.</param>
    /// <param name="reveal">
    /// Faux pour la laisser masquée. Sert à la fermeture du cadre : la fenêtre
    /// doit quitter le cadre pour ne pas mourir avec lui, mais la montrer une
    /// fraction de seconde avant de la fermer ne servirait qu'à clignoter.
    /// </param>
    public bool Detach(string key, bool reveal = true)
    {
        if (Find(key) is not { } tab)
        {
            return false;
        }

        tab.PropertyChanged -= OnTabChanged;
        Items.Remove(tab);

        if (reveal)
        {
            _windows.SetVisible(tab.Window, true);
        }

        _windows.Undock(tab.Window);

        if (tab.IsSelected && Items.Count > 0)
        {
            Select(Items[0]);
        }

        CloseIfEmpty();

        return true;
    }

    /// <summary>Ressort toutes les fenêtres.</summary>
    public void DetachAll(bool reveal = true)
    {
        foreach (var key in Items.Select(t => t.Key).ToList())
        {
            Detach(key, reveal);
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
        ArgumentNullException.ThrowIfNull(keys);

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
    /// position est rendue au lanceur avant de partir, qui la retient.
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

        // Le clavier ne suit pas l'onglet tout seul. Une fenêtre logée est
        // fille du cadre, donc hors d'atteinte du premier plan : sans cet
        // appel, l'image s'affiche et la souris passe, mais rien de ce qu'on
        // tape n'arrive, le collage compris puisque scrcpy colle en frappant.
        _ = _windows.GiveKeyboardFocus(tab.Window);
    }

    /// <summary>
    /// Passe à l'onglet suivant, ou au précédent. Le parcours boucle, comme
    /// celui des fenêtres libres, pour que Ctrl+Tab veuille dire la même chose
    /// dans les deux modes.
    /// </summary>
    /// <returns>Faux s'il n'y a pas de quoi tourner.</returns>
    public bool Cycle(bool forward)
    {
        if (Items.Count < 2)
        {
            return false;
        }

        var courant = Items.FirstOrDefault(t => t.IsSelected) ?? Items[0];
        var pas = forward ? 1 : -1;
        var voulu = ((Items.IndexOf(courant) + pas) % Items.Count + Items.Count) % Items.Count;

        // Cocher suffit : le changement passe par OnTabChanged, comme un clic.
        Items[voulu].IsSelected = true;

        return true;
    }

    /// <summary>
    /// Le rapport de l'onglet actif, ou <c>null</c> si le cadre est vide ou si
    /// l'afficheur n'en impose aucun.
    ///
    /// Publié pour que les commandes de géométrie puissent calculer un
    /// rectangle qui donne au jeu sa forme, comme elles le font pour une
    /// fenêtre libre.
    /// </summary>
    public double? SelectedAspect => Aspect();

    /// <summary>
    /// L'encombrement du châssis, bordures, barre de titre et barre d'onglets
    /// comprises. C'est ce qu'il faut retirer avant d'appliquer un rapport,
    /// puisque le rapport vaut pour la zone de jeu et non pour la fenêtre.
    /// </summary>
    public (int Width, int Height)? Chassis => Chrome();

    /// <summary>Vrai quand le cadre couvre l'écran entier.</summary>
    public bool IsFullscreen => _fullscreen;

    private bool _fullscreen;
    private WindowStyle _styleBefore = WindowStyle.SingleBorderWindow;
    private ResizeMode _resizeBefore = ResizeMode.CanResize;
    private ScreenRect _boundsBefore;

    /// <summary>
    /// Pose le cadre à l'endroit et à la taille demandés, en pixels du bureau.
    ///
    /// C'est le seul point d'entrée des commandes de géométrie. Rien n'est
    /// jamais appliqué à la fenêtre logée elle-même : elle est fille du cadre,
    /// ses coordonnées ne sont pas celles de l'écran, et lui rendre une
    /// bordure la doterait d'une barre de titre à l'intérieur du cadre.
    /// </summary>
    public void ApplyRect(ScreenRect outer)
    {
        if (Handle == 0 || _fullscreen || outer.Width <= 0 || outer.Height <= 0)
        {
            return;
        }

        _windows.MoveWindow(Handle, outer);

        // Deux fois, la seconde une fois la boucle de messages passée. Franchir
        // un écran d'une autre densité fait reproportionner la taille par WPF,
        // et la première pose arrive dans la densité de l'écran de départ :
        // c'est le piège déjà rencontré à la restauration d'un placement.
        //
        // La forme est reprise à ce moment-là seulement : un rectangle venu
        // d'ailleurs, celui d'une fenêtre libre qu'on empile par exemple, n'a
        // aucune raison d'avoir déjà celle du jeu.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                if (Handle == 0 || _fullscreen)
                {
                    return;
                }

                _windows.MoveWindow(Handle, outer);
                Settle();
            });
    }

    /// <summary>
    /// Redonne au cadre la forme de l'onglet montré, une fois la disposition
    /// retombée.
    ///
    /// Différé à dessein. La restauration de la place du cadre repose la taille
    /// enregistrée une seconde fois par le répartiteur, à cette même priorité,
    /// donc après le <see cref="Fit"/> de <see cref="Select"/> : le cadre
    /// gardait une forme qui ne correspondait à aucun onglet, et la fenêtre
    /// logée était simplement recentrée avec ses bandes noires. Mise en file
    /// après elle, la reprise a le dernier mot.
    /// </summary>
    private void Settle()
    {
        if (_fullscreen || Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        Fit(tab);
        Place(tab);
    }

    /// <summary>
    /// Fait couvrir un rectangle entier au cadre, châssis retiré, ou lui rend
    /// son châssis et sa place.
    ///
    /// Le style est retiré par WPF, le rectangle posé par nous. C'est le seul
    /// partage qui tienne : retirer les styles de bordure par Win32 mettrait
    /// la fenêtre en désaccord avec son propre gestionnaire de zone non
    /// cliente, qui les réécrit à la moindre occasion ; et laisser WPF poser la
    /// géométrie par <c>Maximized</c> ne couvrirait pas la barre des tâches, là
    /// où le plein écran des fenêtres libres prend les bornes entières de
    /// l'écran. L'état reste donc <c>Normal</c>, et c'est pourquoi
    /// <see cref="Fit"/> doit renoncer sur le drapeau plutôt que sur l'état.
    ///
    /// Le style d'abord, la géométrie ensuite : posée avant, elle serait
    /// reprise par le retour du châssis.
    /// </summary>
    /// <param name="on">Vrai pour couvrir, faux pour rendre.</param>
    /// <param name="target">
    /// Les bornes de l'écran en entrant, le rectangle à rendre en sortant.
    /// </param>
    public void SetFullscreen(bool on, ScreenRect target)
    {
        if (Handle == 0 || on == _fullscreen)
        {
            return;
        }

        _fullscreen = on;

        if (on)
        {
            // D'où l'on vient, retenu ici et non chez l'appelant : c'est le
            // cadre qui sait ce qu'il occupait, et l'appelant ne peut pas le
            // relire une fois la pose faite.
            _boundsBefore = _windows.GetWindowRect(Handle) ?? default;
            _styleBefore = WindowStyle;
            _resizeBefore = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        }
        else
        {
            WindowStyle = _styleBefore;
            ResizeMode = _resizeBefore;

            if (target.Width <= 0 || target.Height <= 0)
            {
                target = _boundsBefore;
            }
        }

        if (target.Width > 0 && target.Height > 0)
        {
            _windows.MoveWindow(Handle, target);
        }

        if (Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        // En sortant seulement : le cadre reprend la forme du jeu, ce qu'il ne
        // doit surtout pas faire tant qu'il couvre l'écran.
        if (!on)
        {
            Fit(tab);
        }

        Place(tab);
    }

    private void OnHostResized(object sender, SizeChangedEventArgs e)
    {
        if (Items.FirstOrDefault(t => t.IsSelected) is { } tab)
        {
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
        var end = Accueil.PointToScreen(new Point(Accueil.ActualWidth, Accueil.ActualHeight));

        return new ScreenRect(
            (int)Math.Round(coin.X - racine.X),
            (int)Math.Round(coin.Y - racine.Y),
            (int)Math.Round(end.X - coin.X),
            (int)Math.Round(end.Y - coin.Y));
    }

    /// <summary>
    /// Ce que le châssis prend autour de la zone de jeu : bordures, barre de
    /// titre et barre d'onglets. Constant, donc mesurable sur la taille
    /// courante et réutilisable pour toute autre.
    /// </summary>
    private (int Width, int Height)? Chrome()
    {
        if (_windows.GetWindowRect(Handle) is not { } outer
            || HostArea() is not { } zone
            || zone.Width <= 0
            || zone.Height <= 0)
        {
            return null;
        }

        return (outer.Width - zone.Width, outer.Height - zone.Height);
    }

    /// <summary>Le rapport de l'onglet montré, ou <c>null</c> s'il n'y en a pas.</summary>
    private double? Aspect() =>
        Items.FirstOrDefault(t => t.IsSelected) is { Aspect: > 0 } tab ? tab.Aspect : null;

    /// <summary>
    /// Garde la forme de l'image pendant qu'on étire la fenêtre : le cadre
    /// s'attrape par n'importe quel bord, comme une fenêtre de jeu libre. La
    /// règle est dans <see cref="AspectSizing"/>, où elle se vérifie sans
    /// ouvrir d'interface.
    /// </summary>
    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmSizing || Aspect() is not { } rapport || Chrome() is not { } chrome)
        {
            return 0;
        }

        var voulu = Marshal.PtrToStructure<SizingRect>(lParam);

        var corrige = AspectSizing.Constrain(
            new ScreenRect(
                voulu.Left,
                voulu.Top,
                voulu.Right - voulu.Left,
                voulu.Bottom - voulu.Top),
            (int)wParam,
            rapport,
            chrome,
            (int)Math.Round(MinWidth * VisualTreeHelper.GetDpi(this).DpiScaleX));

        voulu.Left = corrige.X;
        voulu.Top = corrige.Y;
        voulu.Right = corrige.Right;
        voulu.Bottom = corrige.Bottom;

        Marshal.StructureToPtr(voulu, lParam, false);
        handled = true;

        return 1;
    }

    /// <summary>
    /// Donne au cadre la forme de l'image, quand ce n'est pas la souris qui
    /// décide : à l'arrivée d'un onglet, ou quand on passe à un onglet dont
    /// l'afficheur n'a pas la même forme.
    /// </summary>
    private void Fit(GameTabViewModel tab)
    {
        if (_fitting
            || _fullscreen
            || tab.Aspect <= 0
            || WindowState != WindowState.Normal
            || Chrome() is not { } chrome
            || HostArea() is not { } zone)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var wanted = ((zone.Width / dpi.DpiScaleX) / tab.Aspect) + (chrome.Height / dpi.DpiScaleY);

        // Deux pixels de tolérance : l'arrondi du rapport ne justifie pas de
        // redimensionner la fenêtre à chaque passage, ce qui la ferait
        // trembler sans jamais se poser.
        if (Math.Abs(wanted - Height) <= 2)
        {
            return;
        }

        var place = WorkAreaHeight(dpi);

        _fitting = true;

        try
        {
            if (wanted <= place)
            {
                Height = wanted;
                return;
            }

            // Plus de place en hauteur : c'est la largeur qui cède. Rogner
            // encore la hauteur donnerait un cadre écrasé, et il a déjà été
            // trouvé trop court une fois.
            Height = place;
            Width = Math.Max(
                MinWidth,
                ((place - (chrome.Height / dpi.DpiScaleY)) * tab.Aspect) + (chrome.Width / dpi.DpiScaleX));
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

        var screen = WindowLayoutCalculator.ChooseMonitor(monitors, outer.CenterX, outer.CenterY);

        return screen.WorkArea.Height / dpi.DpiScaleY;
    }

    /// <summary>Pose la fenêtre logée sur toute la zone d'accueil.</summary>
    private void Place(GameTabViewModel tab)
    {
        if (HostArea() is not { } zone || zone.Width <= 0 || zone.Height <= 0)
        {
            return;
        }

        _windows.MoveWindow(tab.Window, zone);

        // Filet : le cadre a normalement déjà la forme de l'image, mais il
        // reste l'arrondi, et le cas où il est agrandi ou trop petit pour s'y
        // conformer. La fenêtre ressort alors plus petite que demandé et collée
        // en haut à gauche ; on la recentre, faute de quoi le reste noir se
        // retrouve tout entier d'un seul côté.
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

        // Seul l'écart horizontal compte : la barre est horizontale, et le
        // tremblement vertical d'un simple clic partait sinon en glissement.
        if (Math.Abs(e.GetPosition(this).X - _origin.X) < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        var porte = _pressed;
        _pressed = null;

        try
        {
            DragDrop.DoDragDrop(this, porte, DragDropEffects.Move);
        }
        finally
        {
            ClearDropHints();
        }
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (Carried(e) is null
            || (sender as FrameworkElement)?.DataContext is not GameTabViewModel onto)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        Hint(onto, Before(sender, e));
    }

    private void OnTabDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is GameTabViewModel onto)
        {
            onto.ClearDropHint();
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();

        if ((sender as FrameworkElement)?.DataContext is not GameTabViewModel onto
            || Carried(e) is not { } moved
            || ReferenceEquals(moved, onto))
        {
            return;
        }

        Rearrange(moved, onto, Before(sender, e));
    }

    // Le reste de la barre, après le dernier onglet : y lâcher range en fin
    // de liste. Sans cela il fallait viser un onglet, et lâcher à côté ne
    // faisait rien sans dire pourquoi.

    private void OnStripDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (Carried(e) is null || Items.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Move;

        Hint(Items[^1], before: false);
    }

    private void OnStripDragLeave(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();
    }

    private void OnStripDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();

        if (Carried(e) is not { } moved
            || Items.Count == 0
            || ReferenceEquals(moved, Items[^1]))
        {
            return;
        }

        Rearrange(moved, Items[^1], before: false);
    }

    private static GameTabViewModel? Carried(DragEventArgs e) =>
        e.Data.GetData(typeof(GameTabViewModel)) as GameTabViewModel;

    private static bool Before(object sender, DragEventArgs e) =>
        sender is FrameworkElement cible
        && e.GetPosition(cible).X < cible.ActualWidth / 2;

    private void Hint(GameTabViewModel onto, bool before)
    {
        foreach (var tab in Items)
        {
            tab.DropBefore = before && ReferenceEquals(tab, onto);
            tab.DropAfter = !before && ReferenceEquals(tab, onto);
        }
    }

    private void ClearDropHints()
    {
        foreach (var tab in Items)
        {
            tab.ClearDropHint();
        }
    }

    /// <summary>
    /// Range l'onglet tout de suite, puis fait suivre la liste des comptes.
    ///
    /// Tout de suite, et non au retour de l'enregistrement : le trajet par les
    /// réglages prenait un instant pendant lequel l'onglet restait où il
    /// était, et le geste paraissait n'avoir rien fait.
    /// </summary>
    private void Rearrange(GameTabViewModel moved, GameTabViewModel onto, bool before)
    {
        var depart = Items.IndexOf(moved);
        var cible = Items.IndexOf(onto);

        if (depart < 0 || cible < 0)
        {
            return;
        }

        var voulu = before ? cible : cible + 1;

        if (voulu > depart)
        {
            voulu--;
        }

        if (voulu != depart)
        {
            Items.Move(depart, voulu);
        }

        Reordered?.Invoke(this, (moved.Key, onto.Key, before));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new WindowInteropHelper(this).Handle;

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(OnWindowMessage);
        }
    }

    /// <summary>
    /// Le cadre reprend la main : le clavier retourne à l'onglet actif.
    ///
    /// Sans cela, revenir par Alt+Tab rendrait l'activation au cadre, qui n'a
    /// rien à saisir, et les frappes tomberaient à côté du jeu.
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        if (Items.FirstOrDefault(t => t.IsSelected) is not { } tab)
        {
            return;
        }

        _ = _windows.GiveKeyboardFocus(tab.Window);

        // Deux fois, et ce n'est pas une précaution en l'air : WPF rend le
        // focus à son propre arbre en traitant WM_SETFOCUS, qui arrive après
        // l'activation. Le premier appel sert au cas ordinaire, le second
        // repasse derrière lui. Désigner deux fois la même fenêtre ne coûte
        // rien, aucune file n'étant touchée.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (IsActive && Items.FirstOrDefault(t => t.IsSelected) is { } encore)
                {
                    _ = _windows.GiveKeyboardFocus(encore.Window);
                }
            });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Fermer le cadre ferme ce qu'il contient. Les fenêtres en ressortaient
        // libres, et l'on se retrouvait avec autant de fenêtres de jeu éparses
        // qu'on croyait venir de fermer : le geste ne faisait pas ce qu'il dit.
        //
        // Elles quittent tout de même le cadre d'abord, et masquées : une
        // fenêtre logée est fille de celui-ci, et Windows détruirait ses
        // enfants avec lui sans laisser à scrcpy le temps de s'arrêter
        // proprement. Les montrer entre-temps ne ferait que clignoter.
        _closing = true;

        List<string> loges = [.. Items.Select(t => t.Key)];

        DetachAll(reveal: false);

        base.OnClosing(e);

        CloseRequested?.Invoke(this, loges);
    }

    /// <summary>
    /// Le rectangle que Windows passe dans WM_SIZING. Nommé à part de
    /// <c>System.Windows.Rect</c>, avec lequel il n'a en commun que l'idée.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SizingRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
