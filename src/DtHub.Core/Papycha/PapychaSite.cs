namespace DtHub.Core.Papycha;

/// <summary>
/// What belongs to the guide site, and what does not.
///
/// The question already came up when reading category pages, which
/// also cite the wiki and social networks. It matters most for
/// deciding what our windows are allowed to load: they have no
/// address bar, they carry our frame, and the bridge sits on every
/// document there. A page handing us a link would then open any
/// address, from any host, up to "file://", under our colors and
/// without anything saying where we were.
///
/// The comparison is on the host the address parser returns, not on
/// the start of the text: "https://papycha.fr@ailleurs.example/"
/// does start with the site's name without belonging to it, and the
/// parser returns "ailleurs".
/// </summary>
public static class PapychaSite
{
    /// <summary>Host name of the site.</summary>
    public const string Host = "papycha.fr";

    /// <summary>Root of the site.</summary>
    public const string Root = "https://" + Host + "/";

    /// <summary>
    /// Search URL of the site for this text, or <c>null</c> when
    /// there is nothing to search for.
    ///
    /// Our catalog only knows titles: quests, zones, achievements,
    /// dungeons and paths. The site, for its part, searches inside
    /// the body of its articles, so in items, monsters and
    /// characters that we do not index. This is the fallback when
    /// searching for something we do not have.
    ///
    /// The form is WordPress's, <c>?s=</c>, proven on the site: it
    /// returns "Search results for: …". The path form, <c>/search/…</c>,
    /// also responds, but the former is the canonical one.
    /// </summary>
    public static string? SearchUrl(string? query)
    {
        var wanted = (query ?? string.Empty).Trim();

        return wanted.Length == 0 ? null : Root + "?s=" + Uri.EscapeDataString(wanted);
    }

    /// <summary>
    /// True when the address is a page of the site, in plain terms
    /// a secure address of the site's host or one of its subdomains.
    ///
    /// Subdomains are allowed because the site itself uses at least
    /// one: "www" redirects to the bare name, and the redirect is
    /// announced as a navigation before it is followed.
    ///
    /// Plain HTTP is refused, as it is for the browser: the nine
    /// hundred and eighteen addresses in the catalog are all in
    /// "https".
    /// </summary>
    public static bool Owns(string? url) =>
        Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && (string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + Host, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Path of the achievement tree page, without its slashes.
    /// </summary>
    private const string SuccessPath = "succes";

    /// <summary>
    /// True when the address is the site's achievement tree page.
    ///
    /// **The only page of the site that we send back to the
    /// browser**, and the exception deserves its reason. Everything
    /// else opens in our own windows: our windows frame the page,
    /// strip the chrome and keep the reader inside their guide.
    ///
    /// This one is not a guide but a tool. It is a tree you unfold,
    /// that you browse, whose branches you follow, and it is easier
    /// to handle in a real browser, with its tabs and its history.
    /// Opened in ours, it still displayed correctly though: this is
    /// not a display defect we are fixing, it is one window too
    /// many before the button that finally led where the user
    /// wanted to go.
    ///
    /// The comparison is on the path alone. The page lives at
    /// <c>/succes/?pqt_success=…#succes-selectionne</c>: neither
    /// the query string, which names the chosen achievement, nor
    /// the anchor change its nature.
    /// </summary>
    public static bool IsSuccessTree(string? url) =>
        Owns(url)
        && Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
        && string.Equals(
            uri.AbsolutePath.Trim('/'),
            SuccessPath,
            StringComparison.OrdinalIgnoreCase);
}
