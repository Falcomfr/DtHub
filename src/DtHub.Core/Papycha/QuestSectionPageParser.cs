using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Lit le classement que le site tient à la main : le tableau de sa page
/// « Quêtes », puis les quêtes que chacune des pages ainsi listées énumère.
///
/// Fonctions pures : elles se vérifient sur des fragments enregistrés, sans
/// réseau.
/// </summary>
public static partial class QuestSectionPageParser
{
    /// <summary>
    /// Rubriques du tableau de la page « Quêtes », dans l'ordre où le site les
    /// range.
    ///
    /// Le tableau est le seul endroit du site où ce classement existe : ni
    /// l'API ni les catégories ne le portent.
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
    /// Adresses des quêtes énumérées par une page de rubrique.
    ///
    /// Le contenu est celui que rend l'API, sans menu ni pied de page : tout
    /// lien vers le site y est un lien de la rubrique, et il n'y a pas à
    /// deviner où commence l'article.
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
    /// Groupes d'une page de rubrique : un intertitre en gras, puis les quêtes
    /// qu'il coiffe jusqu'à l'intertitre suivant.
    ///
    /// Mesuré sur les vingt-deux pages : quatre-vingt-dix-neuf succès, qui
    /// rattachent trois cent soixante-treize quêtes sur sept cent quatre-vingt-
    /// deux. Le reste des quêtes n'est coiffé par aucun intertitre, et rien ne
    /// doit prétendre le contraire.
    /// </summary>
    public static IReadOnlyList<QuestPageGroup> ParseGroups(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var parts = HeadingPattern().Split(content);

        List<QuestPageGroup> groups = [];

        // Le premier morceau précède le premier intertitre : il ne relève
        // d'aucun groupe. Ensuite les morceaux vont par deux, l'intitulé puis
        // ce qu'il coiffe.
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
    /// Adresse ramenée à une forme comparable. Ni l'ancre ni la barre finale ne
    /// doivent décider si deux liens désignent la même page.
    ///
    /// Sans filtre de domaine : rapprocher deux adresses et décider si un lien
    /// mène au site sont deux questions distinctes, et les mêler ferait que le
    /// rapprochement dépend de l'hébergeur.
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
    /// Adresse d'un lien du site, ramenée à sa forme comparable, ou une chaîne
    /// vide s'il mène ailleurs. Une page de rubrique cite aussi le wiki et les
    /// réseaux sociaux : eux ne rangent aucune quête.
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
