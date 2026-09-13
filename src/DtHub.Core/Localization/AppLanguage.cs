namespace DtHub.Core.Localization;

/// <summary>
/// Chooses the interface language.
///
/// The rule is one that can be explained in one sentence: Windows'
/// display language if we serve it, English otherwise, and a manual
/// setting that overrides both. The comparison is done on the first
/// two letters: "fr-BE" and "es-419" are served as "fr" and "es",
/// which avoids enumerating regional variants.
/// </summary>
public static class AppLanguage
{
    /// <summary>
    /// The language embedded in the assembly, the one that remains
    /// when nothing matches.
    /// </summary>
    public const string Neutral = "en";

    /// <summary>
    /// The translated languages, with the neutral one first.
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = [Neutral, "fr", "es"];

    /// <summary>
    /// Returns the language to apply, from the application setting
    /// (empty to follow Windows) and Windows' display language.
    /// </summary>
    public static string Choose(string? preferred, string? windows)
        => Match(preferred) ?? Match(windows) ?? Neutral;

    /// <summary>
    /// True when a restart is needed for the choice to show, that is,
    /// when the language it designates is not the one already
    /// displayed.
    ///
    /// The prompt used to appear on any setting change and never
    /// went back down: reverting to the starting language, in other
    /// words backing out, still left the prompt showing, for an
    /// application that had nothing left to change. What matters is
    /// not whether the setting was touched, but whether the choice
    /// differs from what is displayed.
    ///
    /// "Follow Windows" is resolved the same way as at startup:
    /// choosing it while Windows already speaks the displayed
    /// language therefore asks for nothing either.
    /// </summary>
    /// <param name="preferred">
    /// The chosen setting, empty to follow Windows.
    /// </param>
    /// <param name="windows">Windows' display language.</param>
    /// <param name="inForce">The language currently displayed.</param>
    public static bool NeedsRestart(string? preferred, string? windows, string? inForce) =>
        !string.Equals(Choose(preferred, windows), Choose(inForce, null), StringComparison.Ordinal);

    /// <summary>True if this language is translated.</summary>
    public static bool Serves(string? culture) => Match(culture) is not null;

    /// <summary>
    /// Returns the language served for this culture, or nothing.
    /// </summary>
    private static string? Match(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        // "fr-BE", "es_MX": only the part before the separator names
        // the language.
        var language = culture.Trim();
        var separator = language.IndexOfAny(['-', '_']);

        if (separator >= 0)
        {
            language = language[..separator];
        }

        foreach (var served in Supported)
        {
            if (string.Equals(served, language, StringComparison.OrdinalIgnoreCase))
            {
                return served;
            }
        }

        return null;
    }
}
