namespace DtHub.Core.Papycha;

/// <summary>
/// Brings together two section titles that designate the same place.
///
/// The site never names something the same way twice: its page says
/// "Quêtes de Frigost" (Frigost Quests) where its category says "Île
/// de Frigost" (Frigost Island). An equality would never occur and a
/// simple inclusion check would fail. So we compare on the words
/// that distinguish them.
///
/// Pure function: it is checked against a saved fragment.
/// </summary>
public static class QuestMenuParser
{
    /// <summary>
    /// Number of distinctive words that two titles have in common,
    /// zero if they do not talk about the same place.
    ///
    /// Used to match a site page to a category: "Quêtes de Frigost"
    /// (Frigost Quests) and "Île de Frigost" (Frigost Island)
    /// designate the same thing, and turning them into two separate
    /// sections would be a duplicate. The count, rather than a
    /// simple yes or no, decides between "Quêtes du Château
    /// d'Amakna" (Amakna Castle Quests) and the categories "Château
    /// d'Amakna" (Amakna Castle) and "Amakna": the first shares two
    /// words, the second only one.
    /// </summary>
    public static int Kinship(string? firstKey, string? secondKey)
    {
        var first = Distinctive(firstKey);
        var second = Distinctive(secondKey);

        return first.Count == 0 || second.Count == 0
            ? 0
            : first.Count(w => second.Contains(w, StringComparer.Ordinal));
    }

    /// <summary>
    /// Rank of a section in the site's ordering, or an end-of-list
    /// rank if it does not mention it. Sections it does not name
    /// come after those it names, never slipping in between.
    /// </summary>
    public static int RankOf(IReadOnlyList<string> order, string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(order);

        for (var i = 0; i < order.Count; i++)
        {
            if (Kinship(order[i], sectionKey) > 0)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Distinctive words that the second title carries in addition
    /// to the first.
    ///
    /// Decides between two sections brought together by the same
    /// word. "Quêtes d'Amakna" (Amakna Quests) shares "amakna" with
    /// the category "Amakna" just as much as with "Château d'Amakna"
    /// (Amakna Castle), and the number of shared words does not
    /// settle it. Whichever adds the fewest is the closest: "Amakna"
    /// adds nothing, "Château d'Amakna" adds one word.
    /// </summary>
    public static int Surplus(string? firstKey, string? secondKey)
    {
        var first = Distinctive(firstKey);
        var second = Distinctive(secondKey);

        return second.Count(w => !first.Contains(w, StringComparer.Ordinal));
    }

    /// <summary>
    /// Words of a section name that truly distinguish it.
    ///
    /// "Île" (Island), "quêtes" (quests), "de" (of) are found
    /// everywhere and would bring anything together with anything.
    /// Only proper nouns and words long enough to be meaningful
    /// remain.
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
}
