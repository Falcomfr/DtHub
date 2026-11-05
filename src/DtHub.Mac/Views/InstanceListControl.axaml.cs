using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.VisualTree;

using DtHub.App.ViewModels;
using DtHub.Core.Devices;
using DtHub.Core.Guidance;
using DtHub.Core.Localization;
using DtHub.Mac.Pc;

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

    public InstanceListControl()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnDragKey, RoutingStrategies.Tunnel);
    }

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
    /// The alert line's Help link: one help opens straight away; several are
    /// offered in a small panel of the theme's colours, one line each, the
    /// app named, so a second problem is not hidden behind the first.
    /// </summary>
    private void OnProblemHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: InstanceRowViewModel { Device: { } device } } link)
        {
            return;
        }

        if (device.Helps.Count == 1)
        {
            OpenHelp(device.Helps[0], device.DeviceId);
            return;
        }

        var items = new StackPanel();
        var menu = new Flyout { Placement = PlacementMode.Bottom };
        menu.FlyoutPresenterClasses.Add("bare");

        foreach (var help in device.Helps)
        {
            var item = new Button
            {
                Content = help.Help == HealthHelp.Input
                    ? Strings.Get("HelpMenuInput")
                    : Strings.Format("HelpMenuBattery", help.App ?? string.Empty),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };

            if (this.TryFindResource("QuietButton", out var quiet) && quiet is ControlTheme theme)
            {
                item.Theme = theme;
            }

            item.Click += (_, _) =>
            {
                menu.Hide();
                OpenHelp(help, device.DeviceId);
            };
            items.Children.Add(item);
        }

        var panel = new Border { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4), Child = items };
        panel.Classes.Add("menu");

        menu.Content = panel;
        menu.ShowAt(link);
    }

    /// <summary>The click setting of the phone, or the battery page of the app the finding names.</summary>
    private async void OpenHelp(HealthFinding finding, string deviceId)
    {
        Window help;

        if (finding.Help == HealthHelp.Input)
        {
            help = MacApp.Services.GetRequiredService<InputHelpWindow>();
        }
        else
        {
            var sleep = MacApp.Services.GetRequiredService<SleepHelpWindow>();

            if (finding.App is { } app)
            {
                sleep.ViewModel.App = app;
                sleep.ViewModel.DeviceId = deviceId;
            }

            help = sleep;
        }

        _ = await Modal.ShowOver<object>(help, TopLevel.GetTopLevel(this) as Window).ConfigureAwait(true);
    }

    /// <summary>The steps to follow on a phone that is not answering.</summary>
    private async void OnDeviceHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: DeviceGroupViewModel device })
        {
            return;
        }

        var help = MacApp.Services.GetRequiredService<HelpWindow>();

        help.OpenFor(device.Manufacturer);
        // At the top: the row says to open wireless debugging, the first
        // step, and opening on the fixes further down hid it (2026-10-26).
        help.OpenOn(OfflineAdvice.HelpOpensOnCable(device.Connection));
        _ = await Modal.ShowOver<object>(help, TopLevel.GetTopLevel(this) as Window).ConfigureAwait(true);
    }

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
        _pointer = e.Pointer;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    /// <summary>Escape gives up a drag, the row staying where it was.</summary>
    private void OnDragKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_dragged is not null || _pending is not null))
        {
            EndDrag();
            _pointer?.Capture(null);
            e.Handled = true;
        }
    }

    private IPointer? _pointer;

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

        // Between two rows the last marker stays, as on the PC, rather than
        // blink off; above the item being dragged itself, none: dropping it
        // there would make no sense.
        if (Hovered(position) is not { } target)
        {
            return;
        }

        if (ReferenceEquals(target.Row, _dragged))
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
