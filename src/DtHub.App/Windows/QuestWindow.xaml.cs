using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace DtHub.App.Windows;

/// <summary>
/// Quest tracking window: a search, and the guide page displayed just as
/// the site renders it.
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

        // The rendering engine keeps six processes and half a gigabyte
        // when the window is hidden, which is the case most of the time:
        // guides are opened to read a step, then play resumes. It can go
        // to sleep, provided nothing else is asked of it.
        IsVisibleChanged += (_, _) => OnVisibilityChanged();

        Loaded += async (_, _) => await IndexAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Shows the window if it is hidden, hides it otherwise.
    /// </summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            // The spot is saved at the moment the window is hidden: it
            // could no longer be recovered afterward, since the window
            // no longer has a screen position worth keeping.
            _ = _placements.SaveAsync(this, WindowPlacements.Quests);

            Hide();
            return;
        }

        Show();
        Activate();

        // The cursor in the search box, and the list expanded: this
        // window is opened to find a quest, never to look at emptiness.
        SearchBox.Focus();
        _viewModel.OpenList();
    }

    /// <summary>
    /// Reopens the window on the quest that was being read at the last
    /// stop.
    ///
    /// The list does not expand in this case: the page is found where it
    /// was left, which is exactly what was being sought.
    /// </summary>
    public async Task RestoreAsync(string? url, int step = 0)
    {
        Show();

        // The step waits until the page has said how many it has: the
        // bridge only reports this once the document is read, and
        // jumping before that would lead nowhere.
        _pendingStep = step;

        // The catalogue must be there before a quest is requested from
        // it. It ordinarily loads on first display, but nothing
        // guarantees it has finished: without this wait, the saved
        // address missed its mark and the window opened on its list.
        await IndexAsync().ConfigureAwait(true);

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

    /// <summary>
    /// The address of what was being read, to find it again on the
    /// next launch.
    /// </summary>
    public string? LastQuestUrl => _viewModel.CurrentUrl;

    /// <summary>
    /// The step reached, to return to it on the next launch.
    /// </summary>
    public int LastQuestStep => _viewModel.StepIndex;

    /// <summary>
    /// The step to restore, until the page announces its own.
    /// </summary>
    private int _pendingStep;

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _viewModel.OpenInBrowser();

    private void OnSearchOnSite(object sender, RoutedEventArgs e) => _viewModel.OpenSiteSearch();

    /// <summary>
    /// Opens the site's report form on the page being read.
    ///
    /// In a separate window, not this one: the quest window holds a
    /// banner, steps, and a chain describing the quest, and none of that
    /// applies to a form.
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
                .ShowReportAsync(demande.Url, Strings.Get("ReportOnPapycha"), demande.Location)
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
    /// Clicking in the search box expands the list, as an ordinary
    /// dropdown list would: it is the gesture made without thinking when
    /// one does not yet know what one is looking for.
    /// </summary>
    private void OnSearchClicked(object sender, MouseButtonEventArgs e) => OpenList();

    private void OnSearchFocused(object sender, KeyboardFocusChangedEventArgs e) => OpenList();

    /// <summary>
    /// Expands the list, then brings the open quest into view.
    /// Scrolling goes through the dispatcher queue: row containers are
    /// only created after rendering, and scrolling before that leads
    /// nowhere.
    /// </summary>
    private void OpenList()
    {
        _viewModel.OpenList();

        Dispatcher.BeginInvoke(ScrollToSelection, DispatcherPriority.Loaded);
    }

    private void OnNodeChosen(object sender, MouseButtonEventArgs e) => ChooseSelected();

    /// <summary>
    /// From the search box, the down arrow enters the list: it is the
    /// expected gesture of any dropdown list, and it avoids letting go
    /// of the keyboard to reach for the mouse.
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

        // The first row can be a subheading: we go down to the first
        // one that can actually be selected.
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

    /// <summary>
    /// Enter counts as a click: the list can also be browsed with the
    /// keyboard.
    /// </summary>
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

    /// <summary>
    /// Loads a quest's page, making sure the engine is ready.
    /// </summary>
    private async void Open(string url)
    {
        try
        {
            await PrepareAsync().ConfigureAwait(true);

            NavigateTo(url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Without a running engine, the window cannot show anything:
            // saying so is better than leaving an empty area.
            // ReportViewFailure also lowers the waiting flag, which may
            // have just been raised.
            _viewModel.ReportViewFailure(exception);
        }
    }

    /// <summary>
    /// Starts the engine and installs the bridge in it, only once.
    ///
    /// The script is set before any document loads: set afterward, it
    /// would let the site's own look show through for an instant.
    /// </summary>
    private async Task PrepareAsync()
    {
        // The environment tells the engine where to write and how much
        // to keep. Without it, it puts its cache next to the executable
        // and lets it grow.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        if (_bridgeReady)
        {
            return;
        }

        _bridgeReady = true;

        View.CoreWebView2.WebMessageReceived += OnBridgeMessage;

        // Clicking a link in the guide used to navigate this window in
        // place: the banner kept the old quest while the steps became
        // those of the new page. Any navigation not requested therefore
        // now goes to a separate window.
        View.CoreWebView2.NavigationStarting += OnNavigationStarting;

        // And target="_blank", which the runtime would open in a window
        // outside any control, without our frame or our foreground
        // state.
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
    /// Loads a page of the site, marking that it is awaited.
    ///
    /// A single gate: four paths used to lead to Navigate, and the wait
    /// had to be lifted at all four. It now rests on the bridge message,
    /// which only comes once the page is framed, or failing that, at
    /// the end of navigation.
    /// </summary>
    private void NavigateTo(string url)
    {
        _viewModel.IsLoadingPage = true;

        LogNavigate(url, View.CoreWebView2 is not null);

        View.CoreWebView2?.Navigate(url);

        // A safeguard, not a fix: the page announces its arrival through
        // two paths, the bridge and the end of navigation, and it took
        // only neither of them speaking up for the indicator to spin
        // forever. Past this delay, the view is released rather than
        // left spinning; the log then says the wait was for nothing.
        _watchdog.Stop();
        _watchdog.Start();
    }

    /// <summary>
    /// Delay beyond which a page is no longer waited for. Twenty
    /// seconds: a page of the site takes one or two, so a legitimate
    /// wait is never cut short, even on a slow connection.
    /// </summary>
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Identifier of the navigation being waited for. Zero when nothing
    /// is awaited.
    ///
    /// Not every navigation is ours: a quest link clicked in the guide
    /// is refused, then relaunched by us. The refused navigation signals
    /// its own end, and it used to do so after ours had already started:
    /// the wait would switch off at once, and the old guide would stay
    /// on screen with nothing indicating a page was on its way.
    /// </summary>
    private ulong _awaited;

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        LogNavigationCompleted(e.NavigationId, _awaited, e.IsSuccess, e.WebErrorStatus.ToString());

        // The safety net: a page in error, a cut network, and the
        // bridge will never say anything. The indicator would then
        // spin forever.
        if (e.NavigationId != _awaited)
        {
            return;
        }

        _watchdog.Stop();
        _viewModel.IsLoadingPage = false;

        // Nothing more is reported, and that is deliberate: the engine
        // shows its own error page, in French, with a retry button.
        // Measured against an unreachable address, it does show up.
        // Overlaying a message of our own was impossible anyway, since
        // the web view is a native window drawn on top of every WPF
        // element in the same frame.
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
            // The message is not text. The bridge never posts anything
            // else, but it is installed on every document this window
            // loads, and any page can call "postMessage" with whatever
            // it wants.
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
    /// Resumes the guide at the step where it was left, only once, and
    /// only if that step still exists: the site may have shortened the
    /// page since, and jumping to a missing step would lead nowhere.
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
    /// Puts the rendering engine to sleep or wakes it, depending on
    /// whether the window shows or hides.
    ///
    /// Measured: four hundred and sixty-three megabytes with the window
    /// open, four hundred and nineteen once asleep, that is forty-four
    /// megabytes freed. The six processes remain, only their working
    /// memory relaxes: it is less than one could hope for, and it is
    /// free. The page comes back exactly as it was left, scroll
    /// position included.
    ///
    /// The engine refuses to sleep as long as it believes itself
    /// visible, and says so with a state error, 0x8007139F. The view is
    /// therefore removed beforehand, which the window's visibility flag
    /// already governs.
    ///
    /// With no engine started, there is nothing to put to sleep: the
    /// window can be hidden before it has ever shown a page.
    /// </summary>
    private void OnVisibilityChanged()
    {
        var visible = IsVisible;

        // The view is removed before sleep is requested, and put back
        // before waking: the engine refuses to sleep as long as it
        // believes itself visible, and returns a state error, as
        // measured.
        _viewModel.IsWindowVisible = visible;

        // After the layout pass, otherwise the view would still be
        // visible at the moment of the request.
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
            // An engine that refuses to sleep only costs memory; forcing
            // it awake or complaining about it would cost the window.
            LogDozeFailed(exception);
        }
    }

    /// <summary>Scrolls the page to a step.</summary>
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

    /// <summary>
    /// A step chosen from the list: it is navigated to, and the list
    /// closes.
    /// </summary>
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
    /// Diverts to a separate window any navigation that is not the page
    /// that was requested.
    ///
    /// The comparison is made without the trailing slash: the site
    /// renders sometimes one form, sometimes the other, and sticking to
    /// strict equality would divert the page just opened.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var ours = SamePage(e.Uri, _viewModel.CurrentUrl);

        LogNavigationStarting(e.Uri, e.NavigationId, ours, e.IsRedirected);

        if (ours)
        {
            // This one is ours: it is its end that will lift the wait.
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
    /// Sends an address wherever it reads best: in place if it is a
    /// quest from the catalogue, in a separate window otherwise.
    /// </summary>
    private void Route(string url)
    {
        // Outside the site, nothing enters our windows: they have no
        // address bar, they carry our frame, and the bridge is set on
        // every document there. A guide linking to the wiki or to a
        // video therefore opens in the browser, where one can see where
        // one is going.
        if (!PapychaSite.Owns(url))
        {
            LogSentOutside(url);
            _dialogs.OpenUrl(url);

            return;
        }

        // The success tree goes to the browser, and it is the only page
        // of the site in this case. It is not a guide but a tool that
        // is expanded and browsed through: opened in one of our
        // windows, it displayed fine but imposed one more step before
        // the button that finally led where one wanted to go. See
        // PapychaSite.IsSuccessTree.
        if (PapychaSite.IsSuccessTree(url))
        {
            LogTreeSentOutside(url);
            _dialogs.OpenUrl(url);

            return;
        }

        if (!_viewModel.TryFollowUrl(url, remember: true))
        {
            _ = OpenAsideAsync(url);

            return;
        }

        // The model is up to date, the page is not: the navigation
        // that brought us here has just been cancelled, and there is
        // no one else to relaunch it. Without this the banner would
        // announce the new quest above the old one's guide.
        //
        // Deferred, because we are still inside the handler that just
        // refused this same navigation.
        _ = Dispatcher.BeginInvoke(() => NavigateTo(url));
    }

    /// <summary>
    /// True when two addresses differ only by their anchor.
    ///
    /// A dungeon page offers "Aller directement à la mécanique du
    /// donjon", which is an anchor. The engine reports it as a
    /// navigation, and treating it as foreign would open a second
    /// window on the page already being read.
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
    /// Opens an address in its own window, above the others.
    ///
    /// The failure is logged: launched without being awaited, this task
    /// would otherwise carry off its exception in silence, and the
    /// click would remain without effect or explanation.
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

    /// <summary>
    /// The success tree, the only page of the site handed to the
    /// browser. Its own line, because the previous one said "outside
    /// the site" of an address that, in fact, is part of it: a log that
    /// lies about what it did is worth less than a silent log.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Arbre des succès ouvert dans le navigateur : {url}")]
    private partial void LogTreeSentOutside(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Page liée non ouverte ({reason}) : {url}")]
    private partial void LogPageFailed(string url, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "La place du suivi de quêtes n'a pas pu être rétablie.")]
    private partial void LogPlacementFailed(Exception exception);

    // The loading path of a page, traced end to end: the waiting
    // indicator has already gotten stuck twice, and without these
    // traces it had to be guessed. They say who requests, who starts,
    // who finishes.
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
        Level = LogLevel.Information,
        Message = "Catalogue indexé en {seconds:F1} s.")]
    private partial void LogIndexed(double seconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Aucune nouvelle de la page après vingt secondes : la vue est rendue.")]
    private partial void LogWaitedInVain();


    /// <summary>
    /// Brings the selected row into view. Setting it is not enough: in
    /// a category of sixty quests, it stays off screen.
    /// </summary>
    private void ScrollToSelection()
    {
        if (NodeList.SelectedItem is not null)
        {
            NodeList.ScrollIntoView(NodeList.SelectedItem);
        }
    }

    private void OnGoBack(object sender, RoutedEventArgs e) => _viewModel.GoBack();

    /// <summary>Goes back to the quest we came from.</summary>
    private void OnGoBackPage(object sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBackPage() is { } url)
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
    /// Opens a row's prerequisites, or closes them if they are already
    /// the ones shown. They can then be read without holding the mouse,
    /// and the ones that lead to a quest can be clicked.
    ///
    /// The same button's tooltip is switched off while the panel is
    /// open: it would otherwise open on top and say the same thing
    /// without the links.
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
    /// Closes the prerequisites as soon as a click lands anywhere other
    /// than the lock that opened them.
    ///
    /// A click on that lock is let through: it is the one that closes,
    /// and closing it here would reopen it at once. A click inside the
    /// panel itself never reaches here, a popup window having its own.
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

    /// <summary>
    /// True if the second element is in the visual tree of the first.
    /// </summary>
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

    /// <summary>Opens the quest a prerequisite names.</summary>
    private void OnFollowNeed(object sender, RoutedEventArgs e)
    {
        PanneauPrerequis.IsOpen = false;

        if ((sender as FrameworkElement)?.Tag is QuestSummary quest)
        {
            Route(quest.Url);
        }
    }

    /// <summary>
    /// Follows a chain link: the previous quest or the next one.
    /// </summary>
    private void OnFollowChain(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is QuestLink link)
        {
            _viewModel.Follow(link);
            NavigateTo(link.Url);
        }
    }

    private bool _indexingReported;

    /// <summary>
    /// Indexes, then logs the duration when there was something to
    /// index.
    ///
    /// **Nothing used to time indexing**, and the fifty seconds cited
    /// from memory in the project's decisions dated from a time when it
    /// made eight requests instead of some fifty. Any improvement was
    /// therefore judged by feel.
    ///
    /// Silent when the cache was enough, which is the ordinary case: a
    /// line per window opening would say nothing useful.
    /// </summary>
    private async Task IndexAsync()
    {
        await _viewModel.InitializeAsync().ConfigureAwait(true);

        // Reported once, because LastIndexing stays set for the whole session
        // while this method runs twice on the restore path: Show() raises
        // Loaded, which indexes, and RestoreAsync then awaits IndexAsync again
        // to be sure the catalogue is there. The second call indexes nothing
        // and used to log the first call's duration a second time, which read
        // as two indexings.
        if (!_indexingReported && _viewModel.LastIndexing is { } duree)
        {
            _indexingReported = true;
            LogIndexed(duree.TotalSeconds);
        }
    }

    /// <summary>
    /// Native handle, kept once and for all. It is read from the
    /// foreground watcher, which is not allowed to query a WPF window:
    /// querying it used to throw on every window change, and the
    /// hotkey would die without anything saying so. The configurator
    /// already took this precaution, this one had not.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>
    /// The window has just obtained its handle: this is the moment to
    /// put it back where it was. Any earlier there would be nothing to
    /// place, any later it would be seen jumping.
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

    /// <summary>
    /// The close button hides, it does not close: the window is a tool
    /// brought back with the hotkey, like the configurator.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        // The close button hides, it does not close: the spot is
        // therefore saved here, otherwise closing the window with the
        // close button would forget it.
        _ = _placements.SaveAsync(this, WindowPlacements.Quests);

        e.Cancel = true;
        Hide();
    }
}
