using System.Windows;

using DtHub.App.ViewModels;

namespace DtHub.App.Windows;

/// <summary>
/// Fenêtre de suivi des quêtes : une recherche, et la page du guide affichée
/// telle que le site la rend.
/// </summary>
public partial class QuestWindow : Window
{
    private readonly QuestViewModel _viewModel;

    public QuestWindow(QuestViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>Montre la fenêtre si elle est masquée, la masque sinon.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _viewModel.OpenInBrowser();

    /// <summary>
    /// La croix masque, elle ne ferme pas : la fenêtre est un outil qu'on
    /// rappelle au raccourci, comme le configurateur.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Cancel = true;
        Hide();
    }
}
