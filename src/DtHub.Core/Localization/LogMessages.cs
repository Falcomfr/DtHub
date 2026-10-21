using System.Globalization;
using System.Resources;

namespace DtHub.Core.Localization;

/// <summary>
/// Translates stable diagnostic templates at rendering time. Keys are the
/// original templates so generated LoggerMessage methods keep their constant
/// templates and named properties. Unknown or external messages pass through.
/// </summary>
public static class LogMessages
{
    private static readonly ResourceManager Manager =
        new("DtHub.Core.Localization.LogMessages", typeof(LogMessages).Assembly);

    public static string Translate(string template, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Manager.GetString(template, culture ?? Strings.Spoken) ?? template;
    }
    /// <summary>Formats owned diagnostic prose without changing raw argument values.</summary>
    public static string Format(string template, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Translate(template), arguments);
}
