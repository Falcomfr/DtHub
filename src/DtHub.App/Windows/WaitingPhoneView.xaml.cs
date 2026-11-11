using System.Windows;
using System.Windows.Controls;

namespace DtHub.App.Windows;

/// <summary>The view that waits for the phone where the game was.</summary>
public partial class WaitingPhoneView : UserControl
{
    public WaitingPhoneView() => InitializeComponent();

    /// <summary>
    /// Says why the game waits: <c>null</c> for the phone's Wi-Fi link, with
    /// its icon, or a text of its own, an iPhone's cable or new stream.
    /// </summary>
    public void Explain(string? text)
    {
        var own = text is not null;

        Link.Visibility = own ? Visibility.Collapsed : Visibility.Visible;
        LinkText.Visibility = own ? Visibility.Collapsed : Visibility.Visible;
        OwnText.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
        OwnText.Text = text ?? string.Empty;
    }

    /// <summary>Raised by the Close button.</summary>
    public event EventHandler? CloseRequested;

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
