namespace DtHub.Core.Papycha;

/// <summary>
/// Decides which side a path belongs to.
///
/// The site does not say: its categories only give the zone, and
/// its page links are almost always missing. The title alone is
/// enough, under two conditions.
///
/// A path goes to dungeons if it spells out the word "donjon", or
/// if it shares at least two distinctive words with a dungeon from
/// the catalog. A single shared word is not enough, and that is
/// what rules out false positives: "Zaap du village de la canopée"
/// only shares "canopée" with "Canopée du Kimbo", "Ile de Sakaï"
/// only shares "Sakaï" with "Mine de Sakaï".
///
/// Checked against the twenty-one published paths: six to dungeons,
/// fifteen to quests.
///
/// Pure function: it only checks titles, no network.
/// </summary>
public static class PathTarget
{
    /// <summary>
    /// How many distinctive words must be shared to decide.
    /// </summary>
    private const int Needed = 2;

    /// <summary>
    /// Words too common to distinguish anything. "Donjon" is
    /// included here, even though it decides on its own elsewhere:
    /// without this, "donjon du Koulosse" and "Donjon des
    /// Dragoeufs" would look alike.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "chemin", "guide", "aller", "sur", "du", "de", "la", "le", "les", "des",
        "au", "aux", "en", "pour", "et", "ile", "iles", "ilot", "ilots", "tuto",
        "donjon", "donjons", "vers", "dans", "un", "une", "avec", "son", "sa",
    };

    /// <summary>The side of a path, given the dungeon names.</summary>
    public static PathSide Of(string? title, IEnumerable<string> dungeonTitles)
    {
        ArgumentNullException.ThrowIfNull(dungeonTitles);

        var words = Words(title);

        if (words.Count == 0)
        {
            return PathSide.Quests;
        }

        // The word "donjon" spelled out on its own settles it: the
        // path then says where it leads. The plural counts just as
        // much.
        var written = Words(title, keepCommon: true);

        if (written.Contains("donjon") || written.Contains("donjons"))
        {
            return PathSide.Dungeons;
        }

        foreach (var dungeon in dungeonTitles)
        {
            var shared = 0;

            foreach (var word in Words(dungeon))
            {
                if (words.Contains(word) && ++shared >= Needed)
                {
                    return PathSide.Dungeons;
                }
            }
        }

        return PathSide.Quests;
    }

    /// <summary>
    /// Paths whose title does not name their zone, placed by hand with the
    /// user on 2026-10-17: a zaap, an island of the zone, a spell. Path id,
    /// section id.
    /// </summary>
    private static readonly Dictionary<int, int> ByHand = new()
    {
        [3628] = 131,   // Zaap du village de la canopée et Zoth: Île d'Otomaï
        [13235] = 135,  // Ile de Sakaï: Île de Frigost
        [5817] = 135,   // Zaap du Village Enseveli: Île de Frigost
        [1618] = 52,    // Wabbit GM: Île des Wabbits
        [6210] = 52,    // Obtenir le sort Cawotte: Île des Wabbits
    };

    /// <summary>
    /// The dungeon a path leads to: the one sharing the most distinctive
    /// words with its title. <c>null</c> when none shares one, or when two
    /// share as many: "Wabbit" alone does not tell the castle from the
    /// burrow.
    /// </summary>
    public static DungeonSummary? DungeonOf(string? title, IEnumerable<DungeonSummary> dungeons)
    {
        ArgumentNullException.ThrowIfNull(dungeons);

        var words = Words(title);

        List<(DungeonSummary Dungeon, int Shared)> ranked =
        [
            .. dungeons
                .Select(d => (d, Words(d.Title).Count(words.Contains)))
                .Where(pair => pair.Item2 > 0)
                .OrderByDescending(pair => pair.Item2),
        ];

        return ranked.Count == 0 || (ranked.Count > 1 && ranked[1].Shared == ranked[0].Shared)
            ? null
            : ranked[0].Dungeon;
    }

    /// <summary>
    /// The zone a quest path belongs to, or zero: the section whose
    /// distinctive words its title all carries, "Otomaï" for "Restat sur
    /// Otomaï", the most specific when several do. A path the title does not
    /// place may be placed by hand.
    /// </summary>
    public static int ZoneOf(PathSummary path, IReadOnlyList<QuestSection> sections)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(sections);

        if (ByHand.TryGetValue(path.Id, out var zone))
        {
            return sections.Any(s => s.Id == zone) ? zone : 0;
        }

        var words = Words(path.Title);

        return sections
            .Select(s => (s.Id, Needed: Words(QuestZoneOrder.DisplayName(s.Name))))
            .Where(z => z.Needed.Count > 0 && z.Needed.IsSubsetOf(words))
            .OrderByDescending(z => z.Needed.Count)
            .ThenBy(z => z.Id)
            .Select(z => z.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// The words of a title, normalized the same way as search and
    /// stripped of anything that does not distinguish.
    /// </summary>
    private static HashSet<string> Words(string? title, bool keepCommon = false)
    {
        HashSet<string> words = new(StringComparer.Ordinal);

        foreach (var word in QuestSearch.Normalize(title ?? string.Empty).Split(' '))
        {
            if (word.Length > 2 && (keepCommon || !Common.Contains(word)))
            {
                words.Add(word);
            }
        }

        return words;
    }
}
