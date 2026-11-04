using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DtHub.Mac.Platform;

/// <summary>
/// The PC's NameIconButton (App.xaml.cs): an icon button would otherwise be
/// read out as "button" alone, by VoiceOver and by anything that drives the
/// interface. It takes the label shown on it first, so what is read matches
/// what is seen; failing that, its tooltip, followed as it changes ("Open"
/// then "Close"). A name set in the markup wins. One handler for every
/// button, as on the PC, so that no style or template is left silent.
/// </summary>
internal static class AccessibleNames
{
    public static void Install() =>
        Control.LoadedEvent.AddClassHandler<Button>(OnLoaded, RoutingStrategies.Direct, handledEventsToo: true);

    private static void OnLoaded(Button button, RoutedEventArgs e)
    {
        if (button.Content is string or TextBlock || !string.IsNullOrEmpty(AutomationProperties.GetName(button)))
        {
            return;
        }

        if ((button.Content as Panel)?.Children.OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text))
            is { } label)
        {
            AutomationProperties.SetName(button, label.Text);
            return;
        }

        Follow(button);
        button.PropertyChanged += (_, change) =>
        {
            if (change.Property == ToolTip.TipProperty)
            {
                Follow(button);
            }
        };
    }

    private static void Follow(Button button)
    {
        if (ToolTip.GetTip(button) is string tip)
        {
            AutomationProperties.SetName(button, tip);
        }
    }
}
