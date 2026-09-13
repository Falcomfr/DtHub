using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Explains how to prevent Android from putting the game to sleep.
/// </summary>
public partial class SleepHelpWindow : Window
{
    public SleepHelpWindow(SleepHelpViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
