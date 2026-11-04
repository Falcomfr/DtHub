using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DtHub.App.Windows;

/// <summary>
/// The PC's WaitingPhoneView (src/DtHub.App/Windows/WaitingPhoneView.xaml.cs):
/// the no-signal mark, what happened, and a way to close the account. In
/// the tabbed frame's waiting area and in <see cref="WaitingWindow"/>.
/// </summary>
public sealed partial class WaitingPhoneView : UserControl
{
    public WaitingPhoneView() => AvaloniaXamlLoader.Load(this);

    /// <summary>The view's Close: closes that account, as its tab's cross does.</summary>
    public event EventHandler? CloseRequested;

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
