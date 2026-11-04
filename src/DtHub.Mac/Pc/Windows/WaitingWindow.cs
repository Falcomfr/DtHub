using Avalonia.Controls;
using Avalonia.Layout;

using DtHub.Core.Localization;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's WaitingWindow on the Mac: it stands at a game window's place
/// while its phone's link is down, until the game comes back there.
/// </summary>
public sealed class WaitingWindow : Window
{
    private bool _released;

    public WaitingWindow(string title)
    {
        Title = title;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var close = new Button { Content = Strings.Get("Close"), HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(24),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.Get("WaitingPhoneTitle"),
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                new TextBlock
                {
                    Text = Strings.Get("WaitingPhoneText"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    TextAlignment = Avalonia.Media.TextAlignment.Center,
                },
                close,
            },
        };
    }

    public event EventHandler? CloseRequested;

    public void Release()
    {
        _released = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Quitting DT Hub takes every window down: that is no player's
        // close, and the account must stay in the next startup.
        if (!_released && !DtHub.Mac.MacApp.IsShuttingDown)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
