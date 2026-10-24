using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Dofus;
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
    private readonly Func<WebToolWindow> _trackerWindow;

    /// <summary>The linking window while it is open: one at a time.</summary>
    private WebToolWindow? _linking;

    /// <summary>The step the page was resumed at, 0 when none.</summary>
    private int _resumedStep;

    public QuestWindow(
        QuestViewModel viewModel,
        WindowPlacements placements,
        SettingsService settings,
        WebViewEnvironment engine,
        IDialogService dialogs,
        Func<WebToolWindow> trackerWindow,
        ILogger<QuestWindow> logger)
    {
        InitializeComponent();

        if (PageTranslator.Offers(PageTranslation.French))
        {
            Translate.Visibility = Visibility.Visible;
            OpenPage.Visibility = Visibility.Collapsed;
        }

        _viewModel = viewModel;
        _placements = placements;
        _settings = settings;
        _engine = engine;
        _dialogs = dialogs;
        _trackerWindow = trackerWindow;
        _logger = logger;
        DataContext = viewModel;

        // A character picked in the bar is shown in the tracker window too,
        // when it is open on the player's quests.
        viewModel.Tracker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestTrackerViewModel.SelectedCharacter)
                && _linking is { IsLoaded: true, ShowsQuests: true } window
                && viewModel.Tracker.SelectedCharacter is { } character)
            {
                _ = window.ShowCharacterAsync(character.Id);
            }
        };

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

        // The tracker is read each time the window shows, not only when a
        // page opens: the list alone needs the link and the marks, and a
        // status may have changed elsewhere since. Queued below the
        // Loaded-priority wake-up of OnVisibilityChanged, so the cookie is
        // never asked of an engine still asleep.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background, () => _ = PrepareOnShowAsync());

        // A guide that was being read comes back as it was left: players
        // hide it to see the game, then need their spot in the quest again.
        if (_viewModel.HasQuest && !_viewModel.IsListOpen)
        {
            View.Focus();
            return;
        }

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
            _ = PrepareOnShowAsync();

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

            return;
        }

        // Prepared already: this only reads the tracker, as any show does.
        _ = PrepareOnShowAsync();
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

    private void OnTranslate(object sender, RoutedEventArgs e) => _viewModel.OpenTranslated();

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
    /// What every show does: prepare the engine, then read the tracker. The
    /// read waits for the preparation, which hands the view model its cookie.
    /// </summary>
    private async Task PrepareOnShowAsync()
    {
        try
        {
            await PrepareAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // No engine: the list still works, the page cannot show.
            _viewModel.ReportViewFailure(exception);

            return;
        }

        await _viewModel.Tracker.RefreshOnShowAsync().ConfigureAwait(true);
    }

    /// <summary>One preparation for the window's life: later calls share it.</summary>
    private async Task PrepareAsync()
    {
        var task = _preparing ??= PrepareCoreAsync();

        try
        {
            await task.ConfigureAwait(true);
        }
        catch
        {
            // A failed preparation is dropped so the next attempt starts
            // again, unless a newer one already replaced it.
            if (ReferenceEquals(_preparing, task))
            {
                _preparing = null;
            }

            throw;
        }
    }

    private Task? _preparing;

    /// <summary>
    /// Starts the engine and installs the bridge in it, only once.
    ///
    /// The script is set before any document loads: set afterward, it
    /// would let the site's own look show through for an instant.
    /// </summary>
    private async Task PrepareCoreAsync()
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

        // The tracker cookie lives in this WebView's profile: the view
        // model reads and clears it through the engine, never sees the
        // access phrase.
        var cookies = View.CoreWebView2.CookieManager;

        _viewModel.Tracker.CookieSource = async () =>
        {
            var found = await cookies.GetCookiesAsync(PapychaSite.Root).ConfigureAwait(true);

            return found.FirstOrDefault(c => c.Name == PapychaSite.TrackerCookie)?.Value;
        };
        _viewModel.Tracker.ForgetCookie = () =>
        {
            cookies.DeleteCookies(PapychaSite.TrackerCookie, PapychaSite.Root);

            return Task.CompletedTask;
        };
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
                    message.Steps);
                ResumeStep();
                break;

            case QuestBridgeMessage.Step:
                _viewModel.SetStep(message.Index);

                // A late step of the previous page (inertial scroll) must not
                // start the quest now being loaded.
                if (!_viewModel.IsLoadingPage)
                {
                    _ = _viewModel.Tracker.OnStepAsync(message.Index, _viewModel.StepCount, _resumedStep, deliberate: false);
                }

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
        _resumedStep = 0;

        if (step > 0 && step < _viewModel.StepCount)
        {
            _resumedStep = step;
            GoToStep(step);
        }
    }

    private void OnRewardLevelKey(object sender, KeyEventArgs e) => ApplyOnEnter(e, OnRewardLevelApply, sender);

    private static void ApplyOnEnter(KeyEventArgs e, RoutedEventHandler apply, object sender)
    {
        if (e.Key == Key.Enter)
        {
            apply(sender, e);
            e.Handled = true;
        }
    }

    // SetReward* contain their own failures; a refused text puts the old value back.
    private void OnRewardLevelApply(object sender, RoutedEventArgs e) =>
        _ = _viewModel.Tracker.SetRewardLevelAsync(((TextBox)sender).Text);

    private void OnBonusPackClick(object sender, RoutedEventArgs e) =>
        _ = _viewModel.Tracker.SetRewardXpBonusAsync(
            ((CheckBox)sender).IsChecked == true ? QuestTrackerViewModel.BonusPackPercent : 0);

    private void OnLinkTracker(object sender, RoutedEventArgs e) => OpenTrackerWindow(quests: false);

    private void OnOpenTracker(object sender, RoutedEventArgs e) => OpenTrackerWindow(quests: true);

    private void OnUnlinkTracker(object sender, RoutedEventArgs e)
    {
        TrackerMenuToggle.IsChecked = false;

        // UnlinkAsync contains its own failures.
        _ = _viewModel.Tracker.UnlinkAsync();
    }

    /// <summary>
    /// Writes "completed", or once it is, opens the status menu: writing it
    /// again would change nothing, the menu is how to undo it.
    /// </summary>
    private void OnMarkDone(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Tracker.IsDone)
        {
            StatusMenuToggle.IsChecked = true;

            return;
        }

        _ = _viewModel.Tracker.SetStatusAsync(QuestStatus.Completed);
    }

    private void OnPickStatus(object sender, RoutedEventArgs e)
    {
        StatusMenuToggle.IsChecked = false;

        if (((FrameworkElement)sender).Tag is QuestStatus status)
        {
            // SetStatusAsync contains its own failures.
            _ = _viewModel.Tracker.SetStatusAsync(status);
        }
    }

    /// <summary>
    /// Opens the tracker page, one window at a time: to sign in, or on the
    /// player's quests. Signing in, the window closes itself when a new
    /// cookie appears; whatever closes it, the tracker is read again, since
    /// the player may have changed statuses there.
    /// </summary>
    private void OpenTrackerWindow(bool quests)
    {
        // Held from the opening, not from Loaded: a second click before Loaded
        // opened a second window. Visible, so that one whose opening threw
        // does not hold the place for good.
        if (_linking is { IsVisible: true })
        {
            _linking.Activate();

            return;
        }

        var window = _trackerWindow();

        window.Owner = this;
        window.Tool = WebTool.PapychaTracker;
        window.ShowsQuests = quests;
        window.TrackerCharacterId = _viewModel.Tracker.SelectedCharacter?.Id;
        window.CharacterPicked += (_, id) => _viewModel.Tracker.PickCharacter(id);
        window.GuideRequested += (_, url) =>
        {
            Route(url);
            Activate();
        };
        window.Closed += (_, _) =>
        {
            _linking = null;
            _ = _viewModel.Tracker.RefreshAsync();
        };
        _linking = window;
        window.Show();
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

            // The bridge posts no Step message for a requested jump, so the
            // tracker hears of it here. A move reported by both paths is
            // counted once: OnStepAsync starts a quest at most once. Not
            // while a page loads: the step belongs to the one leaving.
            if (!_viewModel.IsLoadingPage)
            {
                _ = _viewModel.Tracker.OnStepAsync(index, _viewModel.StepCount, _resumedStep, deliberate: true);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _viewModel.ReportViewFailure(exception);
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
        Message = "Noms officiels du jeu non lus : le catalogue reste celui de Papycha.")]
    private partial void LogNamesFailed(Exception exception);

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

    private void OnRecommendedClick(object sender, RoutedEventArgs e) => _viewModel.ToggleRecommended();

    private void OnPickClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuestNode node })
        {
            _viewModel.Pick(node, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }
    }

    // The batch methods never throw: the tracker turns every failure into a result.
    private async void OnMarkAllClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuestNode node })
        {
            await _viewModel.MarkAllAsync(node).ConfigureAwait(true);
        }
    }

    private async void OnMarkPickedClick(object sender, RoutedEventArgs e) =>
        await _viewModel.MarkPickedAsync().ConfigureAwait(true);

    private void OnCancelBatchClick(object sender, RoutedEventArgs e) => _viewModel.CancelBatch();

    /// <summary>Each open gain hover's read, by the element carrying it: its closing cancels that read only.</summary>
    private readonly Dictionary<object, CancellationTokenSource> _tipReads = [];

    /// <summary>Fills the gain's hover with where the quest's items come from, once read; the hover stays open meanwhile.</summary>
    private async void OnGainTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: QuestNode { Quest: { } quest } node, ToolTip: ToolTip { Content: StackPanel panel } }
            || panel.Children.Count < 2
            || panel.Children[1] is not TextBlock sources)
        {
            return;
        }

        if (_tipReads.Remove(sender, out var stale))
        {
            stale.Cancel();
        }

        using var read = new CancellationTokenSource();
        _tipReads[sender] = read;

        try
        {
            // Never throws: a cancelled read gives null.
            var text = await _viewModel.WhereToFindAsync(quest, read.Token).ConfigureAwait(true);

            // The row may show another quest by now.
            if (!ReferenceEquals(((FrameworkElement)sender).DataContext, node))
            {
                return;
            }

            sources.Text = text ?? string.Empty;
            sources.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            // Dropped before the source is disposed, so a later closing cannot cancel a disposed source.
            if (_tipReads.TryGetValue(sender, out var mine) && ReferenceEquals(mine, read))
            {
                _tipReads.Remove(sender);
            }
        }
    }

    private void OnGainTipClosing(object sender, ToolTipEventArgs e)
    {
        if (_tipReads.Remove(sender, out var read))
        {
            read.Cancel();
        }
    }

    /// <summary>
    /// The tracker bar's right-hand group (link, or status buttons, "Mes quêtes" and the menu) goes under the
    /// character's line when both no longer fit: at the 440 px minimum with a guide open, about 310 px of buttons
    /// left the character selector some 90 px of its 150.
    /// </summary>
    private void OnTrackerBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The selector and its margin; the level boxes already wrap under it.
        const double characterLine = 160;
        var under = TrackerBar.ActualWidth - TrackerRight.DesiredSize.Width < characterLine;

        if (under == (Grid.GetRow(TrackerRight) == 1))
        {
            return;
        }

        Grid.SetRow(TrackerRight, under ? 1 : 0);
        Grid.SetColumn(TrackerRight, under ? 0 : 1);
        Grid.SetColumnSpan(TrackerRight, under ? 2 : 1);
        TrackerRight.HorizontalAlignment = under ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        TrackerRight.Margin = under ? new Thickness(0, 6, 0, 0) : default;
    }

    private void OnProfitMe(object sender, RoutedEventArgs e) => _viewModel.ShowTeam(false);

    private void OnProfitTeam(object sender, RoutedEventArgs e) => _viewModel.ShowTeam(true);

    private void OnProfitXp(object sender, RoutedEventArgs e) => _viewModel.ShowKamas(false);

    private void OnProfitKamas(object sender, RoutedEventArgs e) => _viewModel.ShowKamas(true);

    private void OnProfitQuests(object sender, RoutedEventArgs e) => _viewModel.ShowSeries(false);

    private void OnProfitSeries(object sender, RoutedEventArgs e) => _viewModel.ShowSeries(true);

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
            // Moving on to the next quest often means this one is done, not
            // always: it is offered, never written on its own.
            var left = link == _viewModel.NextQuest ? _viewModel.CurrentQuest : null;

            _viewModel.Follow(link);
            NavigateTo(link.Url);

            if (left is not null)
            {
                _viewModel.Tracker.OfferToFinish(left.Id, left.Title);
            }
        }
    }

    // AcceptFinishOfferAsync contains its own failures, like every write.
    private void OnAcceptFinish(object sender, RoutedEventArgs e) => _ = _viewModel.Tracker.AcceptFinishOfferAsync();

    private void OnDismissFinish(object sender, RoutedEventArgs e) => _viewModel.Tracker.DismissFinishOffer();

    // Both contain their own failures, like every write.
    private void OnFinishForOther(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TrackerCharacter character)
        {
            _ = _viewModel.Tracker.FinishForAsync(character);
        }
    }

    private void OnFinishForAll(object sender, RoutedEventArgs e) => _ = _viewModel.Tracker.FinishForAllAsync();

    private bool _indexingReported;
    private bool _namesReported;

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

        if (!_namesReported)
        {
            _namesReported = true;
            _ = ReportNamesAsync();
        }
    }

    /// <summary>Logs once why the game's names could not be read, if so.</summary>
    private async Task ReportNamesAsync()
    {
        await _viewModel.NamesRead.ConfigureAwait(true);

        if (_viewModel.NamesFailure is { } failure)
        {
            LogNamesFailed(failure);
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
