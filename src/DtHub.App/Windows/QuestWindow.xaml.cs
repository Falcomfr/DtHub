using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
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

    private bool _bridgeReady;

    /// <summary>Charge la page d'une quête, en s'assurant que le moteur est prêt.</summary>
    private async void Open(QuestSummary quest)
    {
        try
        {
            await PrepareAsync().ConfigureAwait(true);

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
    /// Met le moteur en route et y installe le pont, une seule fois.
    ///
    /// Le script est posé avant tout chargement de document : posé après, il
    /// laisserait voir le décor du site le temps d'une image.
    /// </summary>
    private async Task PrepareAsync()
    {
        await View.EnsureCoreWebView2Async().ConfigureAwait(true);

        if (_bridgeReady)
        {
            return;
        }

        _bridgeReady = true;

        View.CoreWebView2.WebMessageReceived += OnBridgeMessage;

        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript())
            .ConfigureAwait(true);
    }

    /// <summary>Lit le pont depuis les ressources de l'assembly.</summary>
    private static string BridgeScript()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DtHub.App.quest-bridge.js")
            ?? throw new InvalidOperationException("Le pont de la fenêtre de quêtes est absent de l'assembly.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private void OnBridgeMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.TryGetWebMessageAsString());

            var root = document.RootElement;

            switch (root.GetProperty("kind").GetString())
            {
                case "loaded":
                    _viewModel.SetPage(
                        Text(root, "intro"),
                        Text(root, "chain"),
                        Steps(root));
                    break;

                case "step":
                    _viewModel.SetStep(root.GetProperty("index").GetInt32());
                    break;

                default:
                    break;
            }
        }
        catch (JsonException)
        {
            // Un message qui ne vient pas du pont, ou une page qui en aurait
            // posté un autre : on l'ignore plutôt que de faire tomber la
            // fenêtre.
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static IReadOnlyList<string> Steps(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. steps.EnumerateArray().Select(s => s.GetString() ?? string.Empty)];
    }

    /// <summary>Fait défiler la page jusqu'à une étape.</summary>
    private async void GoToStep(int index)
    {
        if (index < 0)
        {
            return;
        }

        try
        {
            await View.CoreWebView2
                .ExecuteScriptAsync($"window.__dtHubGoToStep({index.ToString(CultureInfo.InvariantCulture)})")
                .ConfigureAwait(true);

            _viewModel.SetStep(index);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _viewModel.ReportViewFailure(exception);
        }
    }

    private void OnPreviousStep(object sender, RoutedEventArgs e) => GoToStep(_viewModel.StepTarget(-1));

    private void OnNextStep(object sender, RoutedEventArgs e) => GoToStep(_viewModel.StepTarget(1));

    /// <summary>Suit un lien de chaîne : la quête précédente ou la suivante.</summary>
    private void OnFollowChain(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is QuestLink link)
        {
            _viewModel.Follow(link);
            View.CoreWebView2?.Navigate(link.Url);
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
