using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads the classification the site maintains by hand: the table
/// on its "Quêtes" (Quests) page, then the quests each of the pages
/// it lists enumerates.
///
/// Pure functions: they can be verified on recorded fragments,
/// without a network.
/// </summary>
public static partial class QuestSectionPageParser
{
    /// <summary>
    /// Sections of the table on the "Quêtes" (Quests) page, in the
    /// order the site arranges them.
    ///
    /// The table is the only place on the site where this
    /// classification exists: neither the API nor the categories
    /// carry it.
    /// </summary>
    public static IReadOnlyList<QuestPageSection> ParseIndex(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var table = TablePattern().Match(html);

        if (!table.Success)
        {
            return [];
        }

        List<QuestPageSection> sections = [];

        foreach (Match cell in CellPattern().Matches(table.Value))
        {
            var link = LinkPattern().Match(cell.Value);

            if (!link.Success)
            {
                continue;
            }

            var url = Internal(link.Groups["url"].Value);
            var name = Text(link.Groups["label"].Value);

            if (url.Length == 0
                || name.Length == 0
                || sections.Any(s => string.Equals(s.Url, url, StringComparison.Ordinal)))
            {
                continue;
            }

            sections.Add(new QuestPageSection { Name = name, Url = url });
        }

        return sections;
    }

    /// <summary>
    /// Addresses of the quests listed by a section page.
    ///
    /// The content is what the API returns, without menu or footer:
    /// any link to the site there is a link of the section, and
    /// there is no need to guess where the article begins.
    /// </summary>
    public static IReadOnlyList<string> ParseQuestLinks(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        List<string> urls = [];

        foreach (Match link in LinkPattern().Matches(content))
        {
            var url = Internal(link.Groups["url"].Value);

            if (url.Length > 0 && !urls.Contains(url, StringComparer.Ordinal))
            {
                urls.Add(url);
            }
        }

        return urls;
    }

    /// <summary>
    /// Groups within a section page: a bold subheading, then the
    /// quests it heads until the next subheading.
    ///
    /// Measured across the twenty-two pages: ninety-nine
    /// achievements, which attach three hundred and seventy-three
    /// quests out of seven hundred and eighty-two. The rest of the
    /// quests are headed by no subheading at all, and nothing
    /// should claim otherwise.
    /// </summary>
    public static IReadOnlyList<QuestPageGroup> ParseGroups(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var parts = HeadingPattern().Split(content);

        List<QuestPageGroup> groups = [];

        // The first piece comes before the first subheading: it
        // belongs to no group. After that, pieces come in pairs,
        // the heading then what it covers.
        for (var i = 1; i + 1 < parts.Length; i += 2)
        {
            var raw = Text(parts[i]).TrimEnd(':', ' ', ' ').Trim();
            var success = SuccessPattern().Match(raw);
            var name = success.Success ? success.Groups["name"].Value.Trim() : raw;

            if (name.Length == 0)
            {
                continue;
            }

            List<string> urls = [];

            foreach (Match link in LinkPattern().Matches(parts[i + 1]))
            {
                var url = Internal(link.Groups["url"].Value);

                if (url.Length > 0 && !urls.Contains(url, StringComparer.Ordinal))
                {
                    urls.Add(url);
                }
            }

            if (urls.Count > 0)
            {
                groups.Add(new QuestPageGroup
                {
                    Name = name,
                    IsSuccess = success.Success,
                    QuestUrls = urls,
                });
            }
        }

        return groups;
    }

    /// <summary>
    /// Address reduced to a comparable form. Neither the anchor nor
    /// the trailing slash should decide whether two links point to
    /// the same page.
    ///
    /// Without a domain filter: matching two addresses and deciding
    /// whether a link leads to the site are two distinct questions,
    /// and mixing them would make the matching depend on the host.
    /// </summary>
    public static string Key(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var value = WebUtility.HtmlDecode(url).Trim();
        var anchor = value.IndexOf('#', StringComparison.Ordinal);

        if (anchor >= 0)
        {
            value = value[..anchor];
        }

        return value.TrimEnd('/');
    }

    /// <summary>
    /// Address of a site link, reduced to its comparable form, or
    /// an empty string if it leads elsewhere. A section page also
    /// cites the wiki and social networks: those do not classify
    /// any quest.
    /// </summary>
    private static string Internal(string? url)
    {
        var value = Key(url);

        return PapychaSite.Owns(value) ? value : string.Empty;
    }

    private static string Text(string? html) =>
        WebUtility.HtmlDecode(TagPattern().Replace(html ?? string.Empty, " "))
            .Replace(' ', ' ')
            .Trim();

    [GeneratedRegex("<table.*?</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TablePattern();

    [GeneratedRegex("<td[^>]*>.*?</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CellPattern();

    [GeneratedRegex(
        @"<a[^>]*href=""(?<url>[^""]*)""[^>]*>(?<label>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(
        @"<p[^>]*>\s*<strong>(.*?)</strong>\s*</p>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^\[\s*Succ[eè]s\s*\]\s*(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SuccessPattern();
}
