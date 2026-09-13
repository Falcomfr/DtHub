using System.Globalization;

namespace DtHub.Core.Almanax;

/// <summary>
/// The Almanax of DOFUS Touch, at Ankama.
///
/// **Touch does not have the same Almanax as DOFUS**, and that is the
/// only real trap in this function: a wrong offering costs a day of
/// questing to whoever follows it. Measured on the official site on
/// September 10, 2026, the same page depending on the filter: "1 Aile
/// de dragodinde" ("1 Dragon Turkey Wing") for DOFUS, "1 Dent de
/// Dragodinde" ("1 Dragon Turkey Tooth") for Touch. On the 11th, "2
/// Corne de Dragoeuf Guerrier" ("2 Warrior Dragoegg Horns") on the
/// Touch side.
///
/// **No API serves Touch.** When probed, "api.dofusdu.de" only
/// responds for "dofus3"; "dofustouch", "touch" and "retro" are
/// unknown routes. The one dedicated application, Almafus, left the
/// Play Store in 2024. The libraries out there all scrape the same
/// portal, without its filter, and therefore return the Almanax of
/// DOFUS.
///
/// **Reading the calendar from the phone was tried, and does not
/// work.** The Touch client is a fifteen-megabyte Cordova wrapper that
/// downloads its assets and stores them in its internal storage.
/// Probed on a real device: the application's external storage is
/// empty, there is no OBB, nothing from Ankama elsewhere on the card,
/// "/data/data" refuses, and "run-as" answers that the package is not
/// debuggable. This holds for every non-rooted phone, hence for
/// everyone's.
///
/// What remains is Ankama's page, which is shown as is. Nothing is
/// copied, nothing is hosted, so nothing can go stale in silence: the
/// authoritative source itself is displayed, and the filter is in the
/// address.
/// </summary>
public static class AlmanaxCalendar
{
    /// <summary>Host name of the portal that publishes the Almanax.</summary>
    public const string Host = "krosmoz.com";

    /// <summary>
    /// The filter that switches the page to DOFUS Touch.
    ///
    /// The portal remembers the choice for the session, but the
    /// address carries it too, and that is the form we use: a window
    /// that depended on a cookie already set would show the Almanax
    /// of DOFUS on first opening, that is, the offering for a
    /// different game, with nothing saying so.
    /// </summary>
    public const string TouchFilter = "?game=dofustouch";

    /// <summary>
    /// The address of today's Almanax, in the requested language.
    ///
    /// The portal publishes in eight languages and the application
    /// speaks three of them: the mapping is direct, and anything we
    /// do not know falls back to the neutral language rather than
    /// inventing a path that does not exist.
    /// </summary>
    public static string UrlFor(string? language)
    {
        var code = (language ?? string.Empty).Trim().ToLowerInvariant();

        var path = code switch
        {
            "fr" => "fr",
            "es" => "es",
            _ => "en",
        };

        return "https://www." + Host + "/" + path + "/almanax" + TouchFilter;
    }

    /// <summary>
    /// The address of a specific day.
    ///
    /// The portal accepts the date in the path, in ISO format. It is
    /// always included, even for today: the bare address does return
    /// the current day, but it does so according to the server's
    /// clock, and the window states a date read from the machine's
    /// own clock. The two cross paths around midnight, and the date
    /// shown would then no longer be that of the data.
    /// </summary>
    public static string UrlFor(string? language, DateOnly date)
    {
        var root = UrlFor(language);
        var filter = root.IndexOf('?', StringComparison.Ordinal);

        return string.Concat(
            root.AsSpan(0, filter),
            "/",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.AsSpan(filter));
    }

    /// <summary>
    /// True when the address is an Almanax page of the portal.
    ///
    /// The window has no address bar: a page is seen there without
    /// knowing where it comes from, under our title and our icon. It
    /// therefore only receives the Almanax, and the rest goes to the
    /// browser. This is the same caution as for guide pages, and for
    /// the same reason.
    ///
    /// The comparison is against the host the address parser returns,
    /// not against the start of the text: "https://krosmoz.com@
    /// ailleurs.example/" ("ailleurs" means "elsewhere") does start
    /// with the portal's name without belonging to it.
    /// </summary>
    public static bool Owns(string? url)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            return false;
        }

        var known = string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase);

        if (!known)
        {
            return false;
        }

        // "/fr/almanax", "/fr/almanax/2026-09-10", "/fr/almanax/aide"
        // ("help"): the language first, the section next. The rest of
        // the portal, its forums and its shop, has no business in a
        // window that says "Almanax".
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2 && string.Equals(parts[1], "almanax", StringComparison.OrdinalIgnoreCase);
    }
}
