using System.IO;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using DtHub.App.Services;
using DtHub.App.Windows;
using DtHub.Core.Dofus;
using DtHub.Core.Localization;
using DtHub.Core.Papycha;
using DtHub.Core.Settings;
using DtHub.Mac.Platform;

using Serilog;

namespace DtHub.Mac.Views;

/// <summary>
/// The PC's WebToolWindow (src/DtHub.App/Windows/WebToolWindow.xaml.cs): a
/// DOFUS Touch tool that is a web site, shown in DT Hub's own engine like
/// the guides. One window per tool, each with its own place. The ads are
/// refused by WebKit content rules before any request leaves, where the
/// PC both resolves their hosts to nothing and refuses their requests.
/// </summary>
internal sealed partial class WebToolWindow : Window
{
    /// <summary>The share of the work area the map takes the first time.</summary>
    private const double MapShare = 0.65;

    /// <summary>Lomitall's first height: wanted large by the project owner (2026-10-14).</summary>
    private const double LomitallHeightShare = 0.85;

    /// <summary>Lomitall's first width, the PC's: its top bar needs 1458 pixels.</summary>
    private const double LomitallWidth = 1480;

    /// <summary>Calcofus's first width: its narrow layout, readable at 460.</summary>
    private const double CalcofusWidth = 480;

    private const double CalcofusHeightShare = 0.85;

    /// <summary>DofusRoom's first width: its map editor, still readable at 600.</summary>
    private const double DofusRoomWidth = 600;

    /// <summary>The Papycha sign-in window: two cards and a button.</summary>
    private const double SignInWidth = 560;

    private const double SignInHeight = 820;

    /// <summary>The player's quests: the dashboard rarely runs long.</summary>
    private const double QuestsHeight = 960;

    private readonly WindowPlacements _placements = null!;
    private readonly SettingsService _settings = null!;
    private readonly IDialogService _dialogs = null!;
    private WebTool _tool;
    private bool _showsQuests;

    /// <summary>For the designer only.</summary>
    public WebToolWindow() => InitializeComponent();

    public WebToolWindow(
        WindowPlacements placements,
        SettingsService settings,
        IDialogService dialogs)
    {
        InitializeComponent();

        _placements = placements;
        _settings = settings;
        _dialogs = dialogs;

        // At its place from the first frame; PlaceAsync settles it once open.
        _placements.PlaceBeforeShow(this, () => Remembers ? PlacementKey : null);

        View.NavigationCompleted += OnPageLoaded;
        Failure.Retry += OnRetry;

        Opened += async (_, _) =>
        {
            await PlaceAsync().ConfigureAwait(true);
            await OpenAsync().ConfigureAwait(true);
        };
    }

