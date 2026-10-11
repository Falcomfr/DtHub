using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DtHub.Core.Almanax;

/// <summary>
/// The portal's week page, reduced to each day's bonus title.
///
/// One page instead of seven: it lists the seven days after today,
/// with the bonus and the quest of both games, and no offering. That
/// is enough to write the bonus under each day of the strip, which is
/// what tells, at a glance, which day is for farming, for gathering,
/// or for a quest elsewhere.
///
/// The page names its days in the World of Twelve's calendar, "12
/// Octolliard". The ISO date is read from the "know more" link
/// instead, which carries it, sometimes followed by the event's name
/// when a day has two: the day is then listed twice, and kept once.
/// </summary>
public static partial class AlmanaxWeek
{
    /// <summary>
    /// Each day's Touch bonus, empty if the message cannot be used.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, string> From(string? json)
    {
        var days = new Dictionary<DateOnly, string>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return days;
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Same reason as the day's message: any page can call
            // "postMessage". The strip then simply stays without its
            // bonuses, which is what it was before.
            return days;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("week", out var week)
            || week.ValueKind != JsonValueKind.Array)
        {
            return days;
        }

        foreach (var line in week.EnumerateArray())
        {
            var label = Text(line, "label");

            // "Bonus DOFUS TOUCH :" in the three languages. The game's
            // name is the same safeguard as on the day's page: DOFUS's
            // bonuses are not Touch's.
            if (!AlmanaxReading.IsTouch(label)
                || !label.TrimStart().StartsWith("Bonus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = AlmanaxReading.Clean(Text(line, "value"));
            var date = IsoDate().Match(Text(line, "href"));

            if (value.Length > 0
                && date.Success
                && DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                days.TryAdd(day, value);
            }
        }

        return days;
    }

    private static string Text(JsonElement line, string name) =>
        line.ValueKind == JsonValueKind.Object
        && line.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    [GeneratedRegex(@"(?<=/almanax/)\d{4}-\d{2}-\d{2}")]
    private static partial Regex IsoDate();
}
