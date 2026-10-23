using System.Text;
using System.Text.Json;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads <c>window.PAPYCHA_QUEST_TRACKER = {...};</c> out of the
/// tracker page. This is the only thing we take from that HTML: the
/// rest of the page is markup we do not own.
///
/// Exactly one JSON value is read from the first brace, with a
/// reader, so whatever script follows on the page cannot interfere.
/// Pure function, no network.
/// </summary>
public static class TrackerPageParser
{
    private const string Marker = "PAPYCHA_QUEST_TRACKER";

    private static readonly TrackerReadResult Unreadable = new(TrackerReadOutcome.Unreadable, null);

    public static TrackerReadResult Parse(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return Unreadable;
        }

        var at = html.IndexOf(Marker, StringComparison.Ordinal);
        if (at < 0)
        {
            return Unreadable;
        }

        var equals = html.IndexOf('=', at);
        var brace = equals < 0 ? -1 : html.IndexOf('{', equals);
        if (brace < 0)
        {
            return Unreadable;
        }

        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(html[brace..]));
            using var doc = JsonDocument.ParseValue(ref reader);
            return Read(doc.RootElement);
        }
        catch (JsonException)
        {
            // Broken JSON is a normal outcome, reported as Unreadable.
            return Unreadable;
        }
    }

    private static TrackerReadResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Unreadable;
        }

        // Anonymous visitors get "space":null (or no space at all).
        if (!root.TryGetProperty("space", out var space) || space.ValueKind == JsonValueKind.Null)
        {
            return new TrackerReadResult(TrackerReadOutcome.NotLinked, null);
        }

        if (space.ValueKind != JsonValueKind.Object)
        {
            return Unreadable;
        }

        var views = root.TryGetProperty("views", out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
        var characters = new List<TrackerCharacter>();
        var progress = new Dictionary<string, TrackerProgress>();

        if (space.TryGetProperty("characters", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in list.EnumerateArray())
            {
                var id = Text(c, "id");
                if (id is null || Flag(c, "archived"))
                {
                    continue;
                }

                characters.Add(new TrackerCharacter(
                    id,
                    Text(c, "name") ?? string.Empty,
                    Number(c, "level") ?? 0,
                    Text(c, "gender") ?? "male",
                    Text(c, "class_key") ?? "cra",
                    Text(c, "color") ?? "#6b992f"));
                var view = views.ValueKind == JsonValueKind.Object && views.TryGetProperty(id, out var w) ? w : default;
                progress[id] = ReadProgress(c, view);
            }
        }

        var state = new TrackerState(
            Text(root, "nonce") ?? string.Empty,
            characters,
            Text(space, "active_character_id"),
            progress);

        return new TrackerReadResult(TrackerReadOutcome.Linked, state);
    }

    private static TrackerProgress ReadProgress(JsonElement character, JsonElement view)
    {
        var statuses = new Dictionary<int, QuestStatus>();

        // An empty map arrives as a JSON array (PHP), not an object.
        if (character.TryGetProperty("quest_statuses", out var raw) && raw.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in raw.EnumerateObject())
            {
                if (int.TryParse(p.Name, out var quest))
                {
                    statuses[quest] = QuestStatusCodes.FromSite(p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null);
                }
            }
        }

        var inner = view.ValueKind == JsonValueKind.Object && view.TryGetProperty("progress", out var pr) ? pr : default;
        var next = new List<int>();

        if (view.ValueKind == JsonValueKind.Object && view.TryGetProperty("nextQuests", out var n) && n.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in n.EnumerateArray())
            {
                var id = item.ValueKind == JsonValueKind.Object ? Number(item, "id") : item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var i) ? i : (int?)null;
                if (id is int quest)
                {
                    next.Add(quest);
                }
            }
        }

        return new TrackerProgress(statuses, Ids(inner, "completed_quests"), Ids(inner, "available_quests"), next);
    }

    private static HashSet<int> Ids(JsonElement parent, string name)
    {
        var set = new HashSet<int>();
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in a.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var i))
                {
                    set.Add(i);
                }
            }
        }

        return set;
    }

    private static string? Text(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? Number(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i) ? i : null;

    private static bool Flag(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
