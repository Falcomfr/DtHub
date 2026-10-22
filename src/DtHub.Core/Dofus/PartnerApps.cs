namespace DtHub.Core.Dofus;

/// <summary>
/// A DOFUS Touch companion app by MakeInTouch, shown with its maker's
/// agreement (2026-10-10).
/// </summary>
/// <param name="PackageName">Its Android package.</param>
/// <param name="Name">Its name, the same in every language.</param>
/// <param name="LineKey">The resource key of its one line presentation.</param>
/// <param name="IconAsset">The pack path of its bundled icon.</param>
/// <param name="CanTurn">True when it lays itself out in landscape too.</param>
public sealed record PartnerApp(string PackageName, string Name, string LineKey, string IconAsset, bool CanTurn = false);

/// <summary>Which connected devices have one partner app, and which lack it.</summary>
/// <param name="App">The app.</param>
/// <param name="WithApp">Device ids where it is installed.</param>
/// <param name="WithoutApp">Device ids where it is not.</param>
public sealed record PartnerAvailability(
    PartnerApp App,
    IReadOnlyList<string> WithApp,
    IReadOnlyList<string> WithoutApp);

/// <summary>
/// The two MakeInTouch apps DT Hub presents. Constants, not a setting: the
/// spec names these two and no others.
/// </summary>
public static class PartnerApps
{
    public static IReadOnlyList<PartnerApp> All { get; } =
    [
        new("com.makeintouch.dtstuff", "DTStuff", "PartnerDtStuffLine", "/assets/dtstuff.png", CanTurn: true),
        new("com.makeintouch.ocrapp", "OcrApp", "PartnerOcrAppLine", "/assets/ocrapp.png"),
    ];

    public static bool IsPartner(string? packageName) =>
        All.Any(a => string.Equals(a.PackageName, packageName, StringComparison.Ordinal));

    /// <summary>
    /// The display of a window opened in passing: a portrait phone screen,
    /// or a landscape one for an app that can turn, when chosen. Measured
    /// on 2026-10-22 on the 13T Pro: DTStuff has a landscape layout, OcrApp
    /// turned its 1920x1080 display back to portrait within the second.
    /// </summary>
    public static (int Width, int Height) ToolDisplay(string? packageName, bool landscape) =>
        landscape && CanTurn(packageName) ? (1920, 1080) : (1080, 1920);

    public static bool CanTurn(string? packageName) =>
        All.Any(a => a.CanTurn && string.Equals(a.PackageName, packageName, StringComparison.Ordinal));

    /// <summary>The app's page in the Play Store, as Android opens it.</summary>
    public static string StoreUri(string packageName) => $"market://details?id={packageName}";

    /// <summary>
    /// The same availability, with one app now installed on one device:
    /// what a store window learns before the next sweep does.
    /// </summary>
    public static IReadOnlyList<PartnerAvailability> WithInstalled(
        IReadOnlyList<PartnerAvailability> availability,
        string packageName,
        string deviceId) =>
        [.. availability.Select(a =>
            !string.Equals(a.App.PackageName, packageName, StringComparison.Ordinal)
            || a.WithApp.Contains(deviceId, StringComparer.Ordinal)
                ? a
                : a with
                {
                    WithApp = [.. a.WithApp, deviceId],
                    WithoutApp = [.. a.WithoutApp.Where(d => !string.Equals(d, deviceId, StringComparison.Ordinal))],
                })];

    /// <summary>
    /// For each app, in <see cref="All" /> order, the devices that have it
    /// and those that do not.
    /// </summary>
    /// <param name="devices">
    /// Each connected device with the packages of its main profile, user 0.
    /// A clone profile is left out on purpose: the apps are opened from the
    /// main one.
    /// </param>
    public static IReadOnlyList<PartnerAvailability> Availability(
        IReadOnlyList<(string DeviceId, IReadOnlyList<string> PackagesUser0)> devices) =>
        [.. All.Select(app => new PartnerAvailability(
            app,
            [.. devices.Where(d => d.PackagesUser0.Contains(app.PackageName, StringComparer.Ordinal)).Select(d => d.DeviceId)],
            [.. devices.Where(d => !d.PackagesUser0.Contains(app.PackageName, StringComparer.Ordinal)).Select(d => d.DeviceId)]))];
}

