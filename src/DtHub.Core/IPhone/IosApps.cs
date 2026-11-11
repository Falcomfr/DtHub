using System.Text.Json;

namespace DtHub.Core.IPhone;

/// <summary>The apps an iPhone lists, as the stream's control server prints them.</summary>
public static class IosApps
{
    /// <summary>
    /// The game's bundle id, read from the device as Ankama's may differ by
    /// store; <c>null</c> when DOFUS Touch is not installed.
    /// </summary>
    /// <exception cref="JsonException">The text is not that list.</exception>
    public static string? FindDofus(string appsJson)
    {
        using var document = JsonDocument.Parse(appsJson);

        return document.RootElement.EnumerateArray()
            .Where(app => string.Equals(app.GetProperty("name").GetString()?.Trim(), "DOFUS Touch", StringComparison.OrdinalIgnoreCase))
            .Select(app => app.GetProperty("bundle").GetString())
            .FirstOrDefault();
    }
}
