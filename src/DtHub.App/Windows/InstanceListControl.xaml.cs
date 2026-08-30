using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Liste des téléphones et de leurs instances. Le contexte de données attendu
/// est un <see cref="InstanceListViewModel"/>.
///
/// Le glisser-déposer vit ici, et non dans le modèle de vue : XAML ne sait pas
/// l'exprimer, et il n'existe aucun conteneur sélectionnable dans cette liste,
/// qui est un simple <c>ItemsControl</c>. Une seule nature d'objet s'y
/// déplace, l'instance : les appareils ne se trient plus.
/// </summary>
public partial class InstanceListControl : UserControl
{
    /// <summary>
    /// Montre la case « ouvrir au démarrage ». Vraie seulement dans la fenêtre
    /// de premier lancement : c'est le dernier endroit où la question se pose.
    /// Ensuite, ce qui rouvre est ce qui était ouvert au moment de quitter.
    /// </summary>
    public static readonly DependencyProperty ShowSelectionProperty =
        DependencyProperty.Register(
            nameof(ShowSelection),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(false));

    /// <summary>
    /// Montre les commandes de réordonnancement. Absentes de la fenêtre de
    /// premier lancement, où rien n'est encore ouvert.
    /// </summary>
    public static readonly DependencyProperty ShowOrderingProperty =
        DependencyProperty.Register(
            nameof(ShowOrdering),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    /// <summary>
    /// Montre les boutons d'action de chaque instance. Coupé dans la fenêtre
    /// de mise en route : on y coche ce qui doit s'ouvrir, et c'est le bouton
    /// « Enregistrer et lancer » qui décide, pas un bouton par ligne.
    /// </summary>
    /// <summary>
    /// Montre l'état de chaque appareil en toutes lettres, et non par le seul
    /// point de couleur.
    ///
    /// Vrai dans la fenêtre de démarrage : on y choisit ce qu'on lance, et
    /// savoir pourquoi une ligne manque compte plus qu'une liste sobre.
    /// </summary>
    public static readonly DependencyProperty ShowDeviceStatusProperty =
        DependencyProperty.Register(
            nameof(ShowDeviceStatus),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowActionsProperty =
        DependencyProperty.Register(
            nameof(ShowActions),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    private Point _origin;
    private InstanceRowViewModel? _pending;

    public InstanceListControl() => InitializeComponent();

    public bool ShowSelection
    {
        get => (bool)GetValue(ShowSelectionProperty);
        set => SetValue(ShowSelectionProperty, value);
    }

    public bool ShowDeviceStatus
    {
        get => (bool)GetValue(ShowDeviceStatusProperty);
        set => SetValue(ShowDeviceStatusProperty, value);
    }

    public bool ShowOrdering
    {
        get => (bool)GetValue(ShowOrderingProperty);
        set => SetValue(ShowOrderingProperty, value);
    }

    public bool ShowActions
    {
        get => (bool)GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    private InstanceListViewModel? ViewModel => DataContext as InstanceListViewModel;

    /// <summary>
    /// Retient le point de départ. Seule la poignée déclenche un glissé : une
    /// ligne entière rendrait impossible la sélection de texte dans le champ
    /// de renommage.
    /// </summary>
    private void OnDragSourcePressed(object sender, MouseButtonEventArgs e)
    {
        _pending = ShowOrdering && sender is FrameworkElement { DataContext: InstanceRowViewModel row }
            ? row
            : null;
        _origin = e.GetPosition(this);
    }

    private void OnDragSourceMoved(object sender, MouseEventArgs e)
    {
        if (_pending is null || e.LeftButton != MouseButtonState.Pressed || ViewModel is not { } model)
        {
            return;
        }

        var moved = e.GetPosition(this) - _origin;

        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = _pending;
        _pending = null;

        // Le balayage périodique reconstruit la liste : le suspendre évite
        // qu'une carte disparaisse sous le curseur en plein glissé.
        model.IsReordering = true;
        payload.IsDragging = true;

        try
        {
            DragDrop.DoDragDrop(this, payload, DragDropEffects.Move);
        }
        finally
        {
            model.IsReordering = false;
            model.ClearDropHints();
        }
    }

    /// <summary>
    /// Montre où l'élément se posera : un trait au-dessus ou en dessous de
    /// celui que l'on survole, selon la moitié où se trouve le curseur.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        e.Effects = DragDropEffects.Move;

        if (Hovered(sender) is not { } onto || ViewModel is not { } model)
        {
            return;
        }

        // Au-dessus de l'élément déplacé lui-même, aucun repère : le poser là
        // n'aurait pas de sens, et l'omettre laisserait le précédent figé au
        // mauvais endroit après un aller-retour.
        if (ReferenceEquals(onto, Dragged(e)))
        {
            model.ClearDropHints();
            return;
        }

        model.ShowDropHint(onto, IsUpperHalf(sender, e));
    }

    /// <summary>
    /// Le repère n'est pas effacé en quittant un élément. Passer d'une ligne à
    /// sa voisine, ou simplement survoler un champ de saisie, fait sortir puis
    /// entrer : effacer à chaque fois faisait clignoter le trait. Il n'est
    /// retiré qu'à la fin du glissé.
    /// </summary>
    private void OnDragLeave(object sender, DragEventArgs e) => e.Handled = true;

    /// <summary>
    /// Garde le curseur habituel pendant le glissé. Les curseurs de
    /// glisser-déposer de Windows changent au passage de chaque élément, ce
    /// qui donne l'impression que quelque chose ne va pas, alors que seul le
    /// trait de position compte ici.
    /// </summary>
    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.SizeNS);
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        var above = IsUpperHalf(sender, e);

        if (Hovered(sender) is not { } onto
            || Dragged(e) is not { } dragged
            || ReferenceEquals(dragged, onto)
            || ViewModel is not { } model)
        {
            return;
        }

        model.ClearDropHints();

        await model.ReorderAsync(dragged, onto, above).ConfigureAwait(true);
    }

    /// <summary>Vrai si le curseur est dans la moitié haute de la cible.</summary>
    private static bool IsUpperHalf(object sender, DragEventArgs e) =>
        sender is FrameworkElement target
        && e.GetPosition(target).Y < target.ActualHeight / 2;

    /// <summary>Ligne survolée, ou <c>null</c> si le curseur n'est sur aucune.</summary>
    private static InstanceRowViewModel? Hovered(object sender) =>
        sender is FrameworkElement { DataContext: InstanceRowViewModel row } ? row : null;

    private static InstanceRowViewModel? Dragged(DragEventArgs e) =>
        e.Data.GetData(typeof(InstanceRowViewModel)) as InstanceRowViewModel;
}
