using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;
using DtHub.Mac.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DtHub.Mac.Views;

/// <summary>
/// The PC's QuestWindow (src/DtHub.App/Windows/QuestWindow.xaml.cs): a
/// search, and the guide page displayed just as the site renders it. The
/// same view model, the same bridge, the same routing of links; what
/// WebView2 did, the Mac's web view does under the same names. The engine
/// is not put to sleep when hidden: WebKit has no such call, and macOS
/// already throttles a page no one sees.
/// </summary>
internal sealed partial class QuestWindow : Window
{
    /// <summary>The PC's gutter of the tree, none in "Recommandées", whose rows start at the edge.</summary>
    public static readonly IValueConverter GutterWidth =
        new FuncValueConverter<bool, GridLength>(recommended => new GridLength(recommended ? 0 : 19));

    /// <summary>Without its achievement line, the banner's block gives back a bit of room.</summary>
    public static readonly IValueConverter SeriesMargin =
        new FuncValueConverter<bool, Thickness>(series => series ? default : new Thickness(0, 4));

    /// <summary>The same for a chain button without its series.</summary>
    public static readonly IValueConverter ChainMargin =
        new FuncValueConverter<bool, Thickness>(series => series ? default : new Thickness(0, 3));

    /// <summary>
    /// A quest row keeps the level column only when a quest of the list has
    /// a level; a dungeon row (Facts) always does.
    /// </summary>
    public static readonly IMultiValueConverter LevelShown =
        new FuncMultiValueConverter<object?, bool>(values =>
        {
            var list = values.ToList();
            return list.Count == 3 && list[0] is true && (list[1] is not null || list[2] is true);
        });

    private readonly QuestViewModel _viewModel = null!;
    private readonly ILogger<QuestWindow> _logger = null!;
    private readonly WindowPlacements _placements = null!;
    private readonly SettingsService _settings = null!;
    private readonly IDialogService _dialogs = null!;
    private readonly Func<WebToolWindow> _trackerWindow = null!;

    /// <summary>
    /// Delay beyond which a page is no longer waited for: a page of the
    /// site takes one or two seconds.
    /// </summary>
    private readonly DispatcherTimer _watchdog = new() { Interval = TimeSpan.FromSeconds(20) };

    /// <summary>Each open gain hover's read, by the element carrying it.</summary>
    private readonly Dictionary<object, CancellationTokenSource> _tipReads = [];

    /// <summary>The linking window while it is open: one at a time.</summary>
    private WebToolWindow? _linking;

    /// <summary>The step the page was resumed at, 0 when none.</summary>
    private int _resumedStep;

    /// <summary>The step to restore, until the page announces its own.</summary>
    private int _pendingStep;

    private bool _bridgeReady;
    private Task? _preparing;

    /// <summary>The navigation waited for, zero when none: not every navigation is ours.</summary>
    private ulong _awaited;

    private bool _retrying;
    private bool _indexingReported;
    private bool _namesReported;
    private bool _placed;

    /// <summary>Why the awaited page did not come, null while it did or is coming.</summary>
    private string? _failure;

    /// <summary>For the designer only.</summary>
    public QuestWindow() => InitializeComponent();

