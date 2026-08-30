using System.Globalization;
using System.Text;

namespace DtHub.Core.Papycha;

/// <summary>
/// Filtrage des quêtes et des rubriques à la frappe.
///
/// Les titres du site sont en français et pleins d'accents et d'apostrophes
/// typographiques : « Le dragon d'Astrub » avec une apostrophe courbe,
/// « Complètement givré ». Personne ne les tape ainsi. La comparaison se fait
/// donc sur une forme réduite, sans accent, sans ponctuation, en minuscules.
///
/// Fonction pure : elle se vérifie sans réseau ni catalogue réel.
/// </summary>
public static class QuestSearch
{
    /// <summary>
    /// Réduit un texte à ce qui sert à le reconnaître.
    ///
    /// La décomposition Unicode sépare la lettre de son accent, qu'on écarte
    /// ensuite : « é » devient « e ». Tout ce qui n'est ni lettre ni chiffre
    /// devient une espace, et les espaces sont resserrées.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                space = false;
                continue;
            }

            if (!space)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Vrai si la clé contient tous les mots demandés, dans n'importe quel
    /// ordre. « dragon astrub » trouve donc « Le dragon d'Astrub », et
    /// « astrub dragon » aussi.
    /// </summary>
    public static bool Matches(string searchKey, IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        if (terms.Count == 0)
        {
            return true;
        }

        foreach (var term in terms)
        {
            if (!searchKey.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Découpe une saisie en mots comparables.</summary>
    public static IReadOnlyList<string> Terms(string? query)
    {
        var normalized = Normalize(query);

        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Quêtes correspondant à la saisie, les titres qui commencent par la
    /// recherche d'abord.
    ///
    /// Chercher « dofus » doit proposer « Dofus Cawotte » avant « Un nouveau
    /// Dofus ? » : on lit de gauche à droite, et le début d'un titre pèse plus
    /// que son milieu.
    /// </summary>
    public static IReadOnlyList<QuestSummary> Filter(
        IEnumerable<QuestSummary> quests,
        string? query,
        int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(quests);

        var terms = Terms(query);
        var first = terms.Count > 0 ? terms[0] : string.Empty;

        return
        [
            .. quests
                .Where(q => Matches(q.SearchKey, terms))
                .OrderBy(q => q.SearchKey.StartsWith(first, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(q => q.Title, StringComparer.CurrentCulture)
                .Take(limit),
        ];
    }
}
