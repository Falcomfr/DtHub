using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Updates;

/// <summary>
/// Makes the release note readable, as it arrives in the repository's
/// markup language.
///
/// No library for this: the note is a bullet list and two or three
/// headings, and embedding a full rendering engine to display it
/// would cost more than it is worth. What is not recognized is left
/// as is, which is the worst acceptable case: the raw text is read.
/// </summary>
public static partial class ReleaseNotes
{
    /// <summary>The note, stripped of its markup.</summary>
    public static string Readable(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        var lines = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var built = new StringBuilder();
        var blank = 0;

        foreach (var raw in lines)
        {
            var line = Clean(raw);

            if (line.Length == 0)
            {
                blank++;

                continue;
            }

            if (built.Length > 0)
            {
                _ = built.Append('\n');

                // Only one blank separator line: the repository puts two
                // or three between its blocks, which would leave gaps in
                // a panel only a few lines tall.
                if (blank > 0)
                {
                    _ = built.Append('\n');
                }
            }

            blank = 0;
            _ = built.Append(line);
        }

        return built.ToString();
    }

    private static string Clean(string raw)
    {
        var line = raw.TrimEnd();

        // Headings lose their hash marks, not their text.
        line = HeadingPattern().Replace(line, string.Empty);

        // A bullet becomes a bullet.
        line = BulletPattern().Replace(line, "•  ");

        // Bold, italics and code are not rendered here: their markers
        // would hinder reading more than they would help it.
        line = EmphasisPattern().Replace(line, "$1");
        line = CodePattern().Replace(line, "$1");

        // A link keeps its text and loses its address.
        line = LinkPattern().Replace(line, "$1");

        return line.Trim();
    }

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s*", RegexOptions.None, 2000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+", RegexOptions.None, 2000)]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"\*{1,3}([^*]+)\*{1,3}", RegexOptions.None, 2000)]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"`([^`]+)`", RegexOptions.None, 2000)]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.None, 2000)]
    private static partial Regex LinkPattern();
}
