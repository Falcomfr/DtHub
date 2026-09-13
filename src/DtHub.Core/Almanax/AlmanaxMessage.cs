using System.Text.Json;

namespace DtHub.Core.Almanax;

/// <summary>
/// What the page tells us about a day, and what we keep from it.
///
/// Parsing is lenient everywhere **except on one thing**: the
/// block must be DOFUS Touch's. The portal serves both games on
/// the same page, and DOFUS's Almanax calls for different items. A
/// missing field is replaced with emptiness; the wrong game is not
/// replaced with anything, and is refused.
/// </summary>
public static class AlmanaxMessage
{
    /// <summary>
    /// The day read, or <c>null</c> if the message cannot be used.
    /// </summary>
    /// <param name="json">What the bridge posted.</param>
    /// <param name="date">
    /// The requested day: the page does not carry it in plain
    /// sight.
    /// </param>
    public static AlmanaxDay? From(string? json, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // The bridge never posts any other kind, but it is
            // attached to any document the window loads, and any
            // page can call "postMessage" with whatever it wants.
            // Nothing to report: the window will say it could not
            // read it, which is the case.
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object || !AlmanaxReading.IsTouch(Text(root, "heading")))
        {
            return null;
        }

        var offering = AlmanaxReading.Clean(Text(root, "offering"));
        var read = AlmanaxReading.Offering(offering);

        var day = AlmanaxReading.Clean(Text(root, "day"));
        var month = AlmanaxReading.Clean(Text(root, "month"));

        return new AlmanaxDay(
            date,
            string.Join(' ', new[] { day, month }.Where(part => part.Length > 0)),
            offering,
            read?.Quantity,
            read?.Item,
            AlmanaxReading.AfterColon(Text(root, "bonus")),
            AlmanaxReading.Clean(Text(root, "bonusDetail")),
            AlmanaxReading.AfterColon(Text(root, "quest")),
            AlmanaxReading.Clean(Text(root, "meryde")),
            AlmanaxReading.Clean(Text(root, "monthEvent")));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
