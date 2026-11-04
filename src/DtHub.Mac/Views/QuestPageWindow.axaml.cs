using System.Globalization;

using Avalonia.Controls;
using Avalonia.Interactivity;

using DtHub.App.Services;
using DtHub.App.Windows;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;
using DtHub.Mac.Platform;

using Serilog;

namespace DtHub.Mac.Views;

/// <summary>
/// The PC's QuestPageWindow (src/DtHub.App/Windows/QuestPageWindow.xaml.cs):
/// a page opened from a link in a guide, or the site's report form. A
/// separate window has nothing to keep up to date and therefore lies
/// about nothing.
/// </summary>
internal sealed partial class QuestPageWindow : Window
{
    /// <summary>The handles of the open pages, for the shortcuts, read off the interface thread.</summary>
    private static readonly HashSet<nint> Handles = [];

    private readonly IDialogService _dialogs = null!;
    private readonly WindowPlacements _placements = null!;
    private readonly SettingsService _settings = null!;

    private string _url = string.Empty;
    private bool _watched;
    private string? _report;
    private nint _handle;

    /// <summary>For the designer only.</summary>
    public QuestPageWindow() => InitializeComponent();

    public QuestPageWindow(
        IDialogService dialogs,
        WindowPlacements placements,
        SettingsService settings)
    {
        _dialogs = dialogs;
        _placements = placements;
        _settings = settings;

        InitializeComponent();

        if (PageTranslator.Offers(PageTranslation.French))
        {
            Translate.IsVisible = true;
            OpenPage.IsVisible = false;
        }

        // At its place from the first frame; OnOpenedAsync settles it once open.
        _placements.PlaceBeforeShow(this, () => WindowPlacements.LinkedPage);

        View.NavigationCompleted += OnPageLoaded;
        Failure.Retry += (_, _) =>
        {
            ShowFailure(null);
            View.Navigate(View.LastNavigation ?? _url);
        };

        Opened += async (_, _) => await OnOpenedAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// A page that did not come shows why, in its place, as WebView2's
    /// error page does on the PC; one that came takes its place back.
    /// </summary>
    private void OnPageLoaded(object? sender, WebNavigationCompleted e)
    {
        if (e.IsSuccess)
        {
            ShowFailure(null);
        }
        else if (!e.IsInterrupted)
        {
            Log.Warning("La page n'a pas pu être chargée : {Etat}", e.WebErrorStatus);
            ShowFailure(e.Description);
        }
    }

    private void ShowFailure(string? reason)
    {
        Failure.Reason = reason;
        Failure.IsVisible = reason is not null;
        View.IsVisible = reason is null;
    }

    /// <summary>True if this handle is that of an open page.</summary>
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

        // A single spot for all these windows: "the pages window".
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

    /// <summary>Remembered here: the handle no longer exists by Closed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _ = _placements.SaveAsync(this, WindowPlacements.LinkedPage);

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        lock (Handles)
        {
            Handles.Remove(_handle);
        }

        View.Dispose();

        base.OnClosed(e);
    }

    /// <summary>Opens the page and shows itself, if it truly is a page of the site.</summary>
    public Task ShowPageAsync(string url, string? title) => ShowAsync(url, title, report: null);

    /// <summary>
    /// Opens the page on its report form, unfolded and ready to fill in.
    /// Nothing is sent: the reader decides to press the button.
    /// </summary>
    public Task ShowReportAsync(string url, string? title, string location) =>
        ShowAsync(url, title, location ?? string.Empty);

    private Task ShowAsync(string url, string? title, string? report)
    {
        // The achievement tree goes to the browser like what is outside the
        // site. See PapychaSite.IsSuccessTree.
        if (!PapychaSite.Owns(url) || PapychaSite.IsSuccessTree(url))
        {
            _dialogs.OpenUrl(url);

            return Task.CompletedTask;
        }

        _report = report;

        // The report window has only one form to show: its width is the
        // one the bridge gives to the form, its height stays free.
        if (report is not null)
        {
            MinWidth = Width;
            MaxWidth = Width;
            CanMaximize = false;
        }

        _url = url;

        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        Show();
        Activate();

        // Before navigating: framing only, except in report mode, whose own
        // script waits for the page, the form not existing before that.
        if (_report is null)
        {
            View.AddScriptToExecuteOnDocumentCreated(QuestBridge.Script(framingOnly: true));
        }
        else
        {
            View.NavigationCompleted += OnReportPageLoaded;
        }

        if (!_watched)
        {
            _watched = true;

            View.NavigationStarting += OnNavigationStarting;
            View.NewWindowRequested += OnNewWindowRequested;
        }

        View.Navigate(url);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Prepares the form, once and only once: submitting redirects back to
    /// the article, whose answer must stay readable.
    /// </summary>
    private async void OnReportPageLoaded(object? sender, WebNavigationCompleted e)
    {
        View.NavigationCompleted -= OnReportPageLoaded;

        if (!e.IsSuccess)
        {
            return;
        }

        try
        {
            var state = await View.ExecuteScriptAsync(QuestBridge.ReportScript(_report)).ConfigureAwait(true);

            Log.Information("Formulaire de signalement : {Etat}.", state);

            // "absent": the site changed its article footer; the window
            // shows the whole page, under a title that promises nothing.
            if (state.Contains("absent", StringComparison.Ordinal))
            {
                Title = Strings.Get("PapychaGuides");
                return;
            }

            // The height the form needs in full, within the screen.
            if (Hauteur(state) is { } voulue)
            {
                Height = Math.Min(voulue + (Bounds.Height - View.Bounds.Height), WorkArea.Of(this).Height);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "Le formulaire de signalement n'a pas pu être préparé.");
        }
    }

    /// <summary>The height the script announces ("pret 812", in JSON), or null.</summary>
    private static double? Hauteur(string state)
    {
        var parts = state.Trim('"', ' ').Split(' ');

        return parts.Length > 1
            && double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    /// <summary>Holds the navigation here, and hands off to the browser whatever leaves the site.</summary>
    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (PapychaSite.Owns(e.Uri) && !PapychaSite.IsSuccessTree(e.Uri))
        {
            _url = e.Uri;

            return;
        }

        e.Cancel = true;

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>target="_blank": to the browser, out of a window with no tie back to us.</summary>
    private void OnNewWindowRequested(object? sender, string uri) => _dialogs.OpenUrl(uri);

    private void OnOpenInBrowser(object? sender, RoutedEventArgs e) => _dialogs.OpenUrl(_url);

    private void OnTranslate(object? sender, RoutedEventArgs e) => _dialogs.OpenUrl(PageTranslator.AddressFor(_url));
}
