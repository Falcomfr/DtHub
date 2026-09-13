using System.Text.Json;

// What the old bridge reading did, against what a page can post.
string[] charges =
[
    "{}", "[]", "null", "42", "\"loaded\"",
    "{\"genre\":\"loaded\"}",
    "{\"kind\":\"step\"}",
    "{\"kind\":\"step\",\"index\":\"4\"}",
    "{\"kind\":\"loaded\",\"steps\":[1,2]}",
];

foreach (var charge in charges)
{
    try
    {
        using var document = JsonDocument.Parse(charge);
        var root = document.RootElement;

        switch (root.GetProperty("kind").GetString())
        {
            case "loaded":
                _ = root.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
                    ? steps.EnumerateArray().Select(s => s.GetString() ?? string.Empty).ToList()
                    : [];
                break;
            case "step":
                _ = root.GetProperty("index").GetInt32();
                break;
        }

        Console.WriteLine($"  passe   {charge}");
    }
    catch (JsonException)
    {
        Console.WriteLine($"  rattrapé {charge}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  LEVE {ex.GetType().Name,-28} {charge}");
    }
}
