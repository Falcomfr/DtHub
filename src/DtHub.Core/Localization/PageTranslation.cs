namespace DtHub.Core.Localization;

/// <summary>
/// The Translate button of the web windows. Papycha, lomitall.fr and
/// Calcofus are only written in French: a player in English or Spanish
/// gets the page translated into the application's language, in their
/// own browser, by Google Translate.
///
/// In the browser and not in the window: the window shows the site as its
/// owners agreed to, keeps the Papycha sign-in and the ad filter, and the
/// page reaches Google only when the player asks for it.
///
/// The site's language is known, not read from the page: Calcofus, in
/// French, declares lang="en" (checked on 2026-10-17).
/// </summary>
public static class PageTranslation
{
    /// <summary>The language of Papycha, lomitall.fr and Calcofus.</summary>
    public const string French = "fr";

    /// <summary>
    /// True when the site is written in another language than the
    /// application. A site of unknown language, such as the map that
    /// follows ours, gets no button.
    /// </summary>
    public static bool Offers(string? pageLanguage, string appLanguage) =>
        Primary(pageLanguage) is { Length: > 0 } page
        && !string.Equals(page, Primary(appLanguage), StringComparison.OrdinalIgnoreCase);

    /// <summary>The page, translated into the application's language.</summary>
    public static string Address(string pageUrl, string appLanguage) =>
        "https://translate.google.com/translate?sl=auto&tl=" + Primary(appLanguage)
        + "&u=" + Uri.EscapeDataString(pageUrl);

    /// <summary>"fr-FR", "es_ES": only the part before the separator names the language.</summary>
    private static string Primary(string? language)
    {
        var trimmed = (language ?? string.Empty).Trim();
        var separator = trimmed.IndexOfAny(['-', '_']);

        return (separator >= 0 ? trimmed[..separator] : trimmed).ToLowerInvariant();
    }
}
