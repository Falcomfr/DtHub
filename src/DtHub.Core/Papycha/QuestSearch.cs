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

    /// <summary>
    /// Vrai si chaque mot se trouve dans le titre ou dans une rubrique de la
    /// quête. Les deux sont acceptés mot à mot : « frigost givre » retient une
    /// quête nommée « Complètement givré » qui se déroule à Frigost.
    /// </summary>
    public static bool Matches(QuestSummary quest, IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(quest);
        ArgumentNullException.ThrowIfNull(terms);

        foreach (var term in terms)
        {
            if (!quest.SearchKey.Contains(term, StringComparison.Ordinal)
                && !quest.SectionKey.Contains(term, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rang d'un résultat : le titre passe avant la rubrique.
    ///
    /// Chercher « astrub » doit proposer « Le dragon d'Astrub » avant les
    /// cinquante-six quêtes qui s'y déroulent, sans quoi le résultat qu'on
    /// visait se perdrait au milieu.
    /// </summary>
    private static int Rank(QuestSummary quest, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 1;
        }

        if (quest.SearchKey.StartsWith(terms[0], StringComparison.Ordinal))
        {
            return 0;
        }

        return Matches(quest.SearchKey, terms) ? 1 : 2;
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

        return
        [
            .. quests
                .Where(q => Matches(q, terms))
                .OrderBy(q => Rank(q, terms))
                .ThenBy(q => q.Title, StringComparer.CurrentCulture)
                .Take(limit),
        ];
    }

    /// <summary>
    /// Cherche dans les trois natures à la fois, où que l'on soit dans l'arbre.
    ///
    /// Chercher, c'est vouloir aller ailleurs : la rubrique ouverte ne doit pas
    /// borner ce qu'on trouve. Les succès n'étaient jusqu'ici cherchables par
    /// aucun chemin, alors que le catalogue en porte plus de cent.
    /// </summary>
    public static QuestSearchResults Search(
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<QuestSection> sections,
        string? query,
        int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(sections);

        var terms = Terms(query);

        if (terms.Count == 0)
        {
            return QuestSearchResults.Empty;
        }

        List<QuestSection> zones =
        [
            .. sections
                .Where(s => Matches(Normalize(QuestZoneOrder.DisplayName(s.Name)), terms))
                .Take(limit),
        ];

        // Un succès n'existe que par les quêtes qui le portent : on le retient
        // au premier passage, avec l'orthographe de la première rencontrée.
        Dictionary<string, string> successes = new(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            if (quest.SuccessName.Length == 0)
            {
                continue;
            }

            var key = Normalize(quest.SuccessName);

            if (Matches(key, terms))
            {
                successes.TryAdd(key, quest.SuccessName);
            }
        }

        return new QuestSearchResults(
            zones,
            [.. successes.Values.OrderBy(n => n, StringComparer.CurrentCulture).Take(limit)],
            Filter(quests, query, limit));
    }
}
