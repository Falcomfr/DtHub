using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>Explique comment obtenir une seconde installation du jeu.</summary>
public partial class CloneHelpWindow : Window
{
    public CloneHelpWindow(CloneHelpViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
