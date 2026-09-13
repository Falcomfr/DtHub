using System.Text.Json;

namespace DtHub.Core.Papycha;

/// <summary>
/// What the script placed in the page tells the window, once parsed.
///
/// Parsing lives here, outside the window, because it has to hold up
/// against anything. The script is placed on every document the
/// window loads, and any page can post its own message: the bridge is
/// not a conversation with ourselves.
///
/// The window used to read "kind" without precaution. A message
/// lacking that field would then raise an exception in an event
/// handler, where nobody catches it, which took the application down
/// with it; a message that was not text would raise one even before
/// being read. Three lines of JavaScript on a page of the site were
/// enough.
/// </summary>
public sealed record QuestBridgeMessage
{
    /// <summary>
    /// The guide is framed and read: here is what it contains.
    /// </summary>
    public const string Loaded = "loaded";

    /// <summary>The page has been scrolled to another step.</summary>
    public const string Step = "step";

    /// <summary>What the message announces, "loaded" or "step".</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The guide's introduction block, as is.</summary>
    public string? Intro { get; init; }

    /// <summary>The guide's chain block, as is.</summary>
    public string? Chain { get; init; }

    /// <summary>Each step, with its nature.</summary>
    public IReadOnlyList<QuestStep> Steps { get; init; } = [];

    /// <summary>
    /// True when the first step is indeed the quest's starting point.
    /// </summary>
    public bool Departure { get; init; }

    /// <summary>Rank of the step reached.</summary>
    public int Index { get; init; }


    /// <summary>
    /// Parses a message, or returns <c>null</c> when there is nothing
    /// to get from it.
    ///
    /// Nothing is required besides the kind, and the rank for a step
    /// message: everything else is missing in places on the site
    /// itself, a dungeon page having neither a starting block nor a
    /// chain.
    /// </summary>
    public static QuestBridgeMessage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Silence assumed: a message the bridge did not write, or
            // wrote wrong, says nothing about the page. It is ignored
            // rather than letting the window crash on text coming
            // from the site.
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var name = kind.GetString() ?? string.Empty;

            if (string.Equals(name, Step, StringComparison.Ordinal))
            {
                return root.TryGetProperty("index", out var index)
                    && index.ValueKind == JsonValueKind.Number
                    && index.TryGetInt32(out var rank)
                    ? new QuestBridgeMessage { Kind = Step, Index = rank }
                    : null;
            }

            return new QuestBridgeMessage
            {
                Kind = name,
                Intro = Text(root, "intro"),
                Chain = Text(root, "chain"),
                Steps = Lines(root),
                Departure = Flag(root, "departure"),
            };
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<QuestStep> Lines(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. steps.EnumerateArray().Select(StepOf)];
    }

    /// <summary>
    /// Parses a step, under either of the two forms.
    ///
    /// The bridge writes it as an object, with text and a nature. A
    /// bare string is accepted for what it is, a step with no
    /// declared nature, hence an instruction: this is the form the
    /// bridge wrote before it carried the nature, and it is also all
    /// that a page of the site could post on its own. The rank, for
    /// its part, is kept no matter what: an unreadable step becomes an
    /// empty step and not one step fewer, otherwise the ranks would
    /// shift.
    /// </summary>
    private static QuestStep StepOf(JsonElement step) => step.ValueKind switch
    {
        JsonValueKind.String => new QuestStep(step.GetString() ?? string.Empty, IsTitle: false),
        JsonValueKind.Object => new QuestStep(
            Text(step, "text") ?? string.Empty,
            Flag(step, "title")),
        _ => new QuestStep(string.Empty, IsTitle: false),
    };
}

/// <summary>
/// A step of a guide, as the bridge reports it.
///
/// The nature drives the display. A section title exposes and shows
/// itself as is; an instruction directs, and the window shows nothing
/// of it, the rank being enough to get there. The distinction comes
/// from the site: a dungeon, raid, lair or path page reads through
/// its titles, a quest guide through its paragraphs.
/// </summary>
/// <param name="Text">
/// The step's text, empty for the starting point of a place.
/// </param>
/// <param name="IsTitle">
/// True when the step is a section title from the site.
/// </param>
public readonly record struct QuestStep(string Text, bool IsTitle);