/// <summary>The DOFUS Touch tools that are web sites, each in a DT Hub window.</summary>
public enum WebTool
{
    /// <summary>The interactive map of the resources.</summary>
    Map,

    /// <summary>The damage calculators.</summary>
    Calcofus,

    /// <summary>The lines of sight of DofusRoom's Insightroom, Touch maps only.</summary>
    DofusRoom,

    /// <summary>
    /// The player's quest tracker on papycha.fr, opened to link it. Not a
    /// tool tile: the guides window opens it.
    /// </summary>
    PapychaTracker,

    /// <summary>Lomitall's DOFUS Touch portal.</summary>
    Lomitall,
}

/// <summary>
/// The DOFUS Touch map, first of the tools: a web site shown in a DT Hub
/// window, which needs no phone. Its logo is drawn for DT Hub, not the
/// site's own.
/// </summary>
public static class DofusTouchMap
{
    /// <summary>The site, as given by the project owner on 2026-10-11.</summary>
    public const string Address = "https://www.dofus-touch-map.com/";

    /// <summary>The pack path of its bundled logo.</summary>
    public const string IconAsset = "/assets/dtmap.png";

    /// <summary>Its donation page, the link its own banner carries.</summary>
    public const string DonationAddress = "https://www.dofus-touch-map.com/donation";

    /// <summary>
    /// The hosts its ads came from on 2026-10-11, both of the Adsterra
    /// network, which set off the project owner's antivirus on opening.
    /// Not loaded in DT Hub, by the owner's choice: the site then shows its
    /// donation banner in their place.
    /// </summary>
    // ponytail: the two hosts seen; Adsterra rotates domains, add any new
    // one the antivirus names.
    public static IReadOnlyList<string> BlockedAdHosts { get; } = ["highperformanceformat.com", "topcreativeformat.com"];

