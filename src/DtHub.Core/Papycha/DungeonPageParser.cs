using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads the structured block of a dungeon page.
///
/// As with quests, we only read what the site itself generates:
/// "section.pcd-info" is a homemade block whose classes are code, whereas
/// the body of the article is written by hand. The analysis therefore
/// relies on the classes and never on the labels: "Clef : " (French for
/// "Key: ") can be rewritten, the class "pcd-info__row--key" cannot.
///
/// Pure functions, no network: they are checked against saved fragments.
/// </summary>
public static partial class DungeonPageParser
{
    /// <summary>
    /// Size of the soul stone, exactly as the site writes it. Empty when
    /// the page does not have the block, which happens on one dungeon out
    /// of the eighty-three.
    /// </summary>
    public static string ParseSoulStone(string? html) =>
        Clean(SoulStonePattern().Match(html ?? string.Empty).Groups["value"].Value);

    /// <summary>
    /// Key required at the entrance, stripped of the "Clef : " (French
    /// for "Key: ") label that the site adds for screen readers. Empty on
    /// the nine dungeons that do not require one.
    /// </summary>
    public static string ParseKey(string? html)
    {
        var row = KeyRowPattern().Match(html ?? string.Empty);

        if (!row.Success)
        {
            return string.Empty;
        }

        // The screen reader label is removed before cleanup: once the
        // markup falls away, nothing would distinguish it from the key's
        // name anymore.
        var value = ScreenReaderPattern().Replace(row.Groups["value"].Value, " ");

        return Clean(value);
    }

    /// <summary>
    /// Level written in the prose, "Niveau : 190" (French for "Level:
    /// 190"). Zero when the page does not give one.
    ///
    /// Dungeons put it in their metadata; raids and lairs, except one,
    /// write it plainly in their first paragraphs. It is the only place
    /// we find it for nine out of ten.
    /// </summary>
    public static int ParseLevel(string? html)
    {
        var match = LevelPattern().Match(Clean(html));

        return match.Success
            && int.TryParse(match.Groups["value"].Value, out var level)
            && level is > 0 and <= 300
                ? level
                : 0;
    }

    /// <summary>
    /// Section titles of the page, in their order.
    ///
    /// A dungeon page is not a series of instructions but a dossier: the
    /// monsters, the rooms, the boss, the mechanics, the achievements.
    /// These are the titles we go through, not paragraphs to summarize.
    ///
    /// Two are excluded. "Position du PNJ sur la carte" (NPC position on
    /// the map) duplicates the map that the header block already carries;
    /// "Papycha remercie" (Papycha's thanks section) is the page footer,
    /// which the window hides anyway.
    /// </summary>
    public static IReadOnlyList<string> ParseSections(string? html)
    {
        List<string> titles = [];

        foreach (Match match in HeadingPattern().Matches(html ?? string.Empty))
        {
            var title = Clean(match.Groups["title"].Value);

            if (title.Length == 0 || IsAside(title) || titles.Contains(title, StringComparer.Ordinal))
            {
                continue;
            }

            titles.Add(title);
        }

        return titles;
    }

    private static bool IsAside(string title) =>
        title.StartsWith("Position du PNJ", StringComparison.OrdinalIgnoreCase)
        || title.StartsWith("Papycha remercie", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips markup, decodes entities and tightens spaces.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(TagPattern().Replace(value, " "));

        return SpacePattern().Replace(text, " ").Trim();
    }

    // The two second timeout set on each expression: a malformed page
    // must not make it run forever and freeze the window.
    [GeneratedRegex(
        @"class=""[^""]*\bpcd-info__soul-stone\b[^""]*""[^>]*>(?<value>.*?)</span>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        2000)]
    private static partial Regex SoulStonePattern();

    [GeneratedRegex(
        @"class=""[^""]*\bpcd-info__row--key\b[^""]*"".*?"
        + @"class=""[^""]*\bpcd-info__value\b[^""]*""[^>]*>(?<value>.*?)</strong>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        2000)]
    private static partial Regex KeyRowPattern();

    [GeneratedRegex(
        @"<span class=""screen-reader-text"">.*?</span>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        2000)]
    private static partial Regex ScreenReaderPattern();

    [GeneratedRegex(
        @"<h2[^>]*>(?<title>.*?)</h2>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase,
        2000)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(
        @"\bNiveau\s*:\s*(?<value>\d{1,3})\b",
        RegexOptions.IgnoreCase,
        2000)]
    private static partial Regex LevelPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
