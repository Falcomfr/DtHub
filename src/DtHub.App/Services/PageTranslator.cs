using DtHub.Core.Localization;

namespace DtHub.App.Services;

/// <summary>The web windows' Translate button, against the language chosen.</summary>
public static class PageTranslator
{
    /// <summary>
    /// The language chosen, and not CurrentUICulture: that one falls back to
    /// Windows' outside the startup context (see App.ApplyLanguage), and a
    /// French Windows then saw a French page in an English application.
    /// </summary>
    private static string AppLanguage => Strings.Spoken.TwoLetterISOLanguageName;

    /// <summary>True when a site written in this language needs translating for the player.</summary>
    public static bool Offers(string? siteLanguage) => PageTranslation.Offers(siteLanguage, AppLanguage);

    /// <summary>The page, translated into the application's language.</summary>
    public static string AddressFor(string pageUrl) => PageTranslation.Address(pageUrl, AppLanguage);
}
