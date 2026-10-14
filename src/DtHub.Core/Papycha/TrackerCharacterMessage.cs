using System.Text.Json;

namespace DtHub.Core.Papycha;

/// <summary>
/// What the tracker window's page says when the player picks another
/// character on the site, so that DT Hub follows:
/// <c>{"kind":"trackerCharacter","id":"..."}</c>. The page also carries the
/// guide bridge, which posts its own messages: anything else is ignored.
/// </summary>
public static class TrackerCharacterMessage
{
    public const string Kind = "trackerCharacter";

    /// <summary>The character's id, or null when the payload is not this message.</summary>
    public static string? Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == Kind
                && root.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch (JsonException)
        {
            // Any page may post anything: what is not JSON is not ours.
            return null;
        }
    }
}
