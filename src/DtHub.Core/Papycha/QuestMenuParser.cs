using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Lit l'ordre dans lequel le site range ses rubriques de quêtes.
///
/// L'API donne les catégories par ordre alphabétique, ce qui n'a rien à voir
/// avec l'ordre que le site présente à ses lecteurs : les quêtes principales
/// d'abord, puis les répétables, puis les zones dans un ordre de progression.
/// Cet ordre-là n'existe que dans leur menu, et c'est celui qu'il faut suivre.
///
/// Fonction pure : elle se vérifie sur un fragment enregistré.
/// </summary>
public static partial class QuestMenuParser
{
    /// <summary>
    /// Intitulés des rubriques de quêtes, dans l'ordre du menu, réduits à une
    /// forme comparable.
    ///
    /// La lecture s'arrête à la première entrée qui sort de la branche des
    /// quêtes : le menu enchaîne ensuite les chemins et les guides, qui ne
    /// rangent rien ici.
    /// </summary>
    public static IReadOnlyList<string> ParseOrder(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var start = html.IndexOf("href=\"https://papycha.fr/quetes/\"", StringComparison.Ordinal);

        if (start < 0)
        {
            return [];
        }

        List<string> order = [];

        foreach (Match link in LinkPattern().Matches(html[start..]))
        {
            var url = link.Groups["url"].Value;

            // Le menu quitte les quêtes : tout ce qui suit range autre chose.
            if (!url.Contains("/quetes", StringComparison.OrdinalIgnoreCase)
                && order.Count > 0)
            {
                break;
            }

            var label = QuestSearch.Normalize(
                WebUtility.HtmlDecode(TagPattern().Replace(link.Groups["label"].Value, " ")));

            if (label.Length > 0 && !order.Contains(label, StringComparer.Ordinal))
            {
                order.Add(label);
            }
        }

        return order;
    }

    /// <summary>
    /// Rang d'une rubrique dans l'ordre du site, ou un rang de fin si le menu
    /// ne la mentionne pas.
    ///
    /// Les deux ne se nomment jamais pareil : le menu dit « Quêtes d'Astrub »
    /// pour la catégorie « Astrub », et « Quêtes de Frigost » pour « Île de
    /// Frigost ». Une égalité ne se produirait jamais, et une simple inclusion
    /// échouerait sur Frigost. On rapproche donc sur le mot qui distingue.
    /// </summary>
    public static int RankOf(IReadOnlyList<string> order, string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(order);

        var words = Distinctive(sectionKey);

        if (words.Count == 0)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < order.Count; i++)
        {
            foreach (var word in words)
            {
                if (order[i].Contains(word, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Mots d'un nom de rubrique qui la distinguent vraiment.
    ///
    /// « Île », « quêtes », « de » se retrouvent partout et rapprocheraient
    /// n'importe quoi de n'importe quoi. Ne restent que les noms propres et les
    /// mots assez longs pour être parlants.
    /// </summary>
    private static IReadOnlyList<string> Distinctive(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return [];
        }

        return
        [
            .. sectionKey
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 4 && !Common.Contains(w, StringComparer.Ordinal)),
        ];
    }

    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "quete", "quetes", "iles", "archipel", "region", "alentour", "monde", "douze",
    };

    [GeneratedRegex(
        @"<a[^>]*href=""(?<url>[^""]*)""[^>]*>(?<label>.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
