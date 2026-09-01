using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<QuestWindow> _logger;

    public QuestWindow(QuestViewModel viewModel, ILogger<QuestWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
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
    private void OnSearchClicked(object sender, MouseButtonEventArgs e) => OpenList();

    private void OnSearchFocused(object sender, KeyboardFocusChangedEventArgs e) => OpenList();

    /// <summary>
    /// Déploie la liste, puis amène la quête ouverte sous les yeux. Le
    /// défilement passe par la file du répartiteur : les conteneurs de lignes
    /// ne sont créés qu'après le rendu, et défiler avant ne mène nulle part.
    /// </summary>
    private void OpenList()
    {
        _viewModel.OpenList();

        Dispatcher.BeginInvoke(ScrollToSelection, DispatcherPriority.Loaded);
    }

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

        // La première ligne peut être un intertitre : on descend jusqu'à la
        // première qui se choisit vraiment.
        var index = _viewModel.FirstSelectable();

        if (index < 0)
        {
            return;
        }

        NodeList.SelectedIndex = index;
        NodeList.UpdateLayout();

        if (NodeList.ItemContainerGenerator.ContainerFromIndex(index) is System.Windows.Controls.ListBoxItem first)
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

        // Un clic sur un lien du guide faisait naviguer cette fenêtre en place :
        // le bandeau gardait l'ancienne quête, les étapes devenaient celles de
        // la nouvelle page. Toute navigation qu'on n'a pas demandée part donc
        // dans une fenêtre à part.
        View.CoreWebView2.NavigationStarting += OnNavigationStarting;

        // Et « target="_blank" », que le runtime ouvrirait dans une fenêtre
        // hors de tout contrôle, sans notre cadre ni notre premier plan.
        View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script())
            .ConfigureAwait(true);
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
                        Steps(root),
                        Flag(root, "departure"));
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

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

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

    /// <summary>
    /// Détourne vers une fenêtre à part toute navigation qui n'est pas la page
    /// qu'on a demandée.
    ///
    /// La comparaison se fait sans la barre finale : le site rend tantôt l'une,
    /// tantôt l'autre, et s'en tenir à l'égalité stricte détournerait la page
    /// qu'on vient d'ouvrir.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Same(e.Uri, _viewModel.CurrentUrl))
        {
            return;
        }

        e.Cancel = true;

        Route(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        Route(e.Uri);
    }

    /// <summary>
    /// Envoie une adresse là où elle se lit le mieux : sur place si c'est une
    /// quête du catalogue, dans une fenêtre à part sinon.
    /// </summary>
    private void Route(string url)
    {
        if (!_viewModel.TryFollowUrl(url))
        {
            _ = OpenAsideAsync(url);

            return;
        }

        // Le modèle est à jour, la page ne l'est pas : la navigation qui nous a
        // amenés ici vient d'être annulée, et il n'y a personne d'autre pour la
        // relancer. Sans cela le bandeau annonçait la nouvelle quête au-dessus
        // du guide de l'ancienne.
        //
        // Différée, parce qu'on est encore dans le gestionnaire qui vient de
        // refuser cette même navigation.
        _ = Dispatcher.BeginInvoke(() => View.CoreWebView2?.Navigate(url));
    }

    private static bool Same(string? first, string? second) =>
        string.Equals(
            (first ?? string.Empty).TrimEnd('/'),
            (second ?? string.Empty).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ouvre une adresse dans sa propre fenêtre, au-dessus des autres.
    ///
    /// L'échec est journalisé : lancée sans être attendue, cette tâche
    /// emporterait sinon son exception en silence, et le clic resterait sans
    /// effet ni explication.
    /// </summary>
    private async Task OpenAsideAsync(string url)
    {
        try
        {
            var page = AppHost.Services.GetRequiredService<QuestPageWindow>();

            await page.ShowPageAsync(url, null).ConfigureAwait(true);

            LogPageOpened(url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPageFailed(url, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Page liée ouverte : {url}")]
    private partial void LogPageOpened(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Page liée non ouverte ({reason}) : {url}")]
    private partial void LogPageFailed(string url, string reason);

    /// <summary>
    /// Amène la ligne sélectionnée sous les yeux. La poser ne suffit pas : sur
    /// une rubrique de soixante quêtes, elle reste hors de l'écran.
    /// </summary>
    private void ScrollToSelection()
    {
        if (NodeList.SelectedItem is not null)
        {
            NodeList.ScrollIntoView(NodeList.SelectedItem);
        }
    }

    private void OnGoBack(object sender, RoutedEventArgs e) => _viewModel.GoBack();

    private void OnClearQuery(object sender, RoutedEventArgs e)
    {
        _viewModel.Query = string.Empty;

        SearchBox.Focus();
    }

    private void OnCloseList(object sender, RoutedEventArgs e) => _viewModel.IsListOpen = false;

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
    /// <summary>
    /// Poignée native, retenue une fois pour toutes. Elle est consultée depuis
    /// le guet du premier plan, qui n'a pas le droit d'interroger une fenêtre
    /// WPF : l'interroger levait à chaque changement de fenêtre, et le
    /// raccourci mourait sans que rien ne le dise. Le configurateur prenait
    /// déjà cette précaution, pas celle-ci.
    /// </summary>
    public nint Handle { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        e.Cancel = true;
        Hide();
    }
}
