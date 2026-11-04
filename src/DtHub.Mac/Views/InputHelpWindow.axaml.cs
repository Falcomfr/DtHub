using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using DtHub.App.ViewModels;

namespace DtHub.Mac.Views;

/// <summary>
/// Explains why nothing responds in the game window. The PC's
/// InputHelpWindow (src/DtHub.App/Windows/InputHelpWindow.xaml.cs).
/// </summary>
public partial class InputHelpWindow : Window
{
    /// <summary>For the XAML previewer only.</summary>
    public InputHelpWindow() => AvaloniaXamlLoader.Load(this);

    public InputHelpWindow(InputHelpViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        AvaloniaXamlLoader.Load(this);

        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
