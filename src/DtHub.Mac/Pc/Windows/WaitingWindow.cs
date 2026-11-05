using Avalonia.Controls;

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

        // The PC's WaitingPhoneView, the same as in a tab.
        var view = new WaitingPhoneView();
        view.CloseRequested += (_, _) => Close();
        Content = view;
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
