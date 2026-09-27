using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Help: the steps to follow on the phone, adapted to its brand.
/// </summary>
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

    /// <summary>
    /// Opens the steps on this phone's brand instead of guessing from
    /// whoever is answering. Called before showing, since the brand is
    /// settled when the window loads.
    /// </summary>
    public void OpenFor(string? manufacturer) => _viewModel.OpenedFor = manufacturer;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
