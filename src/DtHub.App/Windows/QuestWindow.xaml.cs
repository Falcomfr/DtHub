using System.Windows;
using System.Windows.Input;

using DtHub.App.ViewModels;
using DtHub.Core.Papycha;

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

        // Le curseur dans la recherche et la liste déroulée : on ouvre cette
        // fenêtre pour trouver une quête, jamais pour regarder du vide.
        SearchBox.Focus();
        _viewModel.OpenList();
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _viewModel.OpenInBrowser();

    /// <summary>
    /// Cliquer dans la recherche déroule la liste, comme le ferait une liste
    /// déroulante ordinaire : c'est le geste qu'on fait sans y penser quand on
    /// ne sait pas encore ce qu'on cherche.
    /// </summary>
    private void OnSearchClicked(object sender, MouseButtonEventArgs e) => _viewModel.OpenList();

    private void OnSearchFocused(object sender, KeyboardFocusChangedEventArgs e) => _viewModel.OpenList();

    private void OnNodeChosen(object sender, MouseButtonEventArgs e) => ChooseSelected();

    /// <summary>
    /// Depuis la recherche, la flèche du bas entre dans la liste : c'est le
    /// geste attendu de toute liste déroulante, et il évite de lâcher le
    /// clavier pour prendre la souris.
    /// </summary>
    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Escape)
        {
            _viewModel.IsListOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Down || NodeList.Items.Count == 0)
        {
            return;
        }

        _viewModel.OpenList();

        NodeList.SelectedIndex = 0;

        if (NodeList.ItemContainerGenerator.ContainerFromIndex(0) is System.Windows.Controls.ListBoxItem first)
        {
            first.Focus();
        }

        e.Handled = true;
    }

    /// <summary>Entrée vaut clic : la liste se parcourt aussi au clavier.</summary>
    private void OnNodeKey(object sender, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Enter)
        {
            ChooseSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.IsListOpen = false;
            e.Handled = true;
        }
    }

    private void ChooseSelected()
    {
        if (_viewModel.Activate(NodeList.SelectedItem as QuestNode) is { } quest)
        {
            Open(quest);
        }
    }

    /// <summary>Charge la page d'une quête, en s'assurant que le moteur est prêt.</summary>
    private async void Open(QuestSummary quest)
    {
        try
        {
            await View.EnsureCoreWebView2Async().ConfigureAwait(true);

            View.CoreWebView2.Navigate(quest.Url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Sans moteur d'exécution, la fenêtre ne peut rien montrer : le
            // dire vaut mieux que laisser une zone vide.
            _viewModel.ReportViewFailure(exception);
        }
    }

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