    /// <summary>Which tool this window shows. Set before it is shown.</summary>
    public WebTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Retitle();
        }
    }

    /// <summary>
    /// For the Papycha tracker: true opens on the player's quests, false on
    /// signing in. Set before the window is shown.
    /// </summary>
    public bool ShowsQuests
    {
        get => _showsQuests;
        set
        {
            _showsQuests = value;
            Retitle();
        }
    }

    /// <summary>A guide of the site picked in the tracker, for the guides window to open.</summary>
    public event EventHandler<string>? GuideRequested;

    /// <summary>The character the tracker shows. Set before the window is shown.</summary>
    public string? TrackerCharacterId { get; set; }

    /// <summary>A character picked on the site, for DT Hub's bar to follow.</summary>
    public event EventHandler<string>? CharacterPicked;

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

    /// <summary>The map and DofusRoom open in our language; the other three only exist in French.</summary>
    private string? SiteLanguage => Tool is WebTool.Map or WebTool.DofusRoom ? null : PageTranslation.French;

    /// <summary>False for the Papycha sign-in, a window opened once: its size is set, never remembered.</summary>
    private bool Remembers => Tool != WebTool.PapychaTracker || ShowsQuests;

    private void Retitle()
    {
        Title = Strings.Get(Tool switch
        {
            WebTool.Map => "ToolMapName",
            WebTool.Calcofus => "ToolCalcofusName",
            WebTool.PapychaTracker => ShowsQuests ? "TrackerWindowTitle" : "TrackerSignInTitle",
            WebTool.Lomitall => "ToolLomitallName",
            _ => "ToolDofusRoomName",
        });
        Donate.IsVisible = Tool == WebTool.Map;
    }

    private async Task OpenAsync()
    {
        if (PageTranslator.Offers(SiteLanguage))
        {
            Translate.IsVisible = true;
            OpenPage.IsVisible = false;
        }

        try
        {
            // The ad hosts are refused in every view, before its first
            // request: lomitall.fr serves the map's ads. See MacWebView.
            View.NewWindowRequested += OnNewWindowRequested;

            if (Tool == WebTool.Map)
            {
                View.Navigate(DofusTouchMap.Address);
                return;
            }

            View.NavigationStarting += OnToolNavigating;

            if (Tool == WebTool.PapychaTracker)
            {
                await OpenTrackerAsync().ConfigureAwait(true);
                return;
            }

            if (Tool == WebTool.DofusRoom)
            {
                View.AddScriptToExecuteOnDocumentCreated(DofusRoom.TouchOnlyScript);
                View.Navigate(DofusRoom.Address);
                return;
            }

            if (Tool == WebTool.Lomitall)
            {
                View.Navigate(Lomitall.Address);
                return;
            }

            View.SourceChanged += OnCalcofusPageChanged;

            var document = await _settings.GetAsync().ConfigureAwait(true);
            View.Navigate(Calcofus.StartAddress(document.LastCalcofusUrl));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Log.Warning(exception, "L'outil web {Outil} n'a pas pu s'ouvrir.", Tool);
        }
    }

    /// <summary>
    /// Shows another character, picked in DT Hub's bar: written where the
    /// site looks for it, and the page reloaded.
    /// </summary>
    public async Task ShowCharacterAsync(string id)
    {
        if (id == TrackerCharacterId || !View.IsReady)
        {
            return;
        }

        TrackerCharacterId = id;

        try
        {
            await View.ExecuteScriptAsync(
                "localStorage.setItem('pqt_active_character'," + JsonSerializer.Serialize(id) + ");location.reload();")
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ObjectDisposedException or TaskCanceledException)
        {
            // The window closing under the call: nothing left to show.
            Log.Warning(exception, "Le personnage n'a pas pu être montré dans le suivi Papycha.");
        }
    }

    private void OnTrackerMessage(object? sender, string payload)
    {
        if (TrackerCharacterMessage.Parse(payload) is { } id && id != TrackerCharacterId)
        {
            TrackerCharacterId = id;
            CharacterPicked?.Invoke(this, id);
        }
    }

    /// <summary>
    /// The tracker page, framed down to its own content. Signing in, only the
    /// cookie the site then sets is looked for: a new phrase is a cookie with
    /// another value than the one at opening.
    /// </summary>
    private async Task OpenTrackerAsync()
    {
        _openingCookie = await TrackerCookieValueAsync().ConfigureAwait(true);
        View.AddScriptToExecuteOnDocumentCreated(QuestBridge.Script(framingOnly: true));
        View.AddScriptToExecuteOnDocumentCreated(QuestBridge.TrackerScript(ShowsQuests, TrackerCharacterId));

        if (ShowsQuests)
        {
            View.WebMessageReceived += OnTrackerMessage;
        }

        View.NavigationCompleted += OnTrackerNavigated;

        // The quests are on the site's home page once signed in; the tracker
        // page only manages the account.
        View.Navigate(ShowsQuests ? PapychaSite.Root : PapychaSite.TrackerUrl);
    }

    private string? _openingCookie;

    private Task<string?> TrackerCookieValueAsync() =>
        View.GetCookieAsync(PapychaSite.TrackerCookie, PapychaSite.Root);

    private async void OnTrackerNavigated(object? sender, WebNavigationCompleted e)
    {
        try
        {
            var current = await TrackerCookieValueAsync().ConfigureAwait(true);

            if (current is not null && current != _openingCookie)
            {
                View.NavigationCompleted -= OnTrackerNavigated;

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
    /// A page that did not come shows why, in its place, as WebView2's
    /// error page does; one that came takes its place back.
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

    private void OnRetry(object? sender, EventArgs e)
    {
        ShowFailure(null);

        try
        {
            View.Navigate(View.LastNavigation ?? HomeAddress);
        }
        catch (Exception exception) when (exception is ArgumentException or ObjectDisposedException)
        {
            Log.Warning(exception, "L'outil web {Outil} n'a pas pu s'ouvrir.", Tool);
        }
    }

    private void OnOpenInBrowser(object? sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(View.Source is { Length: > 0 } page ? page : HomeAddress);

    private void OnDonate(object? sender, RoutedEventArgs e) => _dialogs.OpenUrl(DofusTouchMap.DonationAddress);

    private void OnTranslate(object? sender, RoutedEventArgs e) =>
        _dialogs.OpenUrl(PageTranslator.AddressFor(View.Source is { Length: > 0 } page ? page : HomeAddress));

    /// <summary>
    /// target="_blank" goes to the system browser, except the tools' ads,
    /// refused here so a pop-under cannot reach the browser this way.
    /// </summary>
    private void OnNewWindowRequested(object? sender, string uri)
    {
        if (DofusTouchMap.IsBlockedAd(uri))
        {
            Log.Information("{Outil} : fenêtre publicitaire refusée ({Hote}).", Tool, new Uri(uri).Host);

            return;
        }

        _dialogs.OpenUrl(uri);
    }

    /// <summary>
    /// A link out of the tool goes to the browser: the window is for the
    /// tool alone.
    /// </summary>
    private void OnToolNavigating(object? sender, WebNavigationStarting e)
    {
        // A guide picked in the tracker reads in the guides window.
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
    private void OnCalcofusPageChanged(object? sender, EventArgs e)
    {
        var url = View.Source;

        if (Calcofus.IsOwnPage(url))
        {
            _ = RememberCalcofusPageAsync(url);
        }
    }

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

    /// <summary>Where it was left, else the PC's first place for the tool.</summary>
    private async Task PlaceAsync()
    {
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
        var area = WorkArea.Of(this);

        if (Tool is WebTool.Map or WebTool.Lomitall)
        {
            var wide = Tool == WebTool.Map ? area.Width * MapShare : Math.Min(LomitallWidth, area.Width);
            var tall = area.Height * (Tool == WebTool.Map ? MapShare : LomitallHeightShare);
            WorkArea.Place(this, area.X + ((area.Width - wide) / 2), area.Y + ((area.Height - tall) / 2), wide, tall);
            return;
        }

        // Against the right edge, where it covers the least of a game
        // window centred or docked on the left.
        var width = Math.Min(Tool == WebTool.Calcofus ? CalcofusWidth : DofusRoomWidth, area.Width);
        var height = Tool == WebTool.PapychaTracker
            ? Math.Min(QuestsHeight, area.Height * CalcofusHeightShare)
            : area.Height * CalcofusHeightShare;
        WorkArea.Place(this, area.Right - width, area.Y + ((area.Height - height) / 2), width, height);
    }

    /// <summary>Centred on the guides window that opened it, or on the screen.</summary>
    private void PlaceSignIn()
    {
        var area = WorkArea.Of(this);
        var width = Math.Min(SignInWidth, area.Width);
        var height = Math.Min(SignInHeight, area.Height);
        var centre = Owner is Window owner ? WorkArea.Centre(owner) : area.Center;

        WorkArea.Place(
            this,
            Math.Clamp(centre.X - (width / 2), area.Left, area.Right - width),
            Math.Clamp(centre.Y - (height / 2), area.Top, area.Bottom - height),
            width,
            height);
    }

    /// <summary>Remembered here: the handle is gone by Closed.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Remembers)
        {
            _ = _placements.SaveAsync(this, PlacementKey);
        }

        base.OnClosing(e);
    }

    /// <summary>The engine let go with the window, as the guides and Almanax windows do.</summary>
    protected override void OnClosed(EventArgs e)
    {
        View.Dispose();

        base.OnClosed(e);
    }
}