    public QuestWindow(
        QuestViewModel viewModel,
        WindowPlacements placements,
        SettingsService settings,
        IDialogService dialogs,
        Func<WebToolWindow> trackerWindow,
        ILogger<QuestWindow> logger)
    {
        InitializeComponent();

        if (PageTranslator.Offers(PageTranslation.French))
        {
            Translate.IsVisible = true;
            OpenPage.IsVisible = false;
        }

        _viewModel = viewModel;
        _placements = placements;
        _settings = settings;
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

        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                // The view is removed while hidden, and put back on return.
                _viewModel.IsWindowVisible = IsVisible;
            }
        };

        // The PC's Preview events: tunnelling, so they come before the
        // controls' own handling.
        AddHandler(PointerPressedEvent, OnWindowPressed, RoutingStrategies.Tunnel);
        SearchBox.AddHandler(PointerPressedEvent, OnSearchClicked, RoutingStrategies.Tunnel, handledEventsToo: true);
        SearchBox.AddHandler(KeyDownEvent, OnSearchKey, RoutingStrategies.Tunnel);
        SearchBox.GotFocus += (_, _) => OpenList();

        // The page shows where the view model says, unless it failed: then
        // the failure takes its place, the native view hidden above it.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuestViewModel.ShowsPage))
            {
                ShowPage();
            }
        };
        ShowPage();
        Failure.Retry += (_, _) =>
        {
            if ((_viewModel.CurrentUrl ?? View.LastNavigation) is { } url)
            {
                NavigateTo(url);
            }
        };

        // A click in the page never reaches Avalonia: the menus it would
        // close are closed here.
        View.NativePressed += (_, _) =>
        {
            StatusMenuToggle.IsChecked = false;
            TrackerMenuToggle.IsChecked = false;
            CloseNeeds();
        };

        // At its place from the first frame; OnOpenedAsync settles it once open.
        _placements.PlaceBeforeShow(this, () => WindowPlacements.Quests);

        Loaded += async (_, _) => await IndexAsync().ConfigureAwait(true);
        Opened += async (_, _) => await OnOpenedAsync().ConfigureAwait(true);
    }

    private void ShowPage()
    {
        View.IsVisible = _viewModel.ShowsPage && _failure is null;
        Failure.IsVisible = _viewModel.ShowsPage && _failure is not null;
        Failure.Reason = _failure;
    }

    /// <summary>
    /// Native handle, kept once and for all: it is read from the foreground
    /// watcher, which may not query the window.
    /// </summary>
    public nint Handle { get; private set; }

    /// <summary>The address of what was being read, to find it again on the next launch.</summary>
    public string? LastQuestUrl => _viewModel.CurrentUrl;

    /// <summary>The step reached, to return to it on the next launch.</summary>
    public int LastQuestStep => _viewModel.StepIndex;

    /// <summary>Shows the window if it is hidden, hides it otherwise.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            // The spot is saved at the moment the window is hidden.
            _ = _placements.SaveAsync(this, WindowPlacements.Quests);

            Hide();
            return;
        }

        Show();
        Activate();

        // The tracker is read each time the window shows: the list alone
        // needs the link and the marks.
        Dispatcher.UIThread.Post(() => _ = PrepareOnShowAsync(), DispatcherPriority.Background);

        // A guide that was being read comes back as it was left.
        // The page itself, once back in the window: Avalonia's focus on its
        // host alone would leave the keys with Avalonia.
        if (_viewModel.HasQuest && !_viewModel.IsListOpen)
        {
            Dispatcher.UIThread.Post(View.FocusPage, DispatcherPriority.Loaded);
            return;
        }

        // The cursor in the search box, and the list expanded.
        SearchBox.Focus();
        _viewModel.OpenList();
    }

    /// <summary>Reopens the window on the quest that was being read at the last stop.</summary>
    public async Task RestoreAsync(string? url, int step = 0)
    {
        Show();

        // The step waits until the page has said how many it has.
        _pendingStep = step;

        // The catalogue must be there before a quest is requested from it.
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

        _ = PrepareOnShowAsync();
    }

    private async Task OnOpenedAsync()
    {
        if (_placed)
        {
            return;
        }

        _placed = true;
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

    private void OnOpenInBrowser(object? sender, RoutedEventArgs e) => _viewModel.OpenInBrowser();

    private void OnTranslate(object? sender, RoutedEventArgs e) => _viewModel.OpenTranslated();

    private void OnSearchOnSite(object? sender, RoutedEventArgs e) => _viewModel.OpenSiteSearch();

    /// <summary>Opens the site's report form on the page being read, in a window of its own.</summary>
    private async void OnReportError(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ErrorReport() is not { } demande)
        {
            return;
        }

        try
        {
            var page = MacApp.Services.GetRequiredService<QuestPageWindow>();

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

    /// <summary>Clicking in the search box expands the list, as a dropdown would.</summary>
    private void OnSearchClicked(object? sender, PointerPressedEventArgs e) => OpenList();

    /// <summary>Expands the list, then brings the open quest into view, once its rows exist.</summary>
    private void OpenList()
    {
        _viewModel.OpenList();

        Dispatcher.UIThread.Post(ScrollToSelection, DispatcherPriority.Loaded);
    }

    /// <summary>A click on a row; one on a button or a box of the row is theirs.</summary>
    private void OnNodeChosen(object? sender, TappedEventArgs e)
    {
        var at = e.Source as Visual;

        for (; at is not null and not ListBoxItem; at = at.GetVisualParent())
        {
            // The scroll bar is no row: a click on it must not reopen the selected one.
            if (at is Button or ToggleButton or ScrollBar or Thumb)
            {
                return;
            }
        }

        if (at is ListBoxItem)
        {
            ChooseSelected();
        }
    }

    /// <summary>From the search box, the down arrow enters the list.</summary>
    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _viewModel.IsListOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Down || NodeList.ItemCount == 0)
        {
            return;
        }

        _viewModel.OpenList();

        // The first row can be a subheading: the first selectable one.
        var index = _viewModel.FirstSelectable();

        if (index < 0)
        {
            return;
        }

        NodeList.SelectedIndex = index;
        NodeList.ScrollIntoView(index);
        NodeList.UpdateLayout();
        NodeList.ContainerFromIndex(index)?.Focus(NavigationMethod.Directional);

        e.Handled = true;
    }

    /// <summary>Enter counts as a click: the list can also be browsed with the keyboard.</summary>
    private void OnNodeKey(object? sender, KeyEventArgs e)
    {
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

    /// <summary>Only a choice lights up under the pointer: the PC's row template trigger.</summary>
    private void OnRowPrepared(object? sender, ContainerPreparedEventArgs e) =>
        e.Container.Classes.Set("choice", e.Container.DataContext is QuestNode { IsEnabled: true });

    private void ChooseSelected()
    {
        if (_viewModel.Activate(NodeList.SelectedItem as QuestNode) is { } url)
        {
            Open(url);
        }
    }

    /// <summary>Loads a quest's page, making sure the engine is ready.</summary>
    private async void Open(string url)
    {
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

    /// <summary>What every show does: prepare the engine, then read the tracker.</summary>
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
            if (ReferenceEquals(_preparing, task))
            {
                _preparing = null;
            }

            throw;
        }
    }

    /// <summary>Installs the bridge in the engine, only once, before any document loads.</summary>
    private Task PrepareCoreAsync()
    {
        if (_bridgeReady)
        {
            return Task.CompletedTask;
        }

        if (!View.IsReady)
        {
            throw new PlatformNotSupportedException();
        }

        _bridgeReady = true;

        View.WebMessageReceived += OnBridgeMessage;

        // Any navigation not requested goes to a separate window.
        View.NavigationStarting += OnNavigationStarting;

        // And target="_blank".
        View.NewWindowRequested += OnNewWindowRequested;

        View.NavigationCompleted += OnNavigationCompleted;

        _watchdog.Tick += (_, _) =>
        {
            _watchdog.Stop();

            if (_viewModel.IsLoadingPage)
            {
                LogWaitedInVain();
                _viewModel.IsLoadingPage = false;
            }
        };

        View.AddScriptToExecuteOnDocumentCreated(QuestBridge.Script());

        // The tracker cookie lives in the web view's store: the view model
        // reads and clears it through the engine, never sees the phrase.
        _viewModel.Tracker.CookieSource = () => View.GetCookieAsync(PapychaSite.TrackerCookie, PapychaSite.Root);
        _viewModel.Tracker.ForgetCookie = () =>
        {
            View.DeleteCookies(PapychaSite.TrackerCookie, PapychaSite.Root);

            return Task.CompletedTask;
        };

        return Task.CompletedTask;
    }

    /// <summary>Loads a page of the site, marking that it is awaited: a single gate.</summary>
    private void NavigateTo(string url)
    {
        _viewModel.IsLoadingPage = true;
        _failure = null;
        ShowPage();

        LogNavigate(url, View.IsReady);

        if (View.IsReady)
        {
            View.Navigate(url);
        }

        // A safeguard: past this delay, the view is released rather than left spinning.
        _watchdog.Stop();
        _watchdog.Start();
    }

    private void OnNavigationCompleted(object? sender, WebNavigationCompleted e)
    {
        LogNavigationCompleted(e.NavigationId, _awaited, e.IsSuccess, e.WebErrorStatus);

        // The safety net: a page in error, a cut network, and the bridge
        // will never say anything.
        if (e.NavigationId != _awaited)
        {
            return;
        }

        // WebView2 shows its error page; WebKit an empty one: the reason
        // and a retry instead. A navigation stopped for another is no failure.
        if (!e.IsSuccess && !e.IsInterrupted)
        {
            _failure = e.Description;
        }

        _watchdog.Stop();
        _viewModel.IsLoadingPage = false;
        ShowPage();
    }

    private void OnBridgeMessage(object? sender, string payload)
    {
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
                _viewModel.SetPage(message.Intro, message.Chain, message.Steps);
                ResumeStep();
                break;

            case QuestBridgeMessage.Step:
                _viewModel.SetStep(message.Index);

                // A late step of the previous page must not start the quest now loading.
                if (!_viewModel.IsLoadingPage)
                {
                    _ = _viewModel.Tracker.OnStepAsync(message.Index, _viewModel.StepCount, _resumedStep, deliberate: false);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Resumes the guide at the step where it was left, once, if that step still exists.</summary>
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

    private void OnRewardLevelKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnRewardLevelApply(sender, e);
            e.Handled = true;
        }
    }

    // SetReward* contain their own failures; a refused text puts the old value back.
    private void OnRewardLevelApply(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
        {
            _ = _viewModel.Tracker.SetRewardLevelAsync(box.Text ?? string.Empty);
        }
    }

    private void OnBonusPackClick(object? sender, RoutedEventArgs e) =>
        _ = _viewModel.Tracker.SetRewardXpBonusAsync(
            ((CheckBox)sender!).IsChecked == true ? QuestTrackerViewModel.BonusPackPercent : 0);

    private void OnLinkTracker(object? sender, RoutedEventArgs e) => OpenTrackerWindow(quests: false);

    private void OnOpenTracker(object? sender, RoutedEventArgs e) => OpenTrackerWindow(quests: true);

    private void OnUnlinkTracker(object? sender, RoutedEventArgs e)
    {
        TrackerMenuToggle.IsChecked = false;

        // UnlinkAsync contains its own failures.
        _ = _viewModel.Tracker.UnlinkAsync();
    }

    /// <summary>Writes "completed", or once it is, opens the status menu.</summary>
    private void OnMarkDone(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Tracker.IsDone)
        {
            StatusMenuToggle.IsChecked = true;

            return;
        }

        _ = _viewModel.Tracker.SetStatusAsync(QuestStatus.Completed);
    }

    private void OnPickStatus(object? sender, RoutedEventArgs e)
    {
        StatusMenuToggle.IsChecked = false;

        if ((sender as Control)?.Tag is QuestStatus status)
        {
            // SetStatusAsync contains its own failures.
            _ = _viewModel.Tracker.SetStatusAsync(status);
        }
    }

    /// <summary>
    /// Opens the tracker page, one window at a time: to sign in, or on the
    /// player's quests. Whatever closes it, the tracker is read again.
    /// </summary>
    private void OpenTrackerWindow(bool quests)
    {
        if (_linking is { IsVisible: true })
        {
            _linking.Activate();

            return;
        }

        var window = _trackerWindow();

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
        window.Show(this);
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
            await View
                .ExecuteScriptAsync($"window.__dtHubGoToStep({index.ToString(CultureInfo.InvariantCulture)})")
                .ConfigureAwait(true);

            _viewModel.SetStep(index);

            // The bridge posts no Step message for a requested jump.
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

    private void OnPreviousStep(object? sender, RoutedEventArgs e) => GoToStep(_viewModel.StepTarget(-1));

    private void OnNextStep(object? sender, RoutedEventArgs e) => GoToStep(_viewModel.StepTarget(1));

    /// <summary>Diverts to a separate window any navigation that is not the page requested.</summary>
    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
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

    private void OnNewWindowRequested(object? sender, string uri) => Route(uri);

    /// <summary>Sends an address wherever it reads best: in place if a quest of the catalogue, aside otherwise.</summary>
    private void Route(string url)
    {
        // Outside the site, nothing enters our windows.
        if (!PapychaSite.Owns(url))
        {
            LogSentOutside(url);
            _dialogs.OpenUrl(url);

            return;
        }

        // The success tree goes to the browser. See PapychaSite.IsSuccessTree.
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

        // The model is up to date, the page is not: deferred, because we
        // are still inside the handler that just refused this navigation.
        Dispatcher.UIThread.Post(() => NavigateTo(url));
    }

    /// <summary>True when two addresses differ only by their anchor.</summary>
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

    /// <summary>Opens an address in its own window, the failure logged.</summary>
    private async Task OpenAsideAsync(string url)
    {
        try
        {
            var page = MacApp.Services.GetRequiredService<QuestPageWindow>();

            await page.ShowPageAsync(url, null).ConfigureAwait(true);

            LogPageOpened(url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogPageFailed(url, exception.Message);
        }
    }

    /// <summary>Brings the selected row into view.</summary>
    private void ScrollToSelection()
    {
        if (NodeList.SelectedItem is { } selected)
        {
            NodeList.ScrollIntoView(selected);
        }
    }

    private void OnGoBack(object? sender, RoutedEventArgs e) => _viewModel.GoBack();

    private void OnRecommendedClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ToggleRecommended();

        // OneWay: the toggle shows what the view model says, not the click.
        RecommendedToggle.SetCurrentValue(ToggleButton.IsCheckedProperty, _viewModel.ShowsRecommended);
    }

    private void OnPickClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: QuestNode node })
        {
            // Shift as it is now, as the PC's Keyboard.Modifiers: the box
            // can be ticked with the space bar too, after no press at all.
            _viewModel.Pick(node, OperatingSystem.IsMacOS() && MacNative.dthub_shift_down());
        }

        // The box flipped itself before the click; a pick refused left it ticked alone.
        if (sender is CheckBox { DataContext: QuestNode current } box)
        {
            box.SetCurrentValue(ToggleButton.IsCheckedProperty, current.Checked);
        }
    }

    // The batch methods never throw: the tracker turns every failure into a result.
    private async void OnMarkAllClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: QuestNode node })
        {
            await _viewModel.MarkAllAsync(node).ConfigureAwait(true);
        }
    }

    private async void OnMarkPickedClick(object? sender, RoutedEventArgs e) =>
        await _viewModel.MarkPickedAsync().ConfigureAwait(true);

    private void OnCancelBatchClick(object? sender, RoutedEventArgs e) => _viewModel.CancelBatch();

    /// <summary>Fills the gain's hover with where the quest's items come from, once read.</summary>
    private async void OnGainTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: QuestNode { Quest: { } quest } node } carrier
            || ToolTip.GetTip(carrier) is not StackPanel { Children.Count: >= 2 } panel
            || panel.Children[1] is not TextBlock sources)
        {
            return;
        }

        if (_tipReads.Remove(carrier, out var stale))
        {
            stale.Cancel();
        }

        using var read = new CancellationTokenSource();
        _tipReads[carrier] = read;

        try
        {
            // Never throws: a cancelled read gives null.
            var text = await _viewModel.WhereToFindAsync(quest, read.Token).ConfigureAwait(true);

            // The row may show another quest by now.
            if (!ReferenceEquals(carrier.DataContext, node))
            {
                return;
            }

            sources.Text = text ?? string.Empty;
            sources.IsVisible = !string.IsNullOrEmpty(text);
        }
        finally
        {
            if (_tipReads.TryGetValue(carrier, out var mine) && ReferenceEquals(mine, read))
            {
                _tipReads.Remove(carrier);
            }
        }
    }

    private void OnGainTipClosing(object? sender, RoutedEventArgs e)
    {
        if (sender is not null && _tipReads.Remove(sender, out var read))
        {
            read.Cancel();
        }
    }

    /// <summary>
    /// The tracker bar's right-hand group goes under the character's line
    /// when both no longer fit.
    /// </summary>
    private void OnTrackerBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // The selector and its margin; the level boxes already wrap under it.
        const double characterLine = 160;
        var under = TrackerBar.Bounds.Width - TrackerRight.DesiredSize.Width < characterLine;

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

    private void OnProfitMe(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowTeam(false), () => !_viewModel.ProfitTeam);

    private void OnProfitTeam(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowTeam(true), () => _viewModel.ProfitTeam);

    private void OnProfitXp(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowKamas(false), () => !_viewModel.ProfitKamas);

    private void OnProfitKamas(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowKamas(true), () => _viewModel.ProfitKamas);

    private void OnProfitQuests(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowSeries(false), () => !_viewModel.ProfitSeries);

    private void OnProfitSeries(object? sender, RoutedEventArgs e) => Profit(sender, () => _viewModel.ShowSeries(true), () => _viewModel.ProfitSeries);

    /// <summary>
    /// A OneWay pair's click: the view model decides, and the toggle shows
    /// its answer, so one clicked while on stays on. Its current value only:
    /// the binding stays.
    /// </summary>
    private static void Profit(object? sender, Action ask, Func<bool> shown)
    {
        ask();

        if (sender is ToggleButton toggle)
        {
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, shown());
        }
    }

    /// <summary>One attempt at a time, preparation and read together.</summary>
    private async void OnRetryTracker(object? sender, RoutedEventArgs e)
    {
        if (_retrying || _viewModel.Tracker.IsReading)
        {
            return;
        }

        _retrying = true;
        try
        {
            // Through the engine's preparation: after it failed, the cookie
            // can only come from a new attempt.
            _viewModel.RetryView();
            await PrepareOnShowAsync().ConfigureAwait(true);
        }
        finally
        {
            _retrying = false;
        }
    }

    /// <summary>Goes back to the quest we came from.</summary>
    private void OnGoBackPage(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.GoBackPage() is { } url)
        {
            NavigateTo(url);
        }
    }

    private void OnClearQuery(object? sender, RoutedEventArgs e)
    {
        _viewModel.Query = string.Empty;

        SearchBox.Focus();
    }

    private void OnCloseList(object? sender, RoutedEventArgs e) => _viewModel.IsListOpen = false;

    /// <summary>
    /// Opens a row's prerequisites, or closes them if they are already the
    /// ones shown; the lock's own hover is switched off meanwhile.
    /// </summary>
    private void OnShowNeeds(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button bouton)
        {
            return;
        }

        if (PanneauPrerequis.IsOpen && ReferenceEquals(PanneauPrerequis.PlacementTarget, bouton))
        {
            CloseNeeds();

            return;
        }

        CloseNeeds();
        ToolTip.SetServiceEnabled(bouton, false);

        PanneauPrerequis.PlacementTarget = bouton;
        PanneauPrerequis.DataContext = bouton.DataContext;
        PanneauPrerequis.IsOpen = true;
    }

    /// <summary>
    /// Closes the prerequisites as soon as a press lands anywhere other than
    /// the lock that opened them.
    /// </summary>
    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!PanneauPrerequis.IsOpen
            || (e.Source is Visual source && PanneauPrerequis.PlacementTarget is Visual target
                && (ReferenceEquals(source, target) || target.IsVisualAncestorOf(source))))
        {
            return;
        }

        CloseNeeds();
    }

    private void CloseNeeds()
    {
        if (PanneauPrerequis.PlacementTarget is Button bouton)
        {
            ToolTip.SetServiceEnabled(bouton, true);
        }

        PanneauPrerequis.IsOpen = false;
    }

    /// <summary>Opens the quest a prerequisite names.</summary>
    private void OnFollowNeed(object? sender, RoutedEventArgs e)
    {
        CloseNeeds();

        if ((sender as Control)?.Tag is QuestSummary quest)
        {
            Route(quest.Url);
        }
    }

    /// <summary>Follows a chain link: the previous quest or the next one.</summary>
    private void OnFollowChain(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is QuestLink link)
        {
            // Moving on to the next quest often means this one is done:
            // offered, never written on its own.
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
    private void OnAcceptFinish(object? sender, RoutedEventArgs e) => _ = _viewModel.Tracker.AcceptFinishOfferAsync();

    private void OnDismissFinish(object? sender, RoutedEventArgs e) => _viewModel.Tracker.DismissFinishOffer();

    private void OnFinishForOther(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is TrackerCharacter character)
        {
            _ = _viewModel.Tracker.FinishForAsync(character);
        }
    }

    private void OnFinishForAll(object? sender, RoutedEventArgs e) => _ = _viewModel.Tracker.FinishForAllAsync();

    /// <summary>Indexes, then logs the duration when there was something to index, once.</summary>
    private async Task IndexAsync()
    {
        await _viewModel.InitializeAsync().ConfigureAwait(true);

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

    /// <summary>The close button hides, it does not close: the window is brought back with the hotkey.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _ = _placements.SaveAsync(this, WindowPlacements.Quests);

        if (!MacApp.IsShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _watchdog.Stop();
        View.Dispose();

        base.OnClosed(e);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Formulaire de signalement ouvert sur : {url}")]
    private partial void LogReportOpened(string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Page liée ouverte : {url}")]
    private partial void LogPageOpened(string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adresse hors du site confiée au navigateur : {url}")]
    private partial void LogSentOutside(string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Arbre des succès ouvert dans le navigateur : {url}")]
    private partial void LogTreeSentOutside(string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Page liée non ouverte ({reason}) : {url}")]
    private partial void LogPageFailed(string url, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "La place du suivi de quêtes n'a pas pu être rétablie.")]
    private partial void LogPlacementFailed(Exception exception);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Le pont annonce la page chargée.")]
    private partial void LogBridgeLoaded();

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalogue indexé en {seconds:F1} s.")]
    private partial void LogIndexed(double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Noms officiels du jeu non lus : le catalogue reste celui de Papycha.")]
    private partial void LogNamesFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aucune nouvelle de la page après vingt secondes : la vue est rendue.")]
    private partial void LogWaitedInVain();
}
