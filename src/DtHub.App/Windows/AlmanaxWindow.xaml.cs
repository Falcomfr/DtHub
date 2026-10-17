using System.Globalization;
using System.Windows;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.Core.Almanax;
using DtHub.Core.Localization;
using DtHub.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// Today's Almanax, read from Ankama's portal and redrawn here.
///
/// The page is not shown: it is a whole desktop page, with its decor,
/// its inserts and its mood text, none of which helps in knowing what
/// to bring today. The engine loads it, the bridge pulls the four
/// useful fields out of it, and the window draws them its own way.
///
/// In a separate window and not in the guides' one: that one holds a
/// search, a quest banner and an achievement chain, and none of that
/// applies to the Almanax, which is not a quest from the catalogue and
/// does not come from the same site.
///
/// <see cref="AlmanaxCalendar" /> says why it is the only accurate
/// source.
/// </summary>
public partial class AlmanaxWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;
    private readonly AlmanaxViewModel _model = new();

    /// <summary>
    /// The days already read, for the duration of the session.
    ///
    /// The calendar is fixed: a day once read will not change again.
    /// Going back to a day already seen is therefore done without
    /// reloading the page, which makes the strip usable instead of
    /// imposing a second's wait per day.
    ///
    /// In memory and not on disk: nothing from the page is kept after
    /// the session.
    /// </summary>
    private readonly Dictionary<DateOnly, AlmanaxDay> _read = [];

    private DateOnly _wanted = DateOnly.FromDateTime(DateTime.Now);
    private string _url = string.Empty;
    /// <summary>
    /// The setup, once and only once.
    ///
    /// A simple flag set at the end was not enough: there are two
    /// waits before it, and two clicks close together would both get
    /// in before the first one had set it. The engine then received
    /// its bridge twice and its subscriptions twice, and posted every
    /// reading twice over. So the task itself is kept, and the second
    /// click awaits it.
    /// </summary>
    private Task? _preparing;

    public AlmanaxWindow(
        IDialogService dialogs,
        WindowPlacements placements,
        SettingsService settings,
        WebViewEnvironment engine)
    {
        _dialogs = dialogs;
        _placements = placements;
        _settings = settings;
        _engine = engine;

        InitializeComponent();

        _model.DayReader = ReadForBatchAsync;

        DataContext = _model;

        _model.DateRequested += (_, date) => _ = LoadAsync(date);
    }

    /// <summary>
    /// Handles of the open windows, so shortcuts stay alive when one
    /// of them has focus. Same reason as for the linked pages: the
    /// question comes from the foreground watcher, which does not
    /// live on the interface thread and therefore cannot touch a WPF
    /// window.
    /// </summary>
    private static readonly HashSet<nint> Handles = [];

    /// <summary>True if this handle is that of an open Almanax.</summary>
    public static bool Owns(nint handle)
    {
        lock (Handles)
        {
            return Handles.Contains(handle);
        }
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Add(handle);
        }

        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            _placements.Restore(this, WindowPlacements.Almanax, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de l'Almanax n'a pas pu être rétablie.");
        }
    }

    /// <summary>
    /// The place is remembered here and not in <c>OnClosed</c>: the
    /// handle no longer exists by then, and there would be nothing
    /// left to query.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.Almanax);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Remove(handle);
        }

        View.Dispose();
        WeekView.Dispose();

        base.OnClosed(e);
    }

    /// <summary>Opens the window on today's Almanax.</summary>
    public async Task ShowAlmanaxAsync()
    {
        Show();
        Activate();

        await LoadAsync(DateOnly.FromDateTime(DateTime.Now)).ConfigureAwait(true);

        await LoadWeekAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The week page, read once per opening, in its own reader.
    ///
    /// Its own and not the day's: the day's reader navigates at every
    /// click in the strip, and a week page interrupted by a click, or
    /// a click's page interrupted by the week, would each have had to
    /// be told apart from a real failure. Two readers, and neither
    /// waits for the other.
    ///
    /// A failure here only costs the bonuses under the strip, which
    /// still fill in as days are read: it is logged, not shown.
    /// </summary>
    private async Task LoadWeekAsync()
    {
        try
        {
            await PrepareWeekViewAsync().ConfigureAwait(true);

            WeekView.CoreWebView2.Navigate(
                AlmanaxCalendar.WeekUrlFor(Strings.Spoken.TwoLetterISOLanguageName));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Forgotten, as for the day's reader: a failed setup kept as
            // is would fail every batch without trying.
            _weekPreparing = null;

            Log.Warning(exception, "La semaine de l'Almanax n'a pas pu être chargée.");
        }
    }

    /// <summary>The second reader's setup, once, like the first one's.</summary>
    private Task? _weekPreparing;

    private Task PrepareWeekViewAsync() => _weekPreparing ??= SetUpWeekViewAsync();

    private async Task SetUpWeekViewAsync()
    {
        await WeekView
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        await WeekView.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(AlmanaxBridge.Script())
            .ConfigureAwait(true);

        WeekView.CoreWebView2.WebMessageReceived += OnWeekMessage;
        WeekView.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Log.Warning("Une page de l'Almanax n'a pas pu être jointe en arrière-plan : {Etat}.", e.WebErrorStatus);

                _batchWait?.TrySetResult(null);
            }
        };

        // Same reason as the day's reader: nothing is shown, so nothing
        // opens.
        WeekView.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
    }

    /// <summary>
    /// The second reader's messages: the week page, or a day read for
    /// a batch. The week is tried first, since a day's message never
    /// carries a week, and a week page still arriving when a batch
    /// starts must not pass for the batch's day.
    /// </summary>
    private void OnWeekMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string payload;

        try
        {
            payload = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Not text: same case as the day's reader.
            return;
        }

        var week = AlmanaxWeek.From(payload);

        if (week.Count > 0)
        {
            _model.KnowWeek(week);

            return;
        }

        if (_batchWait is { } wait)
        {
            // Null when refused, as on the day's reader: not Touch's
            // block, or the page changed. The batch then marks the day
            // as not read.
            wait.TrySetResult(AlmanaxMessage.From(payload, _batchDate));

            return;
        }

        Log.Warning("La semaine de l'Almanax lue ne donne aucun bonus de DOFUS Touch, ou la page a changé.");
    }

    private DateOnly _batchDate;
    private TaskCompletionSource<AlmanaxDay?>? _batchWait;

    /// <summary>
    /// A day for the preparation or the search: from the session's
    /// memory when it was already read, from the portal otherwise, on
    /// the second reader so that clicking in the strip goes on.
    ///
    /// **At most one page a second**, waited for even when the page
    /// came sooner: these reads follow one another without anyone
    /// clicking between them, and they should not reach the portal
    /// faster than someone clicking the days by hand. See D190.
    /// </summary>
    private async Task<AlmanaxDay?> ReadForBatchAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (_read.TryGetValue(date, out var known))
        {
            return known;
        }

        await PrepareWeekViewAsync().ConfigureAwait(true);

        var pace = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        _batchDate = date;
        _batchWait = new TaskCompletionSource<AlmanaxDay?>(TaskCreationOptions.RunContinuationsAsynchronously);

        AlmanaxDay? day;

        try
        {
            WeekView.CoreWebView2.Navigate(
                AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, date));

            day = await _batchWait.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            Log.Warning("Le jour {Jour} de l'Almanax n'a rien rendu en vingt secondes.", date);

            day = null;
        }
        finally
        {
            _batchWait = null;
        }

        await pace.ConfigureAwait(true);

        if (day is not null)
        {
            _read[day.Date] = day;
            _model.Remember(day);
        }

        return day;
    }

    /// <summary>
    /// Loads a day, from the cache if it has already been read, from
    /// the page otherwise.
    /// </summary>
    private async Task LoadAsync(DateOnly date)
    {
        if (_model.AlreadyShowing(date))
        {
            return;
        }

        _wanted = date;

        if (_read.TryGetValue(date, out var known))
        {
            _model.Show(known);

            return;
        }

        // "BeginLoading" and not "Go": "Go" raises the request that
        // this method listens to, and the two would end up
        // triggering each other again.
        _model.BeginLoading(date);

        try
        {
            await PrepareAsync().ConfigureAwait(true);

            _url = AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, date);

            View.CoreWebView2.Navigate(_url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "L'Almanax n'a pas pu être chargé.");

            // The setup task is forgotten: kept as is, it remains
            // failed and any new attempt would fail without even
            // trying.
            _preparing = null;

            _model.Fail();
        }
    }

    /// <summary>
    /// The engine, ready and set up, only once.
    ///
    /// The bridge injects itself when the document is created: set up
    /// after the fact, it would arrive on an already-built page and
    /// would have nothing to hook onto for the next one.
    /// </summary>
    private Task PrepareAsync() => _preparing ??= SetUpEngineAsync();

    private async Task SetUpEngineAsync()
    {
        // The same environment as the other page windows: one single
        // profile, one single cache, in the same place.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(AlmanaxBridge.Script())
            .ConfigureAwait(true);

        View.CoreWebView2.WebMessageReceived += OnBridgeMessage;
        View.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
    }

    /// <summary>
    /// What the bridge read from the page.
    ///
    /// The message is flatly refused if the block is not that of
    /// DOFUS Touch: the portal serves both games on the same page,
    /// and the DOFUS Almanax asks for different objects. Better to
    /// say that reading failed than to give the offering of another
    /// game.
    /// </summary>
    private void OnBridgeMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string payload;

        try
        {
            payload = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // The message is not text. The bridge never posts any
            // other kind, but any page can call "postMessage".
            return;
        }

        if (AlmanaxMessage.From(payload, _wanted) is not { } day)
        {
            Log.Warning("L'Almanax lu n'est pas celui de DOFUS Touch, ou la page a changé.");

            _model.Fail();

            return;
        }

        _read[day.Date] = day;

        // Only if it is still the requested day: one might have
        // clicked elsewhere during the loading, and the page that
        // finishes is no longer the one being waited for.
        if (day.Date == _wanted)
        {
            _model.Show(day);
        }
    }

    /// <summary>
    /// The bridge will post nothing if the page has not arrived: this
    /// is where that is known, and nowhere else.
    /// </summary>
    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            Log.Warning("L'Almanax n'a pas pu être joint : {Etat}.", e.WebErrorStatus);

            _model.Fail();
        }
    }

    /// <summary>
    /// target="_blank", which the engine would otherwise open in a
    /// window of its own, outside any control and with nothing tying
    /// it back to us. The page is not shown, so nobody can click, but
    /// it keeps its own script and nothing requires trusting it.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>
    /// The whole page, for whoever wants the decor and the mood text.
    /// </summary>
    private void OnOpenInBrowser(object sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(_url.Length > 0
            ? _url
            : AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, _wanted));
}
