using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Diagnostics;

/// <summary>
/// Chooses, within a log file, the lines worth sending.
///
/// A one day file runs to one thousand seven hundred lines and one
/// hundred fifty kilobytes, and seven lines in a hundred are
/// warnings or errors. The rest tells the pace of use, the screen
/// configuration and the pages read: nothing that helps understand a
/// fault, and much that identifies a person.
///
/// We therefore keep the warnings and errors of the current session,
/// plus the very last lines to know what was happening at the moment
/// it occurred. Out of one thousand seven hundred lines, this yields
/// about thirty.
///
/// The session matters: four hundred eight startups were recorded
/// over six days, all mixed together in seven files. Without it, a
/// report would drag in the previous day's faults.
/// </summary>
public static partial class LogDigest
{
    /// <summary>What a report can carry, in characters.</summary>
    public const int MaxLength = 8000;

    /// <summary>The word carried by a line we always keep.</summary>
    private static readonly string[] Loud = ["[WRN]", "[ERR]", "[FTL]"];

    /// <summary>
    /// The retained lines, in the file's order.
    /// </summary>
    /// <param name="log">The log file's content.</param>
    /// <param name="session">
    /// The identifier of the current session. When empty, the whole
    /// file is considered: this is the case for a log written by an
    /// earlier version.
    /// </param>
    /// <param name="tail">
    /// How many of the last entries to keep no matter what.
    /// </param>
    public static string Of(string? log, string? session, int tail = 20)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        List<string> entries = [.. Entries(log)];

        if (session is { Length: > 0 })
        {
            // The first entry carrying the marker, not the last:
            // every line of the session carries it, and it is the
            // start we are looking for.
            var start = entries.FindIndex(e => e.Contains(Mark(session), StringComparison.Ordinal));

            if (start > 0)
            {
                entries.RemoveRange(0, start);
            }
        }

        HashSet<int> kept = [];

        for (var i = 0; i < entries.Count; i++)
        {
            if (Loud.Any(level => entries[i].Contains(level, StringComparison.Ordinal)))
            {
                kept.Add(i);
            }
        }

        for (var i = Math.Max(0, entries.Count - tail); i < entries.Count; i++)
        {
            kept.Add(i);
        }

        var text = new StringBuilder();
        var previous = -1;

        foreach (var i in kept.Order())
        {
            // A blank indicates that lines were skipped: without it,
            // two faults an hour apart would read as two faults in a
            // row.
            if (previous >= 0 && i > previous + 1)
            {
                text.Append("\n[…]\n");
            }

            text.Append(entries[i]).Append('\n');
            previous = i;

            if (text.Length > MaxLength)
            {
                text.Append("[…]\n");
                break;
            }
        }

        return text.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// The marker carried by every line of a session, exactly as the
    /// logging template writes it. In brackets, like the level: a
    /// bare identifier would be confused with a word in the message.
    /// </summary>
    public static string Mark(string session) => $"[{session}]";

    /// <summary>
    /// The log's entries. An entry begins with a timestamp; the
    /// lines that lack one extend it, and that is how a stack trace
    /// stays with the message that produced it.
    /// </summary>
    private static IEnumerable<string> Entries(string log)
    {
        var current = new StringBuilder();

        foreach (var line in log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (StampPattern().IsMatch(line))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString().TrimEnd('\n');
                }

                current.Clear();
            }

            current.Append(line).Append('\n');
        }

        if (current.Length > 0)
        {
            yield return current.ToString().TrimEnd('\n');
        }
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", RegexOptions.None, 500)]
    private static partial Regex StampPattern();
}
