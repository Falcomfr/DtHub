using System.Windows;
using System.Windows.Controls;

namespace DtHub.App.Windows;

/// <summary>The view that waits for the phone where the game was.</summary>
public partial class WaitingPhoneView : UserControl
{
    public WaitingPhoneView() => InitializeComponent();

    /// <summary>Raised by the Close button.</summary>
    public event EventHandler? CloseRequested;

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
