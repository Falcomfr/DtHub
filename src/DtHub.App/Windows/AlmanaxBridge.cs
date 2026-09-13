using System.IO;
using System.Reflection;

namespace DtHub.App.Windows;

/// <summary>
/// The script that reads the Almanax page.
///
/// Separate from the guides' bridge: that one frames a page to
/// display it, and is written for another site's structure. Here
/// nothing is framed, we read, and the page is never shown.
/// </summary>
internal static class AlmanaxBridge
{
    /// <summary>
    /// The bridge, as it is injected before document creation.
    /// </summary>
    public static string Script()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("DtHub.App.almanax-bridge.js")
            ?? throw new InvalidOperationException(
                "La ressource « DtHub.App.almanax-bridge.js » est absente de l'assembly.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
