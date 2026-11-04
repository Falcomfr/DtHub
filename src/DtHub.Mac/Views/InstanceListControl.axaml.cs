using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Localization;

using Microsoft.Extensions.DependencyInjection;

namespace DtHub.Mac.Views;

/// <summary>
/// List of phones and their instances, the PC's InstanceListControl
/// (src/DtHub.App/Windows/InstanceListControl.xaml.cs). The expected data
/// context is an <see cref="InstanceListViewModel"/>.
///
/// Reordering lives here, not in the view model, as on the PC. It is a
/// pointer drag rather than the system's drag and drop: only one kind of
/// object moves, within this list, and capturing the pointer on the handle
/// gives the same gesture without a clipboard format to carry it.
/// </summary>
public partial class InstanceListControl : UserControl
{
    /// <summary>Shows the reordering handle. Absent where nothing is open yet.</summary>
    public static readonly StyledProperty<bool> ShowOrderingProperty =
        AvaloniaProperty.Register<InstanceListControl, bool>(nameof(ShowOrdering), defaultValue: true);

    /// <summary>Shows each device's state spelled out, not just through the dot.</summary>
    public static readonly StyledProperty<bool> ShowDeviceStatusProperty =
        AvaloniaProperty.Register<InstanceListControl, bool>(nameof(ShowDeviceStatus));

    /// <summary>Shows each instance's action buttons.</summary>
    public static readonly StyledProperty<bool> ShowActionsProperty =
        AvaloniaProperty.Register<InstanceListControl, bool>(nameof(ShowActions), defaultValue: true);

    // The distance the pointer travels before a press becomes a drag, the
    // system's usual few points: below it, a click on the handle stays one.
    private const double DragThreshold = 4;

    private Point _origin;
    private InstanceRowViewModel? _pending;
    private InstanceRowViewModel? _dragged;

    public InstanceListControl() => AvaloniaXamlLoader.Load(this);

    public bool ShowDeviceStatus
    {
        get => GetValue(ShowDeviceStatusProperty);
        set => SetValue(ShowDeviceStatusProperty, value);
    }

    public bool ShowOrdering
    {
        get => GetValue(ShowOrderingProperty);
        set => SetValue(ShowOrderingProperty, value);
    }

    public bool ShowActions
    {
        get => GetValue(ShowActionsProperty);
        set => SetValue(ShowActionsProperty, value);
    }

    private InstanceListViewModel? ViewModel => DataContext as InstanceListViewModel;

    /// <summary>
    /// The alert line's Help link. On the PC, one help opens the battery
    /// page of the app the line names, or the phone's click setting, and
    /// several are offered in a small menu first.
    /// </summary>
    private void OnProblemHelp(object? sender, RoutedEventArgs e) =>
        // shortcut: SleepHelpWindow and InputHelpWindow are not on the Mac
        // yet, nor the menu that picks between them.
        MacApp.Services.GetRequiredService<IDialogService>().ShowInformation(Strings.Get("MacNotYet"));

    /// <summary>The steps to follow on a phone that is not answering.</summary>
    private void OnDeviceHelp(object? sender, RoutedEventArgs e) =>
        // shortcut: HelpWindow is not on the Mac yet.
        MacApp.Services.GetRequiredService<IDialogService>().ShowInformation(Strings.Get("MacNotYet"));

    /// <summary>
    /// Remembers the starting point. Only the handle starts a drag: an
    /// entire row would make it impossible to select text in the rename
    /// field.
    /// </summary>
    private void OnDragSourcePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: InstanceRowViewModel row } handle
            || !ShowOrdering
            || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pending = row;
        _origin = e.GetPosition(this);
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private void OnDragSourceMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel is not { } model)
        {
            return;
        }

        var position = e.GetPosition(this);

        if (_pending is { } pending)
        {
            var moved = position - _origin;

            if (Math.Abs(moved.X) < DragThreshold && Math.Abs(moved.Y) < DragThreshold)
            {
                return;
            }

            _pending = null;
            _dragged = pending;

            // The periodic sweep rebuilds the list: suspending it keeps a
            // card from disappearing under the cursor in mid-drag.
            model.IsReordering = true;
            pending.IsDragging = true;
        }

        if (_dragged is null)
        {
            return;
        }

        // Above the item being dragged itself, no marker: dropping it there
        // would make no sense, and the previous marker would stay stuck.
        if (Hovered(position) is not { } target || ReferenceEquals(target.Row, _dragged))
        {
            model.ClearDropHints();
            return;
        }

        model.ShowDropHint(target.Row, target.Above);
    }

    private async void OnDragSourceReleased(object? sender, PointerReleasedEventArgs e)
    {
        var dragged = _dragged;
        var target = dragged is null ? null : Hovered(e.GetPosition(this));

        EndDrag();
        e.Pointer.Capture(null);

        if (dragged is null
            || target is not { } onto
            || ReferenceEquals(onto.Row, dragged)
            || ViewModel is not { } model)
        {
            return;
        }

        // An async void handler: a locked settings file or adb failing during
        // the reopen would otherwise surface as an unhandled fault.
        try
        {
            await model.ReorderAsync(dragged, onto.Row, onto.Above).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.ShowBanner(exception.Message);
        }
    }

    /// <summary>Capture taken away (the window lost focus): the drag ends where it was.</summary>
    private void OnDragSourceLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _pending = null;

        if (_dragged is { } dragged)
        {
            dragged.IsDragging = false;
            _dragged = null;
        }

        if (ViewModel is { } model)
        {
            model.ClearDropHints();
            model.IsReordering = false;
        }
    }

    /// <summary>
    /// The row under the pointer, and whether the pointer is in its upper
    /// half, which decides between above and below.
    /// </summary>
    private (InstanceRowViewModel Row, bool Above)? Hovered(Point position)
    {
        var surface = this.GetVisualsAt(position)
            .SelectMany(visual => visual.GetSelfAndVisualAncestors())
            .OfType<Control>()
            .FirstOrDefault(control => control.Classes.Contains("dropSurface"));

        if (surface is not { DataContext: InstanceRowViewModel row })
        {
            return null;
        }

        var local = this.TranslatePoint(position, surface) ?? default;

        return (row, local.Y < surface.Bounds.Height / 2);
    }

    /// <summary>
    /// Applies the name being typed on Enter, or gives it up on Escape.
    /// The field writes on lost focus; Enter looked like the rename had
    /// been ignored.
    /// </summary>
    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox field
            || BindingOperations.GetBindingExpressionBase(field, TextBox.TextProperty) is not { } binding)
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
            // Puts the stored name back in the field.
            binding.UpdateTarget();
            e.Handled = true;
        }
    }
}
