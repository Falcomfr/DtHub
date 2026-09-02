using System.Net;
using System.Text.RegularExpressions;

namespace DtHub.Core.Papycha;

/// <summary>
/// Lit le bloc structuré d'une page de donjon.
///
/// Comme pour les quêtes, on ne lit que ce que le site engendre lui-même :
/// « section.pcd-info » est un bloc maison dont les classes sont du code, là où
/// le corps de l'article est écrit à la main. L'analyse s'appuie donc sur les
/// classes et jamais sur les libellés — « Clef : » peut être réécrit, la classe
/// « pcd-info__row--key » non.
///
/// Fonctions pures, sans réseau : elles se vérifient sur des fragments
/// enregistrés.
/// </summary>
public static partial class DungeonPageParser
{
    /// <summary>
    /// Taille de la pierre d'âme, telle que le site l'écrit. Vide quand la page
    /// n'a pas le bloc, ce qui arrive sur un donjon des quatre-vingt-trois.
    /// </summary>
    public static string ParseSoulStone(string? html) =>
        Clean(SoulStonePattern().Match(html ?? string.Empty).Groups["value"].Value);

    /// <summary>
    /// Clef exigée à l'entrée, débarrassée du « Clef : » que le site pose à
    /// l'usage des lecteurs d'écran. Vide sur les neuf donjons qui n'en
    /// demandent pas.
    /// </summary>
    public static string ParseKey(string? html)
    {
        var row = KeyRowPattern().Match(html ?? string.Empty);

        if (!row.Success)
        {
            return string.Empty;
        }

        // Le libellé de lecture d'écran est retiré avant le nettoyage : une fois
        // le balisage tombé, plus rien ne le distinguerait du nom de la clef.
        var value = ScreenReaderPattern().Replace(row.Groups["value"].Value, " ");

        return Clean(value);
    }

    /// <summary>
    /// Niveau écrit dans la prose, « Niveau : 190 ». Zéro quand la page n'en
    /// donne pas.
    ///
    /// Les donjons le mettent dans leurs métadonnées ; les raids et les
    /// tanières, sauf une, l'écrivent en clair dans leurs premiers paragraphes.
    /// C'est le seul endroit où on le trouve pour neuf des dix.
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
    /// Titres des sections de la page, dans leur ordre.
    ///
    /// Une page de donjon n'est pas une suite de consignes mais un dossier :
    /// les monstres, les salles, le boss, la mécanique, les succès. Ce sont ces
    /// titres qu'on parcourt, et non des paragraphes à résumer.
    ///
    /// Deux sont écartés. « Position du PNJ sur la carte » double la carte que
    /// le bloc d'en-tête porte déjà ; « Papycha remercie » est le pied de page,
    /// que la fenêtre masque de toute façon.
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

    /// <summary>Retire le balisage, décode les entités et resserre les espaces.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = WebUtility.HtmlDecode(TagPattern().Replace(value, " "));

        return SpacePattern().Replace(text, " ").Trim();
    }

    // Le délai de deux secondes posé sur chaque expression : une page mal
    // formée ne doit pas la faire tourner sans fin et bloquer la fenêtre.
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
