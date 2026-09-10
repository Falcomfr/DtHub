using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>Explique pourquoi rien ne répond dans la fenêtre de jeu.</summary>
public partial class InputHelpWindow : Window
{
    public InputHelpWindow(InputHelpViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
