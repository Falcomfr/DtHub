using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// List of phones and their instances. The expected data context
/// is an <see cref="InstanceListViewModel"/>.
///
/// Drag and drop lives here, not in the view model: XAML cannot
/// express it, and there is no selectable container in this list,
/// which is a plain <c>ItemsControl</c>. Only one kind of object
/// moves within it, the instance: devices are no longer sortable.
/// </summary>
public partial class InstanceListControl : UserControl
{
    /// <summary>
    /// Shows the reordering commands. Absent from the first-launch
    /// window, where nothing is open yet.
    /// </summary>
    public static readonly DependencyProperty ShowOrderingProperty =
        DependencyProperty.Register(
            nameof(ShowOrdering),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    /// <summary>
    /// Shows each device's state spelled out in full, not just
    /// through the color dot.
    ///
    /// True in the startup window: it is where you choose what to
    /// launch, and knowing why a row is missing matters more than
    /// a clean list.
    /// </summary>
    public static readonly DependencyProperty ShowDeviceStatusProperty =
        DependencyProperty.Register(
            nameof(ShowDeviceStatus),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(false));

    /// <summary>
    /// Shows each instance's action buttons. Turned off in the
    /// setup window: there you check what should open, and it is
    /// the "Save and launch" button that decides, not a button per
    /// row.
    /// </summary>
    public static readonly DependencyProperty ShowActionsProperty =
        DependencyProperty.Register(
            nameof(ShowActions),
            typeof(bool),
            typeof(InstanceListControl),
            new PropertyMetadata(true));

    private Point _origin;
    private InstanceRowViewModel? _pending;

    public InstanceListControl() => InitializeComponent();

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
    /// Remembers the starting point. Only the handle triggers a
    /// drag: an entire row would make it impossible to select text
    /// in the rename field.
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

        // The periodic sweep rebuilds the list: suspending it
        // prevents a card from disappearing under the cursor in
        // the middle of a drag.
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
    /// Shows where the item will land: a line above or below the
    /// one being hovered over, depending on which half the cursor
    /// is in.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        e.Effects = DragDropEffects.Move;

        if (Hovered(sender) is not { } onto || ViewModel is not { } model)
        {
            return;
        }

        // Above the item being dragged itself, no marker: dropping
        // it there would make no sense, and omitting this check
        // would leave the previous marker stuck in the wrong place
        // after a round trip.
        if (ReferenceEquals(onto, Dragged(e)))
        {
            model.ClearDropHints();
            return;
        }

        model.ShowDropHint(onto, IsUpperHalf(sender, e));
    }

    /// <summary>
    /// The marker is not cleared on leaving an item. Moving from
    /// one row to its neighbor, or simply hovering over an input
    /// field, triggers a leave then an enter: clearing it every
    /// time made the line flicker. It is only removed at the end
    /// of the drag.
    /// </summary>
    private void OnDragLeave(object sender, DragEventArgs e) => e.Handled = true;

    /// <summary>
    /// Keeps the usual cursor during the drag. Windows's
    /// drag-and-drop cursors change as they pass over each item,
    /// which gives the impression that something is wrong, when
    /// only the position line matters here.
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

    /// <summary>True if the cursor is in the target's upper half.</summary>
    private static bool IsUpperHalf(object sender, DragEventArgs e) =>
        sender is FrameworkElement target
        && e.GetPosition(target).Y < target.ActualHeight / 2;

    /// <summary>
    /// Row being hovered over, or <c>null</c> if the cursor is on
    /// none.
    /// </summary>
    private static InstanceRowViewModel? Hovered(object sender) =>
        sender is FrameworkElement { DataContext: InstanceRowViewModel row } ? row : null;

    private static InstanceRowViewModel? Dragged(DragEventArgs e) =>
        e.Data.GetData(typeof(InstanceRowViewModel)) as InstanceRowViewModel;

    /// <summary>
    /// Applies the name being typed, or gives it up.
    ///
    /// The field writes on lost focus, which is what a TextBox does by
    /// default, and nothing was bound to Enter. Applying a new name
    /// therefore meant clicking somewhere else, and pressing Enter looked
    /// like the rename had been ignored.
    /// </summary>
    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (sender is not TextBox field
            || BindingOperations.GetBindingExpression(field, TextBox.TextProperty) is not { } binding)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            binding.UpdateSource();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Puts the stored name back in the field. Without it, giving up
            // on a half typed name means remembering the old one and typing
            // it again.
            binding.UpdateTarget();
            e.Handled = true;
        }
    }
}
