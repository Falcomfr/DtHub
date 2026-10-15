using System.IO;
using System.Text.Json;
using System.Windows;

using DtHub.App.Services;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
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

    /// <summary>
    /// The Papycha sign-in window: two cards and a button, at the width the
    /// site lays them out for, tall enough for the help unfolded.
    /// </summary>
    private const double SignInWidth = 560;

    private const double SignInHeight = 820;

    /// <summary>
    /// The player's quests: the dashboard's header, tabs and a list of the
    /// quests in progress, which rarely runs long. The tools' share of the
    /// screen left its lower third empty on a large screen.
    /// </summary>
    private const double QuestsHeight = 960;

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
        WebTool.PapychaTracker => WindowPlacements.PapychaQuests,
        WebTool.Lomitall => WindowPlacements.Lomitall,
        _ => WindowPlacements.DofusRoom,
    };

    private string HomeAddress => Tool switch
    {
        WebTool.Map => DofusTouchMap.Address,
        WebTool.Calcofus => Calcofus.Address,
        WebTool.PapychaTracker => PapychaSite.TrackerUrl,
        WebTool.Lomitall => Lomitall.Address,
        _ => DofusRoom.Address,
    };

    private bool IsOwnPage(string url) => Tool switch
    {
        WebTool.Calcofus => Calcofus.IsOwnPage(url),
        WebTool.Lomitall => Lomitall.IsOwnPage(url),
        WebTool.PapychaTracker => PapychaSite.Owns(url),
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

            if (Tool == WebTool.PapychaTracker)
            {
                await OpenTrackerAsync().ConfigureAwait(true);
                return;
            }

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
    /// For the Papycha tracker: true opens on the player's quests, false on
    /// signing in. Set before the window is shown.
    /// </summary>
    public bool ShowsQuests { get; set; }

    /// <summary>A guide of the site picked in the tracker, for the guides window to open.</summary>
    public event EventHandler<string>? GuideRequested;

    /// <summary>
    /// The character the tracker shows: DT Hub's choice at opening, then
    /// whatever either side picks. Set before the window is shown.
    /// </summary>
    public string? TrackerCharacterId { get; set; }

    /// <summary>A character picked on the site, for DT Hub's bar to follow.</summary>
    public event EventHandler<string>? CharacterPicked;

    /// <summary>
    /// Shows another character, picked in DT Hub's bar: written where the
    /// site looks for it, and the page reloaded.
    /// </summary>
    public async Task ShowCharacterAsync(string id)
    {
        if (id == TrackerCharacterId || View.CoreWebView2 is not { } engine)
        {
            return;
        }

        TrackerCharacterId = id;

        try
        {
            await engine.ExecuteScriptAsync(
                "localStorage.setItem('pqt_active_character'," + JsonSerializer.Serialize(id) + ");location.reload();")
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // The window closing under the call: nothing left to show.
            Log.Warning(exception, "Le personnage n'a pas pu être montré dans le suivi Papycha.");
        }
    }

    private void OnTrackerMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string payload;

        try
        {
            payload = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            // Not text: not the tracker's message.
            return;
        }

        if (TrackerCharacterMessage.Parse(payload) is { } id && id != TrackerCharacterId)
        {
            TrackerCharacterId = id;
            CharacterPicked?.Invoke(this, id);
        }
    }

    /// <summary>
    /// The tracker page, framed down to its own content. To sign in, the
    /// player types their phrase there, and only the cookie the site then sets
    /// is looked for. The value at opening is remembered, because a lost link
    /// leaves a stale cookie in the profile: a new phrase is a cookie with
    /// another value. Opened on the quests, the value stays the same and the
    /// window stays open.
    /// </summary>
    private async Task OpenTrackerAsync()
    {
        _openingCookie = await TrackerCookieValueAsync().ConfigureAwait(true);
        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.Script(framingOnly: true))
            .ConfigureAwait(true);
        await View.CoreWebView2
            .AddScriptToExecuteOnDocumentCreatedAsync(QuestBridge.TrackerScript(ShowsQuests, TrackerCharacterId))
            .ConfigureAwait(true);

        if (ShowsQuests)
        {
            View.CoreWebView2.WebMessageReceived += OnTrackerMessage;
        }
        View.CoreWebView2.NavigationCompleted += OnTrackerNavigated;

        // The quests are on the site's home page once signed in; the tracker
        // page only manages the account.
        View.CoreWebView2.Navigate(ShowsQuests ? PapychaSite.Root : PapychaSite.TrackerUrl);
    }

    private string? _openingCookie;

    private async Task<string?> TrackerCookieValueAsync() =>
        (await View.CoreWebView2.CookieManager.GetCookiesAsync(PapychaSite.Root).ConfigureAwait(true))
        .FirstOrDefault(cookie => cookie.Name == PapychaSite.TrackerCookie)?.Value;

    private async void OnTrackerNavigated(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        try
        {
            var current = await TrackerCookieValueAsync().ConfigureAwait(true);

            if (current is not null && current != _openingCookie)
            {
                View.CoreWebView2.NavigationCompleted -= OnTrackerNavigated;

                // Linked: the window's job is done. Its owner reads the
                // tracker again on Closed, which also covers a plain close.
                Close();
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An async void handler must not throw; the next navigation checks again.
            Log.Warning(exception, "Le cookie du suivi Papycha n'a pas pu être lu.");
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
        // A guide picked in the tracker reads in the guides window, with its
        // steps and its status, not squeezed into this one.
        if (Tool == WebTool.PapychaTracker && PapychaSite.Owns(e.Uri) && !PapychaSite.IsTrackerPage(e.Uri))
        {
            e.Cancel = true;
            GuideRequested?.Invoke(this, e.Uri);

            return;
        }

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
            WebTool.PapychaTracker => ShowsQuests ? "TrackerWindowTitle" : "TrackerSignInTitle",
            WebTool.Lomitall => "ToolLomitallName",
            _ => "ToolDofusRoomName",
        });
        Donate.Visibility = Tool == WebTool.Map ? Visibility.Visible : Visibility.Collapsed;

        base.OnSourceInitialized(e);

        if (!Remembers)
        {
            PlaceSignIn();

            return;
        }

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
        var width = Tool switch
        {
            WebTool.Calcofus => CalcofusWidth,
            _ => DofusRoomWidth,
        };

        Width = Math.Min(width, area.Width);
        Height = Tool == WebTool.PapychaTracker
            ? Math.Min(QuestsHeight, area.Height * CalcofusHeightShare)
            : area.Height * CalcofusHeightShare;
        Left = area.Right - Width;
        Top = area.Top + ((area.Height - Height) / 2);
    }

    /// <summary>
    /// False for the Papycha sign-in, a window opened once: its size is set,
    /// never remembered.
    /// </summary>
    private bool Remembers => Tool != WebTool.PapychaTracker || ShowsQuests;

    /// <summary>Centred on the guides window that opened it, or on the screen.</summary>
    private void PlaceSignIn()
    {
        var area = SystemParameters.WorkArea;

        Width = Math.Min(SignInWidth, area.Width);
        Height = Math.Min(SignInHeight, area.Height);

        var centre = Owner is { } owner
            ? new Point(owner.Left + (owner.ActualWidth / 2), owner.Top + (owner.ActualHeight / 2))
            : new Point(area.Left + (area.Width / 2), area.Top + (area.Height / 2));

        Left = Math.Clamp(centre.X - (Width / 2), area.Left, area.Right - Width);
        Top = Math.Clamp(centre.Y - (Height / 2), area.Top, area.Bottom - Height);
    }

    /// <summary>Remembered here: the handle is gone by OnClosed.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Remembers)
        {
            _ = _placements.SaveAsync(this, PlacementKey);
        }

        base.OnClosing(e);
    }
}