    /// <summary>True for a request to one of those hosts or their subdomains.</summary>
    public static bool IsBlockedAd(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && BlockedAdHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// DofusBook, the stuff builder: a link only, opened in the browser, as the
/// project owner asked (2026-10-11). Shown in a window on 2026-10-14, it set
/// off the owner's antivirus: it stays in the browser.
/// </summary>
public static class DofusBook
{
    public const string Address = "https://touch.dofusbook.net/";

    /// <summary>The pack path of its bundled logo.</summary>
    public const string IconAsset = "/assets/dofusbook.png";
}

/// <summary>
/// Lomitall.fr, a DOFUS Touch portal, in a large DT Hub window, beside the
/// guides at the project owner's request (2026-10-14).
/// </summary>
public static class Lomitall
{
    public const string Address = "https://lomitall.fr/";

    /// <summary>The pack path of its bundled logo, the site's own.</summary>
    public const string IconAsset = "/assets/lomitall.png";

    /// <summary>
    /// True for a page of the portal, the www form included. Its links to
    /// the other tools leave it.
    /// </summary>
    public static bool IsOwnPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("lomitall.fr", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.lomitall.fr", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Calcofus, the damage calculators of DOFUS Touch: a web site shown in a
/// narrow DT Hub window beside the game. Its owner gave DT Hub the right
/// to its name and logo (2026-10-11).
/// </summary>
public static class Calcofus
{
    public const string Address = "https://calcofus.fr/";

    /// <summary>The pack path of its bundled logo.</summary>
    public const string IconAsset = "/assets/calcofus.png";

    /// <summary>
    /// Where the window opens: the calculator last used, when it is one of
    /// the site's pages, its home otherwise. A link followed out of the
    /// site, to Discord or a stuff library, is not one to come back to.
    /// </summary>
    public static string StartAddress(string? remembered) =>
        IsOwnPage(remembered) ? new Uri(remembered!).AbsoluteUri : Address;

    /// <summary>
    /// True for a page of the site, by its host, the www form included:
    /// compared as text, an escaped character sent an in-site page out.
    /// </summary>
    public static bool IsOwnPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("calcofus.fr", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.calcofus.fr", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// DofusRoom's Insightroom, the lines of sight of the fight maps, cut down
/// to its DOFUS Touch category in a DT Hub window. The project owner holds
/// the right to its name, logo and to change how it shows (2026-10-13). Its
/// ads stay, by the owner's choice.
/// </summary>
public static class DofusRoom
{
    public const string Address = "https://www.dofusroom.com/insightroom";

    /// <summary>The pack path of its bundled logo.</summary>
    public const string IconAsset = "/assets/dofusroom.png";

    /// <summary>
    /// Run in each new document: selects the Touch category and hides the
    /// site's menus. The map editor fills the window and hides its own
    /// "Close" button, on the site too: its small grey cross was the only
    /// way back to the list, so it is enlarged in the site's orange, and the
    /// mouse's back button closes the editor instead of leaving the page.
    /// The editor also lost its rounded corners, which left a gap under the
    /// title bar, and the twelve pixels its body scrolled into nothing. An
    /// ad Google left empty ("unfill") is hidden: two of them made 340
    /// pixels of blank page on 2026-10-13; an ad that shows stays. The category is found by its name, not its id; when it
    /// is not, the page stays whole rather than strand the player on
    /// another category with no menu. The site wires its clicks some time
    /// after load, hence the retries. Ads that spill past the window's edge
    /// are clipped, not hidden: a 728 pixel AdSense frame widened the page
    /// at 600 and 700 pixels on 2026-10-13.
    /// </summary>
    public const string TouchOnlyScript = """
        window === window.top && window.addEventListener('DOMContentLoaded', () => {
          const touch = [...document.querySelectorAll('[data-loadable-category]')]
            .find(a => a.querySelector('span:last-of-type')?.textContent.trim() === 'Dofus Touch');
          const panel = touch && document.querySelector(`[data-panel-id="${touch.id}"]`);
          if (!panel) return;
          const style = document.createElement('style');
          style.textContent = `
            html, body { overflow-x: hidden; }
            nav.navbar, footer, #back-to-top,
            #app_root > .row > div:has(> #insightroom-nav) { display: none !important; }
            #app_root > .row > div:has([data-panel-id]) { flex: 0 0 100%; max-width: 100%; }
            #map-modal .modal-header .close { font-size: 2.5rem; opacity: 1; color: #FDB950; text-shadow: none; }
            #map-modal .modal-content { border: 0; border-radius: 0; }
            #map-modal .modal-body { overflow: hidden; }
            ins.adsbygoogle[data-ad-status^="unfill"] { display: none !important; }`;
          document.head.appendChild(style);
          document.addEventListener('mouseup', e => {
            if (e.button !== 3) return;
            e.preventDefault();
            window.jQuery?.('#map-modal').modal('hide');
          });
          let tries = 0;
          const timer = setInterval(() => {
            if (!panel.classList.contains('d-none') || ++tries > 50) return clearInterval(timer);
            touch.click();
          }, 200);
        });
        """;

    /// <summary>
    /// True for the Insightroom page, by host, the bare form included, and
    /// path. Its maps open in a dialog on that same page, so any other
    /// address is a link out of the tool.
    /// </summary>
    public static bool IsOwnPage(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("dofusroom.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("www.dofusroom.com", StringComparison.OrdinalIgnoreCase))
        && uri.AbsolutePath.TrimEnd('/').Equals("/insightroom", StringComparison.OrdinalIgnoreCase);
}
