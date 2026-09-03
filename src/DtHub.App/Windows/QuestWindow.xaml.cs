using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Settings;
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
    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;
    private readonly IDialogService _dialogs;

    public QuestWindow(
        QuestViewModel viewModel,
        WindowPlacements placements,
        SettingsService settings,
        WebViewEnvironment engine,
        IDialogService dialogs,
        ILogger<QuestWindow> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _placements = placements;
        _settings = settings;
        _engine = engine;
        _dialogs = dialogs;
        _logger = logger;
        DataContext = viewModel;

        // Le moteur de rendu garde six processus et un demi-gigaoctet quand la
        // fenêtre est masquée, ce qui est le cas la plupart du temps : on ouvre
        // les guides pour lire une étape, puis on retourne au jeu. Il sait se
        // mettre en sommeil, à condition qu'on ne lui demande rien d'autre.
        IsVisibleChanged += (_, _) => OnVisibilityChanged();

        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>Montre la fenêtre si elle est masquée, la masque sinon.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            // La place est retenue au moment où l'on masque : on ne la
            // retrouverait plus après, la fenêtre n'ayant plus de position à
            // l'écran qui vaille.
            _ = _placements.SaveAsync(this, WindowPlacements.Quests);

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

    /// <summary>
    /// Rouvre la fenêtre sur la quête qu'on lisait au dernier arrêt.
    ///
    /// La liste ne se déroule pas dans ce cas : on retrouve la page où on
    /// l'avait laissée, ce qui est justement ce qu'on venait chercher.
    /// </summary>
    public async Task RestoreAsync(string? url, int step = 0)
    {
        Show();

        // L'étape attend que la page ait dit combien elle en a : le pont ne le
        // rapporte qu'une fois le document lu, et sauter avant ne mènerait
        // nulle part.
        _pendingStep = step;

        // Le catalogue doit être là avant qu'on lui demande une quête. Il se
        // charge d'ordinaire au premier affichage, mais rien ne garantit qu'il
        // ait fini : sans cette attente, l'adresse retenue tombait à côté et la
        // fenêtre s'ouvrait sur sa liste.
        await _viewModel.InitializeAsync().ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(url) || !_viewModel.TryFollowUrl(url))
        {
            _viewModel.OpenList();

            return;
        }

        try
        {
            await PrepareAsync().ConfigureAwait(true);

            NavigateTo(url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _viewModel.ReportViewFailure(exception);
        }
    }

    /// <summary>L'adresse de ce qu'on lisait, pour la retrouver au prochain lancement.</summary>
    public string? LastQuestUrl => _viewModel.CurrentUrl;

    /// <summary>L'étape où l'on en était, pour y revenir au prochain lancement.</summary>
    public int LastQuestStep => _viewModel.StepIndex;

    /// <summary>L'étape à retrouver, le temps que la page annonce les siennes.</summary>
    private int _pendingStep;

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _viewModel.OpenInBrowser();

    /// <summary>
    /// Ouvre le formulaire de signalement du site sur la page qu'on lit.
    ///
    /// Dans une fenêtre à part, et non dans celle-ci : la fenêtre de quêtes
    /// tient un bandeau, des étapes et une chaîne qui décrivent la quête, et
    /// rien de tout cela ne vaut pour un formulaire.
    /// </summary>
    private async void OnReportError(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ErrorReport() is not { } demande)
        {
            return;
        }

        try
        {
            var page = AppHost.Services.GetRequiredService<QuestPageWindow>();

            await page
                .ShowReportAsync(demande.Url, "Remonter une erreur sur Papycha", demande.Location)
                .ConfigureAwait(true);

            LogReportOpened(demande.Url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPageFailed(demande.Url, exception.Message);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Formulaire de signalement ouvert sur : {url}")]
    private partial void LogReportOpened(string url);

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
        if (_viewModel.Activate(NodeList.SelectedItem as QuestNode) is { } url)
        {
            Open(url);
        }
    }

    private bool _bridgeReady;

    /// <summary>Charge la page d'une quête, en s'assurant que le moteur est prêt.</summary>
    private async void Open(string url)
    {
        try
        {
            await PrepareAsync().ConfigureAwait(true);

            NavigateTo(url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Sans moteur d'exécution, la fenêtre ne peut rien montrer : le
            // dire vaut mieux que laisser une zone vide. ReportViewFailure
            // baisse aussi l'attente, qui vient peut-être d'être levée.
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
        // L'environnement dit au moteur où écrire et combien garder. Sans lui,
        // il pose son cache à côté de l'exécutable et le laisse enfler.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

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

        View.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();

            if (_viewModel.IsLoadingPage)
            {
                LogWaitedInVain();
                _viewModel.IsLoadingPage = false;
            }
        };

        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script())
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Charge une page du site en disant qu'on l'attend.
    ///
    /// Seule porte : quatre chemins menaient à Navigate, et l'attente devait
    /// être levée aux quatre. Elle retombe au message du pont, qui ne vient
    /// qu'une fois la page cadrée, ou à défaut à la fin de la navigation.
    /// </summary>
    private void NavigateTo(string url)
    {
        _viewModel.IsLoadingPage = true;

        LogNavigate(url, View.CoreWebView2 is not null);

        View.CoreWebView2?.Navigate(url);

        // Un garde-fou, et non un correctif : la page annonce sa venue par deux
        // chemins, le pont et la fin de navigation, et il a suffi qu'aucun des
        // deux ne parle pour que l'indicateur tourne sans fin. Passé ce délai,
        // on rend la vue plutôt que de laisser tourner ; le journal dit alors
        // qu'on a attendu pour rien.
        _watchdog.Stop();
        _watchdog.Start();
    }

    /// <summary>
    /// Délai au-delà duquel on cesse d'attendre une page. Vingt secondes : une
    /// page du site en met une ou deux, et l'on ne coupe donc jamais une
    /// attente légitime, même sur une connexion lente.
    /// </summary>
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Identifiant de la navigation qu'on attend. Zéro quand on n'attend rien.
    ///
    /// Toutes les navigations ne sont pas les nôtres : un lien de quête cliqué
    /// dans le guide est refusé, puis relancé par nos soins. La navigation
    /// refusée signale sa fin, et elle le faisait après que la nôtre avait
    /// commencé : l'attente s'éteignait aussitôt, et l'ancien guide restait à
    /// l'écran sans que rien n'indique qu'une page arrivait.
    /// </summary>
    private ulong _awaited;

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        LogNavigationCompleted(e.NavigationId, _awaited, e.IsSuccess, e.WebErrorStatus.ToString());

        // Le filet : une page en erreur, un réseau coupé, et le pont ne dira
        // jamais rien. L'indicateur tournerait alors sans fin.
        if (e.NavigationId != _awaited)
        {
            return;
        }

        _watchdog.Stop();
        _viewModel.IsLoadingPage = false;

        // Rien de plus n'est annoncé, et c'est délibéré : le moteur pose sa
        // propre page d'erreur, en français, avec un bouton pour réessayer.
        // Mesuré sur une adresse injoignable, elle s'affiche bel et bien. Y
        // superposer un message à nous était impossible de toute façon, la vue
        // web étant une fenêtre native qui se dessine au-dessus de tout élément
        // WPF du même châssis.
    }

    private void OnBridgeMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string payload;

        try
        {
            payload = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Le message n'est pas du texte. Le pont n'en poste jamais d'autre,
            // mais il est posé sur tout document que cette fenêtre charge, et
            // toute page peut appeler « postMessage » avec ce qu'elle veut.
            return;
        }

        var message = QuestBridgeMessage.Parse(payload);

        if (message is null)
        {
            return;
        }

        switch (message.Kind)
        {
            case QuestBridgeMessage.Loaded:
                LogBridgeLoaded();
                _watchdog.Stop();
                _viewModel.IsLoadingPage = false;
                _viewModel.SetPage(
                    message.Intro,
                    message.Chain,
                    message.Steps,
                    message.Departure);
                ResumeStep();
                break;

            case QuestBridgeMessage.Step:
                _viewModel.SetStep(message.Index);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Reprend le guide à l'étape où on l'avait laissé, une seule fois, et
    /// seulement si elle existe encore : le site peut avoir raccourci la page
    /// depuis, et sauter à une étape absente ne mènerait nulle part.
    /// </summary>
    private void ResumeStep()
    {
        var step = _pendingStep;

        _pendingStep = 0;

        if (step > 0 && step < _viewModel.StepCount)
        {
            GoToStep(step);
        }
    }

    /// <summary>
    /// Endort ou réveille le moteur de rendu selon que la fenêtre se montre ou
    /// se masque.
    ///
    /// Mesuré : quatre cent soixante-trois mégaoctets fenêtre ouverte, quatre
    /// cent dix-neuf une fois endormi, soit quarante-quatre rendus. Les six
    /// processus restent, seule leur mémoire de travail se relâche : c'est
    /// moins que ce qu'on pouvait espérer, et c'est gratuit. La page revient
    /// telle qu'on l'a laissée, défilement compris.
    ///
    /// Le moteur refuse de dormir tant qu'il se croit visible, et le dit par
    /// une erreur d'état, 0x8007139F. La vue est donc retirée avant, ce que le
    /// drapeau de visibilité de la fenêtre gouverne déjà.
    ///
    /// Sans moteur démarré, il n'y a rien à endormir : la fenêtre peut être
    /// masquée avant d'avoir jamais montré une page.
    /// </summary>
    private void OnVisibilityChanged()
    {
        var visible = IsVisible;

        // La vue est retirée avant qu'on demande le sommeil, et remise avant
        // qu'on réveille : le moteur refuse de dormir tant qu'il se croit
        // visible, et rend alors une erreur d'état, mesurée.
        _viewModel.IsWindowVisible = visible;

        // Après la passe de mise en page, faute de quoi la vue serait encore
        // visible au moment de la demande.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded, () => Doze(asleep: !visible));
    }

    private async void Doze(bool asleep)
    {
        try
        {
            if (View.CoreWebView2 is not { } engine)
            {
                return;
            }

            if (asleep)
            {
                LogDozed(await engine.TrySuspendAsync().ConfigureAwait(true));

                return;
            }

            engine.Resume();
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // Un moteur qui refuse de dormir ne coûte que de la mémoire ; le
            // réveiller de force ou s'en plaindre coûterait la fenêtre.
            LogDozeFailed(exception);
        }
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

    /// <summary>Une étape choisie dans la liste : on s'y rend, et la liste se referme.</summary>
    private void OnPickStep(object sender, RoutedEventArgs e)
    {
        StepsToggle.IsChecked = false;

        if ((sender as FrameworkElement)?.DataContext is ViewModels.QuestStepRowViewModel row)
        {
            GoToStep(row.Index);
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
        var ours = SamePage(e.Uri, _viewModel.CurrentUrl);

        LogNavigationStarting(e.Uri, e.NavigationId, ours, e.IsRedirected);

        if (ours)
        {
            // Celle-ci est la nôtre : c'est sa fin qui lèvera l'attente.
            _awaited = e.NavigationId;

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
        // Hors du site, rien n'entre dans nos fenêtres : elles n'ont pas de
        // barre d'adresse, elles portent notre cadre, et le pont y est posé sur
        // tout document. Un guide qui renvoie au wiki ou à une vidéo s'ouvre
        // donc dans le navigateur, où l'on voit où l'on va.
        if (!PapychaSite.Owns(url))
        {
            LogSentOutside(url);
            _dialogs.OpenUrl(url);

            return;
        }

        if (!_viewModel.TryFollowUrl(url, remember: true))
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
        _ = Dispatcher.BeginInvoke(() => NavigateTo(url));
    }

    /// <summary>
    /// Vrai quand deux adresses ne diffèrent que par leur ancre.
    ///
    /// Une page de donjon propose « Aller directement à la mécanique du
    /// donjon », qui est une ancre. Le moteur l'annonce comme une navigation,
    /// et la traiter comme étrangère ouvrait une seconde fenêtre sur la page
    /// qu'on était déjà en train de lire.
    /// </summary>
    private static bool SamePage(string? first, string? second) =>
        Same(WithoutFragment(first), WithoutFragment(second));

    private static string WithoutFragment(string? url)
    {
        var value = url ?? string.Empty;
        var cut = value.IndexOf('#', StringComparison.Ordinal);

        return cut < 0 ? value : value[..cut];
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Adresse hors du site confiée au navigateur : {url}")]
    private partial void LogSentOutside(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Page liée non ouverte ({reason}) : {url}")]
    private partial void LogPageFailed(string url, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La place du suivi de quêtes n'a pas pu être rétablie.")]
    private partial void LogPlacementFailed(Exception exception);

    // Le chemin de chargement d'une page, tracé de bout en bout : l'indicateur
    // d'attente s'est déjà bloqué deux fois, et sans ces traces il a fallu
    // deviner. Elles disent qui demande, qui commence, qui finit.
    [LoggerMessage(Level = LogLevel.Information, Message = "Chargement demandé : {url} (moteur prêt : {ready})")]
    private partial void LogNavigate(string url, bool ready);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Navigation {id} commencée : {url} (à nous : {ours}, redirection : {redirected})")]
    private partial void LogNavigationStarting(string url, ulong id, bool ours, bool redirected);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Navigation {id} terminée (attendue : {awaited}, succès : {success}, état : {status})")]
    private partial void LogNavigationCompleted(ulong id, ulong awaited, bool success, string status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Moteur de rendu mis en sommeil : {asleep}.")]
    private partial void LogDozed(bool asleep);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Le moteur de rendu n'a pas pu être endormi.")]
    private partial void LogDozeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Le pont annonce la page chargée.")]
    private partial void LogBridgeLoaded();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Aucune nouvelle de la page après vingt secondes : la vue est rendue.")]
    private partial void LogWaitedInVain();


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

    /// <summary>Revient sur la quête d'où l'on vient.</summary>
    private void OnGoBackQuest(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBackQuest() is { } url)
        {
            NavigateTo(url);
        }
    }

    private void OnClearQuery(object sender, RoutedEventArgs e)
    {
        _viewModel.Query = string.Empty;

        SearchBox.Focus();
    }

    private void OnCloseList(object sender, RoutedEventArgs e) => _viewModel.IsListOpen = false;

    /// <summary>
    /// Épingle les prérequis d'une ligne, pour qu'on puisse les lire sans tenir
    /// la souris et cliquer ceux qui mènent à une quête.
    ///
    /// L'infobulle du même bouton est éteinte le temps du panneau : elle
    /// s'ouvrirait par-dessus et dirait la même chose sans les liens.
    /// </summary>
    /// <summary>
    /// Ouvre les prérequis d'une ligne, ou les referme si ce sont déjà ceux-là.
    /// </summary>
    private void OnShowNeeds(object sender, RoutedEventArgs e)
    {
        if (sender is not Button bouton)
        {
            return;
        }

        if (PanneauPrerequis.IsOpen
            && ReferenceEquals(PanneauPrerequis.PlacementTarget, bouton))
        {
            CloseNeeds();

            return;
        }

        ToolTipService.SetIsEnabled(bouton, false);

        PanneauPrerequis.PlacementTarget = bouton;
        PanneauPrerequis.DataContext = bouton.DataContext;
        PanneauPrerequis.IsOpen = true;
    }

    /// <summary>
    /// Referme les prérequis dès qu'on clique ailleurs que sur le cadenas qui
    /// les a ouverts.
    ///
    /// Le clic sur ce cadenas est laissé passer : c'est lui qui referme, et le
    /// fermer ici le rouvrirait aussitôt. Un clic dans le panneau lui-même ne
    /// vient jamais jusqu'ici, une fenêtre surgissante ayant la sienne.
    /// </summary>
    private void OnWindowPressed(object sender, MouseButtonEventArgs e)
    {
        if (!PanneauPrerequis.IsOpen
            || (e.OriginalSource is DependencyObject source
                && Owns(PanneauPrerequis.PlacementTarget as DependencyObject, source)))
        {
            return;
        }

        CloseNeeds();
    }

    /// <summary>Vrai si le second élément est dans l'arbre visuel du premier.</summary>
    private static bool Owns(DependencyObject? parent, DependencyObject? child)
    {
        if (parent is null)
        {
            return false;
        }

        for (var at = child; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, parent))
            {
                return true;
            }
        }

        return false;
    }

    private void CloseNeeds()
    {
        if (PanneauPrerequis.PlacementTarget is Button bouton)
        {
            ToolTipService.SetIsEnabled(bouton, true);
        }

        PanneauPrerequis.IsOpen = false;
    }

    /// <summary>Ouvre la quête qu'un prérequis nomme.</summary>
    private void OnFollowNeed(object sender, RoutedEventArgs e)
    {
        PanneauPrerequis.IsOpen = false;

        if ((sender as FrameworkElement)?.Tag is QuestSummary quest)
        {
            Route(quest.Url);
        }
    }

    /// <summary>Suit un lien de chaîne : la quête précédente ou la suivante.</summary>
    private void OnFollowChain(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is QuestLink link)
        {
            _viewModel.Follow(link);
            NavigateTo(link.Url);
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

    /// <summary>
    /// La fenêtre vient d'obtenir sa poignée : c'est le moment de la remettre
    /// où elle était. Plus tôt il n'y aurait rien à placer, plus tard on la
    /// verrait sauter.
    /// </summary>
    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            _placements.Restore(this, WindowPlacements.Quests, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPlacementFailed(exception);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // La croix masque, elle ne ferme pas : la place est donc retenue ici,
        // faute de quoi fermer la fenêtre à la croix l'oublierait.
        _ = _placements.SaveAsync(this, WindowPlacements.Quests);

        e.Cancel = true;
        Hide();
    }
}
