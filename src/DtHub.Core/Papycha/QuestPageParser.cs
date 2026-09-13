using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Reads the structured blocks of a quest page: the introduction and
/// the progression.
///
/// We only read what the site itself generates. The body of the
/// article is written by hand by contributors and its markup varies
/// from one quest to another: one quest puts its objectives in
/// green, another in bold. Trying to parse it would be guessing. The
/// two blocks handled here, by contrast, come from homemade blocks,
/// and their markup is the same everywhere, checked on quests of
/// different types.
///
/// The analysis relies on the classes, never on the labels: "Succès
/// associé" (Related achievement) can be rewritten, the class
/// "pqa-quest-intro__fact--successes" is code.
///
/// Pure function, no network: it is checked against saved fragments.
/// </summary>
public static partial class QuestPageParser
{
    /// <summary>
    /// Reads the introduction block. Returns empty facts if it is
    /// missing.
    /// </summary>
    public static QuestFacts ParseFacts(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new QuestFacts();
        }

        var (step, count) = ParseStep(FactValue(html, "successes-step") ?? FactValue(html, "step"));

        return new QuestFacts
        {
            Success = FactValue(html, "successes"),
            Finality = FactValue(html, "finality"),
            Type = TaxonomyValue(html, "Type"),
            StepNumber = step,
            StepCount = count,
            Start = StartValue(html),
        };
    }

    /// <summary>
    /// Reads the progression block. Returns an empty chain if it is
    /// missing.
    /// </summary>
    public static QuestChain ParseChain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new QuestChain();
        }

        return new QuestChain
        {
            Previous = ColumnLinks(html, "previous"),
            Next = ColumnLinks(html, "next"),
        };
    }

    /// <summary>
    /// Value of a fact, designated by its class suffix.
    ///
    /// The container carries "pqa-quest-intro__fact--&lt;name&gt;"
    /// and the value lives in the &lt;dd&gt; that follows.
    /// </summary>
    private static string? FactValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $@"pqa-quest-intro__fact--{Regex.Escape(name)}\b.*?<dd[^>]*>(?<value>.*?)</dd>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            MatchTimeout);

        return match.Success ? Clean(match.Groups["value"].Value) : null;
    }

    /// <summary>
    /// Value of a classification, designated by its heading.
    ///
    /// Unlike facts, these pairs have no distinctive class: we do
    /// have to rely on the label, for lack of anything better.
    /// </summary>
    private static string? TaxonomyValue(string html, string label)
    {
        var block = Regex.Match(
            html,
            @"pqa-quest-intro__taxonomies\b.*?</dl>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            MatchTimeout);

        if (!block.Success)
        {
            return null;
        }

        var match = Regex.Match(
            block.Value,
            $@"<dt[^>]*>\s*{Regex.Escape(label)}\s*</dt>\s*<dd[^>]*>(?<value>.*?)</dd>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            MatchTimeout);

        return match.Success ? Clean(match.Groups["value"].Value) : null;
    }

    private static string? StartValue(string html)
    {
        var match = Regex.Match(
            html,
            @"pqa-quest-intro__lead[^>]*>(?<value>.*?)</p>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            MatchTimeout);

        return match.Success ? Clean(match.Groups["value"].Value) : null;
    }

    /// <summary>
    /// "Étape 3/4" (Step 3/4) becomes (3, 4). Any other text returns
    /// (0, 0): better no chain at all than a made-up position.
    /// </summary>
    private static (int Step, int Count) ParseStep(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (0, 0);
        }

        var match = Regex.Match(text, @"(?<step>\d+)\s*/\s*(?<count>\d+)", RegexOptions.None, MatchTimeout);

        if (!match.Success
            || !int.TryParse(match.Groups["step"].Value, CultureInfo.InvariantCulture, out var step)
            || !int.TryParse(match.Groups["count"].Value, CultureInfo.InvariantCulture, out var count))
        {
            return (0, 0);
        }

        return (step, count);
    }

    private static List<QuestLink> ColumnLinks(string html, string column)
    {
        var block = Regex.Match(
            html,
            $@"pqt-progress__column--{Regex.Escape(column)}\b(?<body>.*?)</section>\s*(?=<section class=""pqt-progress__column|</nav>)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase,
            MatchTimeout);

        var body = block.Success ? block.Groups["body"].Value : null;

        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        List<QuestLink> links = [];

        foreach (Match anchor in LinkPattern().Matches(body))
        {
            var url = Clean(anchor.Groups["url"].Value);
            var title = Clean(TitlePattern().Match(anchor.Value) is { Success: true } strong
                ? strong.Groups["title"].Value
                : anchor.Groups["inner"].Value);

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title))
            {
                continue;
            }

            links.Add(new QuestLink(title, url, KindOf(anchor.Groups["class"].Value)));
        }

        return links;
    }

    private static QuestLinkKind KindOf(string classes) => classes switch
    {
        var c when c.Contains("--success", StringComparison.OrdinalIgnoreCase) => QuestLinkKind.Success,
        var c when c.Contains("quest", StringComparison.OrdinalIgnoreCase) => QuestLinkKind.Quest,
        _ => QuestLinkKind.Milestone,
    };

    /// <summary>Strips markup, decodes entities and tightens spaces.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(TagPattern().Replace(value, " "));

        return SpacePattern().Replace(text, " ").Trim() is { Length: > 0 } cleaned ? cleaned : null;
    }

    /// <summary>
    /// A malformed page must not make the expression run forever:
    /// beyond that, we return what we have rather than freeze the
    /// window.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    [GeneratedRegex(
        @"<a[^>]*class=""(?<class>[^""]*)""[^>]*href=""(?<url>[^""]*)""[^>]*>(?<inner>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<strong[^>]*>(?<title>.*?)</strong>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitlePattern();


    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
