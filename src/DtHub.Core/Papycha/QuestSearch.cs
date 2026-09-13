using System.Globalization;
using System.Text;

namespace DtHub.Core.Papycha;

/// <summary>
/// Filtering quests and sections as you type.
///
/// The site's titles are in French and full of accents and typographic
/// apostrophes: "Le dragon d'Astrub" (The Astrub Dragon) with a curly
/// apostrophe, "Complètement givré" (Totally Frosted). Nobody types
/// them that way. The comparison is therefore done on a reduced form,
/// without accents, without punctuation, in lowercase.
///
/// Pure function: it is checked without network or a real catalog.
/// </summary>
public static class QuestSearch
{
    /// <summary>
    /// Reduces a text to what serves to recognize it.
    ///
    /// Unicode decomposition separates the letter from its accent,
    /// which is then discarded: "é" becomes "e". Everything that is
    /// neither a letter nor a digit becomes a space, and spaces are
    /// tightened.
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
    /// True if the key contains all the requested words, in any order.
    /// "dragon astrub" therefore finds "Le dragon d'Astrub" (The Astrub
    /// Dragon), and "astrub dragon" too.
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
    /// True if each word is found in the quest's title.
    ///
    /// The title alone, and no longer the section. A quest kept because
    /// its zone carried the word drowned out the ones we were actually
    /// looking for: "frigost" used to return a hundred and seventy
    /// seven, a hundred and seventy three of them through the section
    /// alone, and fifty six of the sixty displayed lines belonged to
    /// it. That intent, "show me all of Frigost", is now served by the
    /// zone group, which did not exist when the section was added here.
    /// </summary>
    public static bool Matches(QuestSummary quest, IReadOnlyList<string> terms)
    {
        ArgumentNullException.ThrowIfNull(quest);

        return Matches(quest.SearchKey, terms);
    }

    /// <summary>
    /// Rank of a result: whatever starts with the searched word goes
    /// first.
    ///
    /// Searching for "dragon" must offer "Dragon Cochon" (Dragon Pig)
    /// before "Le dragon d'Astrub" (The Astrub Dragon), whose title
    /// starts with an article.
    /// </summary>
    private static int Rank(QuestSummary quest, IReadOnlyList<string> terms) =>
        terms.Count > 0 && quest.SearchKey.StartsWith(terms[0], StringComparison.Ordinal)
            ? 0
            : 1;

    /// <summary>Splits an input into comparable words.</summary>
    public static IReadOnlyList<string> Terms(string? query)
    {
        var normalized = Normalize(query);

        return normalized.Length == 0
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Quests matching the input, titles that start with the search
    /// first.
    ///
    /// Searching for "dofus" must offer "Dofus Cawotte" before "Un
    /// nouveau Dofus ?" (A new Dofus?): we read from left to right, and
    /// the start of a title weighs more than its middle.
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
    /// Searches all three kinds at once, wherever we are in the tree.
    ///
    /// Searching means wanting to go elsewhere: the open section must
    /// not bound what we find. Achievements were until now not
    /// searchable through any path, even though the catalog carries
    /// more than a hundred of them.
    /// </summary>
    public static QuestSearchResults Search(
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<QuestSection> sections,
        IReadOnlyList<DungeonSummary> dungeons,
        IReadOnlyList<PathSummary> paths,
        string? query,
        int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(dungeons);
        ArgumentNullException.ThrowIfNull(paths);

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

        // An achievement only exists through the quests that carry it:
        // we keep it on the first pass, with the spelling of the first
        // one encountered.
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
            Filter(quests, query, limit),
            [
                .. dungeons
                    .Where(d => Matches(d.SearchKey, terms))
                    .OrderBy(d => d.Kind)
                    .ThenBy(d => d.Level)
                    .ThenBy(d => d.Title, StringComparer.CurrentCulture)
                    .Take(limit),
            ],
            [
                .. paths
                    .Where(p => Matches(p.SearchKey, terms))
                    .OrderBy(p => p.Title, StringComparer.CurrentCulture)
                    .Take(limit),
            ]);
    }
}
