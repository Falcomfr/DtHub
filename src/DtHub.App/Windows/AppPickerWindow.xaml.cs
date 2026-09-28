using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Chooses what a phone shows beside the game, or asks for a new
/// account. Code-behind limited to closing with the chosen outcome.
/// </summary>
public partial class AppPickerWindow : Window
{
    private readonly AppPickerViewModel _viewModel;

    public AppPickerWindow(AppPickerViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        // The search takes the focus: with a hundred applications on a
        // phone, typing a few letters is the way in.
        Loaded += async (_, _) =>
        {
            _ = Search.Focus();
            await viewModel.LoadAsync().ConfigureAwait(true);
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _viewModel.Outcome = AppPickerOutcome.Saved;
        DialogResult = true;
    }

    private void OnClone(object sender, RoutedEventArgs e)
    {
        _viewModel.Outcome = AppPickerOutcome.CloneRequested;
        DialogResult = true;
    }
}
