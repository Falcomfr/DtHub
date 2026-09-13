using System.IO;
using System.Reflection;
using System.Text.Json;

namespace DtHub.App.Windows;

/// <summary>
/// The script injected into the site's pages.
///
/// It provides two services that nothing forces to be taken
/// together: framing, which strips the site's decoration so a page
/// fits in a narrow window, and step tracking, which only makes
/// sense on a quest guide. The linked pages window only takes the
/// first one: what it shows is just as much a category page as a
/// map, where there is neither a step to describe nor a chain to
/// follow.
/// </summary>
internal static class QuestBridge
{
    /// <summary>
    /// The bridge, as injected before the document is created.
    /// </summary>
    /// <param name="framingOnly">
    /// True to keep only the framing. The article's title is then
    /// kept: on a category page it is the only landmark, and that
    /// window does not show any other title in its own banner.
    /// </param>
    public static string Script(bool framingOnly = false)
    {
        var source = Read("DtHub.App.quest-bridge.js");

        return framingOnly ? "window.__dtHubFramingOnly = true;\n" + source : source;
    }

    /// <summary>
    /// The script that prepares the site's report form.
    ///
    /// It does not go alongside the bridge, it replaces it: the
    /// bridge hides the article footer, and that is exactly where
    /// the form is found.
    ///
    /// The step marker goes through a JSON string, produced by the
    /// serializer: it is also a valid JavaScript string, and a
    /// guide's text contains quotes and apostrophes that would
    /// otherwise need to be escaped by hand, missing a case.
    /// </summary>
    public static string ReportScript(string? location) =>
        Read("DtHub.App.papycha-report.js")
            .Replace(
                "__DTHUB_LOCATION__",
                JsonSerializer.Serialize(location ?? string.Empty),
                StringComparison.Ordinal);

    private static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"La ressource « {name} » est absente de l'assembly.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
