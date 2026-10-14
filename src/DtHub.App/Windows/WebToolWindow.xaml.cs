using System.IO;
using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Settings;

using Microsoft.Web.WebView2.Core;

using Serilog;

namespace DtHub.App.Windows;

/// <summary>
/// A DOFUS Touch tool that is a web site, shown in DT Hub's own engine like
/// the guides. One window per tool, each with its own place.
/// </summary>
public partial class WebToolWindow : Window
{
    /// <summary>The share of the work area the map takes the first time.</summary>
    private const double MapShare = 0.65;

    /// <summary>Lomitall's first height: wanted large by the project owner (2026-10-14).</summary>
    private const double LomitallHeightShare = 0.85;

    /// <summary>
    /// Lomitall's first width: its page stops at 1280 pixels (max-w-7xl)
    /// and centres itself, so 85 % of a wide screen left two empty bands.
    /// Its top bar, though, is 1357 pixels wide in that 1280 and overflows
    /// to the right: measured on 2026-10-14, the search field ends inside
    /// the page from a 1458 pixel view. Hence 1480 with the frame.
    /// </summary>
    private const double LomitallWidth = 1480;

    /// <summary>
    /// Calcofus's first width: its narrow layout, measured readable at 460
    /// pixels on 2026-10-11, leaves the game the rest of the screen.
    /// </summary>
    private const double CalcofusWidth = 480;

    private const double CalcofusHeightShare = 0.85;

    /// <summary>
    /// DofusRoom's first width: its map editor scales to the window, and was
    /// still readable at 600 pixels on 2026-10-13, where 480 left the cells
    /// too small to click. Same height and edge as Calcofus.
    /// </summary>
    private const double DofusRoomWidth = 600;

    private readonly WindowPlacements _placements;
    private readonly SettingsService _settings;
    private readonly WebViewEnvironment _engine;
    private readonly IDialogService _dialogs;

    public WebToolWindow(
        WindowPlacements placements,
        SettingsService settings,
        WebViewEnvironment engine,
        IDialogService dialogs)
    {
        InitializeComponent();

        _placements = placements;
        _settings = settings;
        _engine = engine;
        _dialogs = dialogs;

        Loaded += async (_, _) => await OpenAsync().ConfigureAwait(true);
    }

    /// <summary>Which tool this window shows. Set before it is shown.</summary>
    public WebTool Tool { get; set; }

    private string PlacementKey => Tool switch
    {
        WebTool.Map => WindowPlacements.Map,
        WebTool.Calcofus => WindowPlacements.Calcofus,
        WebTool.Lomitall => WindowPlacements.Lomitall,
        _ => WindowPlacements.DofusRoom,
    };

    private string HomeAddress => Tool switch
    {
        WebTool.Map => DofusTouchMap.Address,
        WebTool.Calcofus => Calcofus.Address,
        WebTool.Lomitall => Lomitall.Address,
        _ => DofusRoom.Address,
    };

    private bool IsOwnPage(string url) => Tool switch
    {
        WebTool.Calcofus => Calcofus.IsOwnPage(url),
        WebTool.Lomitall => Lomitall.IsOwnPage(url),
        _ => DofusRoom.IsOwnPage(url),
    };

