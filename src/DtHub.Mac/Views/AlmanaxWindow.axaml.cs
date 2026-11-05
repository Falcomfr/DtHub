using Avalonia.Controls;
using Avalonia.Interactivity;

using DtHub.App.Services;
using DtHub.App.ViewModels;
using DtHub.App.Windows;
using DtHub.Core.Almanax;
using DtHub.Core.Localization;
using DtHub.Core.Settings;
using DtHub.Mac.Platform;

using Serilog;

namespace DtHub.Mac.Views;

/// <summary>
/// The PC's AlmanaxWindow (src/DtHub.App/Windows/AlmanaxWindow.xaml.cs):
/// today's Almanax, read from Ankama's portal and redrawn here. The page
/// is not shown: the engine loads it, the bridge pulls the useful fields
/// out of it, and the window draws them its own way. Two readers, the
/// day's and the week's, as on the PC.
/// </summary>
internal sealed partial class AlmanaxWindow : Window
{
    /// <summary>The handles of the open windows, for the shortcuts, read off the interface thread.</summary>
    private static readonly HashSet<nint> Handles = [];

    private readonly IDialogService _dialogs = null!;
    private readonly WindowPlacements _placements = null!;
    private readonly SettingsService _settings = null!;
    private readonly AlmanaxViewModel _model = new();

    /// <summary>The days already read, for the session: a day once read does not change.</summary>
    private readonly Dictionary<DateOnly, AlmanaxDay> _read = [];

    private DateOnly _wanted = AlmanaxCalendar.Today();
    private string _url = string.Empty;
    private nint _handle;

    /// <summary>The setup, once: the second click awaits the first one's.</summary>
    private Task? _preparing;

    /// <summary>The second reader's setup, once, like the first one's.</summary>
    private Task? _weekPreparing;

    private DateOnly _batchDate;
    private TaskCompletionSource<AlmanaxDay?>? _batchWait;

    /// <summary>For the designer only.</summary>
    public AlmanaxWindow() => InitializeComponent();

    public AlmanaxWindow(
        IDialogService dialogs,
        WindowPlacements placements,
        SettingsService settings)
    {
        _dialogs = dialogs;
        _placements = placements;
        _settings = settings;

        InitializeComponent();

        _model.DayReader = ReadForBatchAsync;

        DataContext = _model;

        _model.DateRequested += (_, date) => _ = LoadAsync(date);

        // At its place from the first frame; OnOpenedAsync settles it once open.
        _placements.PlaceBeforeShow(this, () => WindowPlacements.Almanax);

        Opened += async (_, _) => await OnOpenedAsync().ConfigureAwait(true);
    }

    /// <summary>True if this handle is that of an open Almanax.</summary>
    public static bool Owns(nint handle)
    {
        lock (Handles)
        {
            return Handles.Contains(handle);
        }
    }

    private async Task OnOpenedAsync()
    {
        _handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        lock (Handles)
        {
            Handles.Add(_handle);
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

    /// <summary>Remembered here: the handle no longer exists by Closed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.Almanax);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        lock (Handles)
        {
            Handles.Remove(_handle);
        }

        // A batch still reading would reach the disposed view on its next day.
        _model.StopBatchCommand.Execute(null);

        View.Dispose();
        WeekView.Dispose();

        base.OnClosed(e);
    }

    /// <summary>Opens the window on today's Almanax.</summary>
    public async Task ShowAlmanaxAsync()
    {
        Show();
        Activate();

        await LoadAsync(AlmanaxCalendar.Today()).ConfigureAwait(true);

        await LoadWeekAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// The week page, read once per opening, in its own reader. A failure
    /// only costs the bonuses under the strip: logged, not shown.
    /// </summary>
    private async Task LoadWeekAsync()
    {
        try
        {
            await PrepareWeekViewAsync().ConfigureAwait(true);

            WeekView.Navigate(AlmanaxCalendar.WeekUrlFor(Strings.Spoken.TwoLetterISOLanguageName));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _weekPreparing = null;

            Log.Warning(exception, "La semaine de l'Almanax n'a pas pu être chargée.");
        }
    }

    private Task PrepareWeekViewAsync() => _weekPreparing ??= SetUpWeekViewAsync();

    private Task SetUpWeekViewAsync()
    {
        WeekView.AddScriptToExecuteOnDocumentCreated(AlmanaxBridge.Script());

        WeekView.WebMessageReceived += OnWeekMessage;
        WeekView.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Log.Warning("Une page de l'Almanax n'a pas pu être jointe en arrière-plan : {Etat}.", e.WebErrorStatus);

                _batchWait?.TrySetResult(null);
            }
        };

        // Nothing is shown, so nothing opens: the new windows are dropped.
        return Task.CompletedTask;
    }

