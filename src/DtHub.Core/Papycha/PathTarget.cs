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
