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
/// l'exprimer, et il n'existe aucun conteneur sélectionnable dans ces listes,
/// qui sont de simples <c>ItemsControl</c>.
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
    public static readonly DependencyProperty ShowActionsProperty =
        DependencyProperty.Register(
            nameof(ShowActions),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    private Point _origin;
    private object? _dragged;

    public InstanceListControl() => InitializeComponent();

    public bool ShowSelection
    {
        get => (bool)GetValue(ShowSelectionProperty);
        set => SetValue(ShowSelectionProperty, value);
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
    /// Retient le point de départ d'un éventuel glissé. Un appui dans une zone
    /// de saisie ou sur un bouton n'en est pas un : renommer une instance doit
    /// rester possible.
    /// </summary>
    private void OnDragSourcePressed(object sender, MouseButtonEventArgs e)
    {
        if (!ShowOrdering || sender is not FrameworkElement source || IsInteractive(e.OriginalSource))
        {
            _dragged = null;
            return;
        }

        _origin = e.GetPosition(this);
        _dragged = source.DataContext;
    }

    private void OnDragSourceMoved(object sender, MouseEventArgs e)
    {
        if (_dragged is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var moved = e.GetPosition(this) - _origin;

        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = _dragged;
        _dragged = null;

        if (ViewModel is not { } model)
        {
            return;
        }

        // Le balayage périodique reconstruit la liste : le suspendre évite
        // qu'une carte disparaisse sous le curseur en plein glissé.
        model.IsReordering = true;

        try
        {
            DragDrop.DoDragDrop(this, payload, DragDropEffects.Move);
        }
        finally
        {
            model.IsReordering = false;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Target(sender, e) is null ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (Target(sender, e) is not { } move || ViewModel is not { } model)
        {
            return;
        }

        await model.ReorderAsync(move.Dragged, move.Onto).ConfigureAwait(true);
    }

    /// <summary>
    /// Couple valide de déplacement, ou <c>null</c> si le dépôt n'a pas de
    /// sens : une instance sur un appareil, ou un élément sur lui-même.
    /// </summary>
    private static (object Dragged, object Onto)? Target(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement target || target.DataContext is not { } onto)
        {
            return null;
        }

        var dragged = e.Data.GetData(typeof(InstanceRowViewModel))
                      ?? e.Data.GetData(typeof(DeviceGroupViewModel));

        return dragged is not null
               && !ReferenceEquals(dragged, onto)
               && dragged.GetType() == onto.GetType()
            ? (dragged, onto)
            : null;
    }

    /// <summary>
    /// Vrai si l'élément visé réagit lui-même à la souris. Partir d'un champ
    /// de texte doit sélectionner du texte, pas déplacer la ligne.
    /// </summary>
    private static bool IsInteractive(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase or ButtonBase or ToggleButton)
            {
                return true;
            }
        }

        return false;
    }
}
