using System.Windows;

namespace DtHub.App.Windows;

/// <summary>
/// Waits for the phone at a free game window's place: its game comes back
/// there. Closing it closes the account, as the game window's cross does.
/// </summary>
public partial class WaitingWindow : Window
{
    private bool _released;

    public WaitingWindow(string title)
    {
        InitializeComponent();
        Title = title;
    }

    /// <summary>Raised when the player closes it, not when DT Hub takes it down.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Takes it down because the game is back, or no longer awaited.</summary>
    public void Release()
    {
        _released = true;
        Close();
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Closing DT Hub takes every window down: that is no player's close,
        // and the account must stay in the next startup.
        if (!_released && !Dispatcher.HasShutdownStarted)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
