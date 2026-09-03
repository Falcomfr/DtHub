using System.Text;
using System.Text.RegularExpressions;

namespace DtHub.Core.Diagnostics;

/// <summary>
/// Choisit, dans un fichier de journal, les lignes qui valent la peine d'être
/// envoyées.
///
/// Un fichier d'une journée fait mille sept cents lignes et cent cinquante
/// kilooctets, et sept lignes sur cent y sont des avertissements ou des
/// erreurs. Le reste dit le rythme d'usage, la configuration d'écrans et les
/// pages lues : rien qui aide à comprendre une faute, et beaucoup qui désigne
/// une personne.
///
/// On garde donc les avertissements et les erreurs de la session en cours, puis
/// les toutes dernières lignes pour savoir ce qu'on faisait au moment où c'est
/// arrivé. Sur mille sept cents lignes, cela en rend une trentaine.
///
/// La session compte : quatre cent huit démarrages ont été relevés en six
/// jours, tous mêlés dans sept fichiers. Sans elle, un rapport emporterait les
/// fautes de la veille.
/// </summary>
public static partial class LogDigest
{
    /// <summary>Ce qu'un rapport peut porter, en caractères.</summary>
    public const int MaxLength = 8000;

    /// <summary>Le mot que porte une ligne qu'on garde toujours.</summary>
    private static readonly string[] Loud = ["[WRN]", "[ERR]", "[FTL]"];

    /// <summary>
    /// Les lignes retenues, dans l'ordre du fichier.
    /// </summary>
    /// <param name="log">Le contenu du fichier de journal.</param>
    /// <param name="session">
    /// L'identifiant de la session en cours. Vide, tout le fichier est
    /// considéré : c'est le cas d'un journal écrit par une version d'avant.
    /// </param>
    /// <param name="tail">Combien de dernières entrées garder quoi qu'il arrive.</param>
    public static string Of(string? log, string? session, int tail = 20)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        List<string> entries = [.. Entries(log)];

        if (session is { Length: > 0 })
        {
            // La première entrée qui porte la marque, non la dernière : chaque
            // ligne de la session la porte, et c'est le début qu'on cherche.
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
            // Un blanc dit qu'on a sauté des lignes : sans lui, deux fautes
            // distantes d'une heure se liraient comme deux fautes de suite.
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
    /// La marque que porte chaque ligne d'une session, telle que le gabarit de
    /// journalisation l'écrit. Entre crochets, comme le niveau : un identifiant
    /// nu se confondrait avec un mot du message.
    /// </summary>
    public static string Mark(string session) => $"[{session}]";

    /// <summary>
    /// Les entrées du journal. Une entrée commence par un horodatage ; les
    /// lignes qui n'en portent pas la prolongent, et c'est ainsi qu'une pile
    /// d'appel reste avec le message qui l'a produite.
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
