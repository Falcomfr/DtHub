namespace DtHub.Core.Papycha;

/// <summary>
/// Display name and traversal order of the quest zones.
///
/// The site names and organizes its sections for a reader arriving
/// from a search engine: "Quêtes du Port de Madrestam" ("Quests of
/// Port Madrestam") reads well on a page, poorly in a list where
/// every line is already a quest. And its order is that of its own
/// table, which does not follow the game's progression.
///
/// Pure functions: they can be checked without a network or a
/// catalog.
/// </summary>
public static class QuestZoneOrder
{
    /// <summary>
    /// Zones of the world, in the order they are played. The main
    /// quests lead the way, then the geographic progression from
    /// Albuera to Frigost.
    /// </summary>
    private static readonly string[] Progression =
    [
        "Quêtes principales",
        "Albuera",
        "Astrub",
        "Amakna",
        "Château d'Amakna",
        "Port de Madrestam",
        "Bworks",
        "Île des Wabbits",
        "Bonta & Cania",
        "Île d'Otomaï",
        "Île de Pandala",
        "Sufokia",
        "Île d'Orado",
        "Archipel de Vulkania",
        "Îlot Rifique",
        "Île de Frigost",
    ];

    /// <summary>
    /// What does not belong to the progression: alignment paths,
    /// seasonal content, and what no zone claims. Placed after a
    /// subheading so the list does not mix two different things.
    /// </summary>
    private static readonly string[] Extras =
    [
        "Krosmoz",
        "Île de Nowel",
        "Sain Ballotin",
        "Bulles Temporelles",
        "Dédale",
        "[Alignement] Bontarien",
        "[Alignement] Brâkmarien",
        "Quêtes répétables",
        "Autres quêtes",
    ];

    /// <summary>
    /// Subheading that separates the progression from the rest.
    /// </summary>
    public const string ExtrasHeader = "Quêtes supplémentaires";

    /// <summary>
    /// Rank of a zone in the list, or a trailing rank if it is not
    /// known.
    ///
    /// An unknown zone is placed at the end of the progression and
    /// not in the middle: the site can add to it, and a newcomer must
    /// be visible without disturbing what precedes it.
    /// </summary>
    public static int RankOf(string? name)
    {
        var key = QuestSearch.Normalize(DisplayName(name));

        return key.Length > 0 && Ranks.TryGetValue(key, out var rank) ? rank : UnknownRank;
    }

    /// <summary>Rank given to zones the table does not name.</summary>
    public static int UnknownRank => Progression.Length;

    /// <summary>
    /// True if the zone belongs to the block after the subheading.
    /// </summary>
    public static bool IsExtra(string? name) => RankOf(name) > UnknownRank;

    /// <summary>
    /// Name of the section that opens the progression without being
    /// a place.
    /// </summary>
    private const string MainQuests = "Quêtes principales";

    /// <summary>
    /// True if the section designates a place in the world rather
    /// than a family of quests.
    ///
    /// What follows the subheading is not one: alignments, seasons,
    /// repeatables. Nor is "Quêtes principales" ("Main quests"),
    /// even though it opens the progression: it is not a place but a
    /// thread running through all of them.
    ///
    /// The distinction changes nothing about the ordering, only the
    /// icon: a list where everything carries the same mark says no
    /// more than a list with no mark at all.
    /// </summary>
    public static bool IsPlace(string? name) =>
        !IsExtra(name) && RankOf(name) != RankOf(MainQuests);

    /// <summary>
    /// Name as we want it to read in the list.
    ///
    /// The "Quêtes" ("Quests") prefix only disappears if it is
    /// followed by an article: what remains is then a place, "Quêtes
    /// du Port de Madrestam" yielding "Port de Madrestam". Without an
    /// article, the word is part of the name and stays: "Quêtes
    /// principales" ("Main quests") and "Quêtes répétables"
    /// ("Repeatable quests") do not designate a place but a kind of
    /// quest.
    /// </summary>
    public static string DisplayName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var value = name.Trim();

        foreach (var article in Articles)
        {
            var prefix = "Quêtes " + article;

            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var rest = value[prefix.Length..].TrimStart('\'', '’', ' ');

                if (rest.Length > 0)
                {
                    return rest;
                }
            }
        }

        return value;
    }

    /// <summary>
    /// Recognized articles, from longest to shortest: "de la" ("of
    /// the") must be tried before "de" ("of"), otherwise "Quêtes de
    /// la Sain Ballotin" would yield "la Sain Ballotin".
    /// </summary>
    private static readonly string[] Articles =
    [
        "de la ",
        "de l",
        "du ",
        "des ",
        "de ",
        "d",
    ];

    /// <summary>
    /// Declared after <see cref="Articles"/>: static fields
    /// initialize in the order of the file, and building the table
    /// before the articles were known made it fail on a null
    /// reference.
    /// </summary>
    private static readonly Dictionary<string, int> Ranks = BuildRanks();

    private static Dictionary<string, int> BuildRanks()
    {
        Dictionary<string, int> ranks = new(StringComparer.Ordinal);

        for (var i = 0; i < Progression.Length; i++)
        {
            ranks[QuestSearch.Normalize(DisplayName(Progression[i]))] = i;
        }

        // After the rank reserved for unknown zones, so that these
        // stay within the progression rather than falling into the
        // supplement.
        for (var i = 0; i < Extras.Length; i++)
        {
            ranks[QuestSearch.Normalize(DisplayName(Extras[i]))] = Progression.Length + 1 + i;
        }

        return ranks;
    }
}