    /// <summary>
    /// The second reader's messages: the week page, or a day read for a
    /// batch. The week is tried first, since a day's message never carries
    /// a week.
    /// </summary>
    private void OnWeekMessage(object? sender, string payload)
    {
        var week = AlmanaxWeek.From(payload);

        if (week.Count > 0)
        {
            _model.KnowWeek(week);

            return;
        }

        if (_batchWait is { } wait)
        {
            wait.TrySetResult(AlmanaxMessage.From(payload, _batchDate));

            return;
        }

        Log.Warning("La semaine de l'Almanax lue ne donne aucun bonus de DOFUS Touch, ou la page a changé.");
    }

    /// <summary>
    /// A day for the preparation or the search: from the session's memory
    /// when already read, from the portal otherwise, at most one page a
    /// second. See D190.
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
            WeekView.Navigate(AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, date));

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

    /// <summary>Loads a day, from the cache if already read, from the page otherwise.</summary>
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

        // "BeginLoading" and not "Go": "Go" raises the request this method
        // listens to.
        _model.BeginLoading(date);

        try
        {
            await PrepareAsync().ConfigureAwait(true);

            _url = AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, date);

            View.Navigate(_url);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "L'Almanax n'a pas pu être chargé.");

            _preparing = null;

            _model.Fail();
        }
    }

    /// <summary>The engine set up only once, its bridge before any document.</summary>
    private Task PrepareAsync() => _preparing ??= SetUpEngineAsync();

    private Task SetUpEngineAsync()
    {
        View.AddScriptToExecuteOnDocumentCreated(AlmanaxBridge.Script());

        View.WebMessageReceived += OnBridgeMessage;
        View.NavigationCompleted += OnNavigationCompleted;
        View.NewWindowRequested += OnNewWindowRequested;

        return Task.CompletedTask;
    }

    /// <summary>
    /// What the bridge read from the page, refused when the block is not
    /// DOFUS Touch's: the portal serves both games on the same page.
    /// </summary>
    private void OnBridgeMessage(object? sender, string payload)
    {
        if (AlmanaxMessage.From(payload, _wanted) is not { } day)
        {
            Log.Warning("L'Almanax lu n'est pas celui de DOFUS Touch, ou la page a changé.");

            _model.Fail();

            return;
        }

        _read[day.Date] = day;

        // Only if it is still the requested day.
        if (day.Date == _wanted)
        {
            _model.Show(day);
        }
    }

    /// <summary>The bridge will post nothing if the page has not arrived: this is where that is known.</summary>
    private void OnNavigationCompleted(object? sender, WebNavigationCompleted e)
    {
        if (!e.IsSuccess)
        {
            Log.Warning("L'Almanax n'a pas pu être joint : {Etat}.", e.WebErrorStatus);

            _model.Fail();
        }
    }

    /// <summary>target="_blank": the page is not shown, but it keeps its own script.</summary>
    private void OnNewWindowRequested(object? sender, string uri) => _dialogs.OpenUrl(uri);

    /// <summary>The whole page, for whoever wants the decor and the mood text.</summary>
    private void OnOpenInBrowser(object? sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(_url.Length > 0
            ? _url
            : AlmanaxCalendar.UrlFor(Strings.Spoken.TwoLetterISOLanguageName, _wanted));
}
