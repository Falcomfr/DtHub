using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>Aide : la marche à suivre sur le téléphone, adaptée à sa marque.</summary>
public partial class HelpWindow : Window
{
    private readonly HelpViewModel _viewModel;

    public HelpWindow(HelpViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
