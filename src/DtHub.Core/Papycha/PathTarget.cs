namespace DtHub.Core.Papycha;

/// <summary>
/// Décide de quel côté un chemin se range.
///
/// Le site ne le dit pas : ses catégories ne donnent que la zone, et les liens
/// de ses pages sont presque toujours absents. Le titre, lui, suffit - à deux
/// conditions.
///
/// Un chemin va aux donjons s'il écrit le mot « donjon », ou s'il partage au
/// moins deux mots distinctifs avec un donjon du catalogue. Un seul mot commun
/// ne suffit pas, et c'est ce qui écarte les faux : « Zaap du village de la
/// canopée » ne partage que « canopée » avec « Canopée du Kimbo », « Ile de
/// Sakaï » que « Sakaï » avec « Mine de Sakaï ».
///
/// Vérifié sur les vingt et un chemins publiés : six aux donjons, quinze aux
/// quêtes.
///
/// Fonction pure : elle se vérifie sur des titres, sans réseau.
/// </summary>
public static class PathTarget
{
    /// <summary>Combien de mots distinctifs il faut partager pour conclure.</summary>
    private const int Needed = 2;

    /// <summary>
    /// Mots trop courants pour distinguer quoi que ce soit. « Donjon » en fait
    /// partie ici, bien qu'il décide à lui seul par ailleurs : sans cela,
    /// « donjon du Koulosse » et « Donjon des Dragoeufs » se ressembleraient.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "chemin", "guide", "aller", "sur", "du", "de", "la", "le", "les", "des",
        "au", "aux", "en", "pour", "et", "ile", "iles", "ilot", "ilots", "tuto",
        "donjon", "donjons", "vers", "dans", "un", "une", "avec", "son", "sa",
    };

    /// <summary>Le côté d'un chemin, connaissant les noms des donjons.</summary>
    public static PathSide Of(string? title, IEnumerable<string> dungeonTitles)
    {
        ArgumentNullException.ThrowIfNull(dungeonTitles);

        var words = Words(title);

        if (words.Count == 0)
        {
            return PathSide.Quests;
        }

        // Le mot « donjon » écrit en toutes lettres tranche à lui seul : le
        // chemin dit alors où il mène. Le pluriel compte autant.
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
    /// Les mots d'un titre, normalisés comme la recherche et débarrassés de ce
    /// qui ne distingue rien.
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