    private async Task OpenAsync()
    {
        try
        {
            await View.EnsureCoreWebView2Async(await _engine.GetAsync().ConfigureAwait(true)).ConfigureAwait(true);
            View.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            if (Tool == WebTool.Map)
            {
                foreach (var host in DofusTouchMap.BlockedAdHosts)
                {
                    // Workers included: Adsterra's push ads run in one.
                    View.CoreWebView2.AddWebResourceRequestedFilter(
                        $"*{host}/*",
                        CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.All);
                }

                View.CoreWebView2.WebResourceRequested += OnMapRequest;
                View.CoreWebView2.Navigate(DofusTouchMap.Address);
                return;
            }

            View.CoreWebView2.NavigationStarting += OnToolNavigating;

            if (Tool == WebTool.DofusRoom)
            {
                await View.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(DofusRoom.TouchOnlyScript).ConfigureAwait(true);
                View.CoreWebView2.Navigate(DofusRoom.Address);
                return;
            }

            if (Tool == WebTool.Lomitall)
            {
                View.CoreWebView2.Navigate(Lomitall.Address);
                return;
            }

            View.CoreWebView2.SourceChanged += OnCalcofusPageChanged;

            var document = await _settings.GetAsync().ConfigureAwait(true);
            View.CoreWebView2.Navigate(Calcofus.StartAddress(document.LastCalcofusUrl));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "L'outil web {Outil} n'a pas pu s'ouvrir.", Tool);
        }
    }

    /// <summary>
    /// The map's ads, refused before they leave the PC: no request, nothing
    /// for an antivirus to stop. The filter is a wildcard, so the host is
    /// checked again exactly.
    /// </summary>
    private void OnMapRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (DofusTouchMap.IsBlockedAd(e.Request.Uri))
        {
            e.Response = View.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
            Log.Information("Carte : publicité refusée ({Hote}).", new Uri(e.Request.Uri).Host);
        }
    }

    private void OnOpenInBrowser(object sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(View.CoreWebView2?.Source is { Length: > 0 } page
            ? page
            : HomeAddress);

    private void OnDonate(object sender, RoutedEventArgs e) => _dialogs.OpenUrl(DofusTouchMap.DonationAddress);

    /// <summary>
    /// target="_blank", which the engine would otherwise open in a window of
    /// its own, outside any control: the system browser takes it.
    ///
    /// Except the map's ads. Refused inside the window, a pop-under could
    /// still reach the system browser this way. Bitdefender stopped
    /// highperformanceformat.com as phishing on 11/10/2026, path not
    /// established: see D192.
    /// </summary>
    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (DofusTouchMap.IsBlockedAd(e.Uri))
        {
            Log.Information("Carte : fenêtre publicitaire refusée ({Hote}).", new Uri(e.Uri).Host);

            return;
        }

        _dialogs.OpenUrl(e.Uri);
    }

    /// <summary>
    /// A link out of the tool, to Discord, Twitch, a stuff library or a
    /// player's profile, goes to the browser: the window is for the tool
    /// alone. Lomitall's links to the other tools leave the same way.
    /// </summary>
    private void OnToolNavigating(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsOwnPage(e.Uri))
        {
            e.Cancel = true;
            _dialogs.OpenUrl(e.Uri);
        }
    }

    /// <summary>
    /// The calculator shown, remembered on each change: the site moves
    /// between pages without reloading, and the window reopens there.
    /// </summary>
    private void OnCalcofusPageChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var url = View.CoreWebView2.Source;

        if (Calcofus.IsOwnPage(url))
        {
            _ = RememberCalcofusPageAsync(url);
        }
    }

    /// <summary>Remembered without blocking, and a failed write said in the log.</summary>
    private async Task RememberCalcofusPageAsync(string url)
    {
        try
        {
            await _settings.SetLastCalcofusUrlAsync(url).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "La page de Calcofus n'a pas pu être retenue.");
        }
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        Title = Strings.Get(Tool switch
        {
            WebTool.Map => "ToolMapName",
            WebTool.Calcofus => "ToolCalcofusName",
            WebTool.Lomitall => "ToolLomitallName",
            _ => "ToolDofusRoomName",
        });
        Donate.Visibility = Tool == WebTool.Map ? Visibility.Visible : Visibility.Collapsed;

        base.OnSourceInitialized(e);

        try
        {
            var document = await _settings.GetAsync().ConfigureAwait(true);

            if (!_placements.Restore(this, PlacementKey, document))
            {
                PlaceFirstTime();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "La place de l'outil web {Outil} n'a pas pu être rétablie.", Tool);
        }
    }

    private void PlaceFirstTime()
    {
        var area = SystemParameters.WorkArea;

        if (Tool is WebTool.Map or WebTool.Lomitall)
        {
            Width = Tool == WebTool.Map ? area.Width * MapShare : Math.Min(LomitallWidth, area.Width);
            Height = area.Height * (Tool == WebTool.Map ? MapShare : LomitallHeightShare);
            Left = area.Left + ((area.Width - Width) / 2);
            Top = area.Top + ((area.Height - Height) / 2);
            return;
        }

        // Against the right edge, where it covers the least of a game
        // window centred or docked on the left.
        Width = Math.Min(Tool == WebTool.Calcofus ? CalcofusWidth : DofusRoomWidth, area.Width);
        Height = area.Height * CalcofusHeightShare;
        Left = area.Right - Width;
        Top = area.Top + ((area.Height - Height) / 2);
    }

    /// <summary>Remembered here: the handle is gone by OnClosed.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _ = _placements.SaveAsync(this, PlacementKey);

        base.OnClosing(e);
    }
}
