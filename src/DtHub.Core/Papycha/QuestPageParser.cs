using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Lit les blocs structurés d'une page de quête : l'introduction et la
/// progression.
///
/// On ne lit que ce que le site engendre lui-même. Le corps de l'article est
/// écrit à la main par des contributeurs et son balisage varie d'une quête à
/// l'autre : une quête met ses objectifs en vert, une autre en gras. Vouloir
/// le découper serait deviner. Les deux blocs traités ici viennent au contraire
/// de blocs maison, et leur balisage est le même partout, vérifié sur des
/// quêtes de types différents.
///
/// L'analyse s'appuie sur les classes, jamais sur les libellés : « Succès
/// associé » peut être réécrit, la classe « pqa-quest-intro__fact--successes »
/// est du code.
///
/// Fonction pure, sans réseau : elle se vérifie sur des fragments enregistrés.
/// </summary>
public static partial class QuestPageParser
{
    /// <summary>Lit le bloc d'introduction. Rend des faits vides s'il manque.</summary>
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

    /// <summary>Lit le bloc de progression. Rend une chaîne vide s'il manque.</summary>
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
    /// Valeur d'un fait, désignée par le suffixe de sa classe.
    ///
    /// Le conteneur porte « pqa-quest-intro__fact--&lt;nom&gt; » et la valeur vit
    /// dans le &lt;dd&gt; qui suit.
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
    /// Valeur d'un classement, désignée par son intitulé.
    ///
    /// Contrairement aux faits, ces couples n'ont pas de classe distinctive :
    /// il faut bien s'appuyer sur le libellé, faute de mieux.
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
    /// « Étape 3/4 » devient (3, 4). Tout autre texte rend (0, 0) : mieux vaut
    /// pas de chaîne du tout qu'une position inventée.
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

    /// <summary>Retire le balisage, décode les entités et resserre les espaces.</summary>
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
    /// Une page mal formée ne doit pas faire tourner l'expression sans fin :
    /// au-delà, on rend ce qu'on a plutôt que de bloquer la fenêtre.
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
