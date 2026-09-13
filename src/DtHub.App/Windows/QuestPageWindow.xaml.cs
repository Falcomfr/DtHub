using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using DtHub.App.Services;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// A page opened from a link in a guide.
///
/// Without it, a click in the guide made the quest window navigate in
/// place: the banner kept the old quest's title, the steps became
/// those of the new page, and "Open in the browser" pointed elsewhere
/// than what was displayed. A separate window has nothing to keep up
/// to date and therefore lies about nothing.
/// </summary>
public partial class QuestPageWindow : Window
{
    private readonly IDialogService _dialogs;
    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;

    private string _url = string.Empty;
    private bool _watched;
    private string? _report;
    private bool _reportMode;

    public QuestPageWindow(
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
    }

    /// <summary>
    /// Handles of the open pages, so that shortcuts stay alive when one
    /// of them has focus.
    ///
    /// A plain set of integers, and not the application's list of
    /// windows: the question is asked from the foreground watch, which
    /// does not live on the interface thread. Touching a WPF window from
    /// there throws immediately, and the shortcut dies without anything
    /// saying so.
    /// </summary>
    private static readonly HashSet<nint> Handles = [];

    /// <summary>True if this handle is that of an open page.</summary>
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

        // The report window has only one form to show: its width is the
        // one the bridge gives to the form, and widening it would show
        // nothing more. The height, on the other hand, stays free, since
        // a small screen cannot always fit it in full.
        if (_reportMode)
        {
            MinWidth = Width;
            MaxWidth = Width;

            // WPF cannot remove maximizing alone: "CanMinimize" also
            // freezes the height. The style is therefore removed by
            // hand, as the project already does to dock a game window.
            _ = SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsMaximizeBox);
        }

        lock (Handles)
        {
            Handles.Add(handle);
        }

        // A single spot for all these windows: they follow one another
        // along the trail of links and are not distinguished from each
        // other. What we want to find again is the place where we put
        // "the pages window".
        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            _placements.Restore(this, WindowPlacements.LinkedPage, document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de la page liée n'a pas pu être rétablie.");
        }
    }

    /// <summary>
    /// The place is remembered here and not in <c>OnClosed</c>: the
    /// handle no longer exists by that point, and there would be
    /// nothing left to query.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.LinkedPage);

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

        base.OnClosed(e);
    }

    /// <summary>
    /// Opens the page and shows itself, if it truly is a page of the
    /// site.
    ///
    /// This window has no address bar: a page is seen there without
    /// knowing where it comes from, under our title and our icon. It
    /// therefore only receives the site, and the rest goes to the
    /// browser. Without this restriction, any link in a guide would
    /// open any address here, "file://" included.
    /// </summary>
    public Task ShowPageAsync(string url, string? title) => ShowAsync(url, title, report: null);

    /// <summary>
    /// Opens the page on its report form, unfolded and ready to fill
    /// in, with the step marker already set.
    ///
    /// Nothing is sent: the window shows the site's form, and it is
    /// the reader who writes and decides to press the button.
    /// </summary>
    public Task ShowReportAsync(string url, string? title, string location) =>
        ShowAsync(url, title, location ?? string.Empty);

    private async Task ShowAsync(string url, string? title, string? report)
    {
        // The achievement tree goes to the browser like what is outside
        // the site, and for a reason of the same nature: it is not a
        // page one reads but a tool one manipulates. See
        // PapychaSite.IsSuccessTree.
        if (!PapychaSite.Owns(url) || PapychaSite.IsSuccessTree(url))
        {
            _dialogs.OpenUrl(url);

            return;
        }

        _report = report;
        _reportMode = report is not null;

        _url = url;

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        Show();
        Activate();

        // The same environment as the guides window: a single profile,
        // a single cache, in the same place.
        await View
            .EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true))
            .ConfigureAwait(true);

        // Before navigating: the script injects itself at document
        // creation, and a page already loaded would not see it pass by.
        // Framing only, without the step tracking that is only
        // meaningful for a guide.
        //
        // Except in report mode: the framing hides the article footer,
        // where the form lives. This one has its own script, and it
        // waits for the page to be there, the form not existing before
        // that.
        if (_report is null)
        {
            await View.CoreWebView2
                .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script(framingOnly: true))
                .ConfigureAwait(true);
        }
        else
        {
            View.CoreWebView2.NavigationCompleted += OnReportPageLoaded;
        }

        if (!_watched)
        {
            _watched = true;

            View.CoreWebView2.NavigationStarting += OnNavigationStarting;
            View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }

        View.CoreWebView2.Navigate(url);
    }

    /// <summary>
    /// Prepares the form, once and only once.
    ///
    /// Only once because submitting redirects back to the article:
    /// replaying it would hide the site's response, which is precisely
    /// what one wants to read after pressing the button.
    /// </summary>
    private async void OnReportPageLoaded(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        View.CoreWebView2.NavigationCompleted -= OnReportPageLoaded;

        if (!e.IsSuccess)
        {
            return;
        }

        try
        {
            var state = await View.CoreWebView2
                .ExecuteScriptAsync(QuestBridge.ReportScript(_report))
                .ConfigureAwait(true);

            Log.Information("Formulaire de signalement : {Etat}.", state);

            // "absent": the page has no form, or the site changed its
            // article footer. The button only appears on a guide, where
            // the form is always there; this is therefore only reached
            // if the site has moved. The window then shows the whole
            // page, and takes back a title that no longer promises what
            // it does not show.
            if (state.Contains("absent", StringComparison.Ordinal))
            {
                Title = Strings.Get("PapychaGuides");
                return;
            }

            // The script returns the height that would be needed to
            // show the form in full. The window sets itself to that,
            // without exceeding the screen.
            if (Hauteur(state) is { } voulue)
            {
                Height = Math.Min(voulue + (ActualHeight - View.ActualHeight), SystemParameters.WorkArea.Height);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le formulaire de signalement n'a pas pu être préparé.");
        }
    }

    /// <summary>
    /// The height that the script announces, or <c>null</c> if it does
    /// not give one.
    ///
    /// The engine returns the result in JSON: "pret 812", quotes
    /// included.
    /// </summary>
    private static double? Hauteur(string state)
    {
        var parts = state.Trim('"', ' ').Split(' ');

        return parts.Length > 1
            && double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    /// <summary>
    /// Holds the navigation back here, and hands off to the browser
    /// whatever leaves the site.
    ///
    /// Until now the window followed everything a page asked it to
    /// follow. A page of the site that redirects elsewhere, or that we
    /// might have replaced, would therefore take the window wherever
    /// it wanted, without the "Open in the browser" button ceasing to
    /// point to the original page.
    /// </summary>
    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (PapychaSite.Owns(e.Uri) && !PapychaSite.IsSuccessTree(e.Uri))
        {
            _url = e.Uri;

            return;
        }

        e.Cancel = true;

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>
    /// And target="_blank", which the engine would otherwise open in a
    /// window of its own, out of all control and with nothing tying it
    /// back to us.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        _dialogs.OpenUrl(e.Uri);
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) => _dialogs.OpenUrl(_url);

    // The only style being removed: the maximize button of the report
    // window.
    private const int GwlStyle = -16;

    private const int WsMaximizeBox = 0x00010000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern int GetWindowLong(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern int SetWindowLong(nint handle, int index, int value);
}
