using System.Globalization;
using System.Resources;

namespace DtHub.Core.Localization;

/// <summary>
/// The text shown to the user, in the current language.
///
/// The resources live in this project and not in the interface
/// one: two thirds of the visible text is written here, in the
/// domain, and a resource set hosted on the windows side would be
/// out of their reach.
///
/// A missing key is returned as-is rather than throwing: a strange
/// label on screen is better than a window that does not open, and
/// <c>StringsResourceTests</c> guarantees that none are missing.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("DtHub.Core.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>
    /// The language chosen for this run, once it has been chosen.
    ///
    /// **The ambient culture could not be trusted, and the screen
    /// proved it.** Setting the thread's culture at startup held long
    /// enough to build the windows, so every label written in XAML came
    /// out in the right language. Everything computed afterwards did
    /// not: a panel set to English showed "Devices" and "Shortcuts"
    /// beside "Connecté en Wi-Fi" and "ouvert à l'instant", because the
    /// sweep that produces those runs from a timer callback, outside
    /// the execution context the culture was set in.
    ///
    /// Holding the choice here settles it: what the application decided
    /// to speak does not depend on which thread happens to ask.
    /// </summary>
    private static CultureInfo? _chosen;

    /// <summary>
    /// The language the interface speaks, falling back to the thread's
    /// own while nothing has been chosen, which is what tests and the
    /// domain's own callers get.
    /// </summary>
    private static CultureInfo Spoken => _chosen ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// Fixes the language for the rest of the run.
    ///
    /// Called once, when the choice between the setting and Windows has
    /// been made. Passing <c>null</c> hands the answer back to the
    /// thread, which is what the tests need between two cases.
    /// </summary>
    public static void Speak(CultureInfo? culture) => _chosen = culture;

    /// <summary>
    /// Returns the text of this key in the interface language.
    /// </summary>
    public static string Get(string key)
        => Manager.GetString(key, Spoken) ?? key;

    /// <summary>
    /// Returns the text of this key, or <c>null</c> if it is not
    /// declared. Used for optional text, where absence is itself an
    /// answer: a brand sheet with no particular warning does not
    /// show one.
    /// </summary>
    public static string? Optional(string key)
        => Manager.GetString(key, Spoken);

    /// <summary>
    /// Returns the text of this key, with its placeholders filled
    /// in. Numbers and dates take the country's format there, which
    /// is a setting distinct from the language.
    /// </summary>
    public static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>
    /// Returns the text of this key in a named language. Used by
    /// tests, which must be able to read a language without
    /// changing the thread's own.
    ///
    /// The name differs from <see cref="Get(string)"/> on purpose:
    /// as an overload, analysis would require passing a culture
    /// everywhere, while following the interface's own is exactly
    /// what is wanted on screen.
    /// </summary>
    public static string GetIn(string key, CultureInfo culture)
        => Manager.GetString(key, culture) ?? key;
}
