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
    /// Returns the text of this key in the interface language.
    /// </summary>
    public static string Get(string key)
        => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Returns the text of this key, or <c>null</c> if it is not
    /// declared. Used for optional text, where absence is itself an
    /// answer: a brand sheet with no particular warning does not
    /// show one.
    /// </summary>
    public static string? Optional(string key)
        => Manager.GetString(key, CultureInfo.CurrentUICulture);

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
