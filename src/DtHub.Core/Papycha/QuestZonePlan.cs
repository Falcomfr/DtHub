namespace DtHub.Core.Papycha;

/// <summary>
/// Un bloc de la liste d'une zone : un succès et ses quêtes, ou une quête que
/// nul succès ne réclame.
/// </summary>
/// <param name="SuccessName">Le nom du succès, vide pour une quête seule.</param>
/// <param name="Quests">Ses quêtes, dans l'ordre où l'on y joue.</param>
public sealed record QuestZoneBlock(string SuccessName, IReadOnlyList<QuestSummary> Quests)
{
    /// <summary>Vrai quand le bloc est un succès et non une quête isolée.</summary>
    public bool IsSuccess => SuccessName.Length > 0;
}

/// <summary>
/// Range les quêtes d'une zone dans l'ordre où l'on y joue.
///
/// Les quêtes qu'aucun succès ne réclame étaient rejetées en fin de liste, par
/// ordre alphabétique. Or beaucoup d'entre elles ouvrent un succès ou le
/// prolongent : « Une arrivée mouvementée » précède « Médiation expéditive » à
/// Albuera, « En route pour Aerdala » précède « Là où souffle le vent » à
/// Pandala. Les voir en bas de liste, coupées de ce qu'elles servent, ne disait
/// rien de la progression. Et l'ordre alphabétique lisait les quatre-vingts
/// quêtes d'alignement « bontarien 1, 10, 11, 12, 2, 20 ».
///
/// La zone se range donc par ses prérequis : sur les sept cent quatre-vingt-deux
/// quêtes, cinq cent quatorze prérequis sur sept cent vingt-neuf désignent une
/// quête du catalogue, et cent quatre-vingt-douze des deux cent quatre-vingt-
/// quatre quêtes seules sont prises dans une chaîne.
///
/// Un succès est un bloc insécable : ses quêtes se suivent, et c'est lui qu'on
/// range parmi les autres. À défaut de prérequis, l'ordre est celui d'avant,
/// si bien qu'une zone dont aucun prérequis ne se reconnaît ne bouge pas.
/// </summary>
public static class QuestZonePlan
{
    /// <summary>
    /// Les blocs d'une zone, dans l'ordre où l'on y joue.
    ///
    /// Les prérequis qui désignent une quête absente de la liste sont ignorés :
    /// ils ne peuvent rien y ranger. C'est le cas de ceux qui pointent une autre
    /// zone.
    /// </summary>
    /// <param name="quests">Les quêtes de la zone.</param>
    /// <param name="successOrder">L'ordre des succès sur le site.</param>
    public static IReadOnlyList<QuestZoneBlock> Of(
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<string> successOrder)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(successOrder);

        var blocks = Blocks(quests, out var blockOfUrl);

        if (blocks.Count == 0)
        {
            return [];
        }

        var waiting = new int[blocks.Count];
        var after = Edges(quests, blocks, blockOfUrl, waiting);
        var keys = Keys(blocks, successOrder, after);

        return Sort(blocks, keys, after, waiting, Lonely(blocks, after, waiting));
    }

    /// <summary>
    /// Un bloc par succès, un bloc d'une quête pour chaque quête seule, et de
    /// quoi retrouver le bloc d'une quête par son adresse.
    /// </summary>
    private static List<(string Success, List<QuestSummary> Quests)> Blocks(
        IReadOnlyList<QuestSummary> quests,
        out Dictionary<string, int> blockOfUrl)
    {
        List<(string Success, List<QuestSummary> Quests)> blocks = [];
        Dictionary<string, int> at = new(StringComparer.Ordinal);

        blockOfUrl = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            var key = quest.SuccessName.Length > 0
                ? "s:" + quest.SuccessName
                : "q:" + quest.Url;

            if (!at.TryGetValue(key, out var index))
            {
                index = blocks.Count;
                at[key] = index;
                blocks.Add((quest.SuccessName, []));
            }

            blocks[index].Quests.Add(quest);
            blockOfUrl[quest.Url] = index;
        }

        return blocks;
    }

    /// <summary>
    /// Ce qui départage deux blocs qu'aucun prérequis ne sépare : c'est l'ordre
    /// d'avant.
    ///
    /// Le rang du succès sur le site d'abord, son nom ensuite. Une quête seule
    /// passe après tous les succès de même rang, comme le faisait le bloc
    /// « Hors succès » qui les rassemblait en fin de liste. Le rang du bloc
    /// clôt le départage, pour que l'ordre soit total.
    ///
    /// **Une quête seule qui découle d'un succès prend son rang**, et se range
    /// donc juste derrière lui plutôt qu'après tous les autres. Sans cela, « La
    /// découverte d'un vaste monde », dont le seul prérequis est le succès
    /// « Devenir une légende », se retrouvait trente rangs plus bas, derrière
    /// des succès qui n'ont rien à voir : le tri la plaçait bien après ce
    /// qu'elle exige, mais si loin que la progression ne se lisait plus.
    /// </summary>
    private static (int Rank, int Kind, int Depth, string Label, int Index)[] Keys(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        IReadOnlyList<string> successOrder,
        List<HashSet<int>> after)
    {
        Dictionary<string, int> rank = new(StringComparer.Ordinal);

        for (var i = 0; i < successOrder.Count; i++)
        {
            rank.TryAdd(successOrder[i], i);
        }

        var keys = new (int Rank, int Kind, int Depth, string Label, int Index)[blocks.Count];

        for (var i = 0; i < blocks.Count; i++)
        {
            var (success, members) = blocks[i];

            keys[i] = success.Length > 0
                ? (rank.GetValueOrDefault(success, int.MaxValue), 0, 0, success, i)
                : (int.MaxValue, 1, 0, members[0].Title, i);
        }

        Inherit(blocks, keys, after);

        return keys;
    }

    /// <summary>
    /// Fait descendre le rang d'un succès sur les quêtes seules qui en
    /// découlent, de proche en proche.
    ///
    /// Le parcours part des succès dans leur ordre, si bien qu'une quête seule
    /// que deux succès pourraient réclamer prend le rang du premier. La
    /// profondeur retient la distance parcourue, pour qu'une suite de quêtes
    /// seules se lise dans l'ordre où on l'enchaîne et non par ordre
    /// alphabétique.
    ///
    /// Ce qu'aucun succès n'atteint garde son rang maximal, et part donc en fin
    /// de liste comme avant : le site ne dit rien de sa place.
    /// </summary>
    private static void Inherit(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        (int Rank, int Kind, int Depth, string Label, int Index)[] keys,
        List<HashSet<int>> after)
    {
        List<int> departs = [.. Enumerable
            .Range(0, blocks.Count)
            .Where(i => blocks[i].Success.Length > 0)
            .OrderBy(i => keys[i].Rank)
            .ThenBy(i => keys[i].Label, StringComparer.Ordinal)];

        HashSet<int> vus = [.. departs];
        Queue<int> file = new(departs);

        while (file.Count > 0)
        {
            var at = file.Dequeue();

            foreach (var next in after[at])
            {
                if (blocks[next].Success.Length > 0 || !vus.Add(next))
                {
                    continue;
                }

                keys[next] = keys[next] with
                {
                    Rank = keys[at].Rank,
                    Depth = keys[at].Depth + 1,
                };

                file.Enqueue(next);
            }
        }
    }

    /// <summary>
    /// Les arcs entre blocs : un prérequis reconnu place son bloc avant celui
    /// de la quête qui le réclame.
    ///
    /// Le rapprochement se fait sur le nom normalisé, comme la recherche et
    /// comme la chaîne de quêtes : le site écrit ses prérequis à la main. Il
    /// passe par <see cref="PrerequisiteLabel"/>, car un prérequis nomme
    /// tantôt une quête, tantôt le jalon qu'elle pose, tantôt un succès entier.
    /// </summary>
    private static List<HashSet<int>> Edges(
        IReadOnlyList<QuestSummary> quests,
        List<(string Success, List<QuestSummary> Quests)> blocks,
        Dictionary<string, int> blockOfUrl,
        int[] waiting)
    {
        Dictionary<string, QuestSummary> byTitle = new(StringComparer.Ordinal);

        foreach (var quest in quests)
        {
            byTitle.TryAdd(QuestSearch.Normalize(quest.Title), quest);
        }

        // Un prérequis nomme parfois un succès entier plutôt qu'une quête,
        // « Succès Un nouveau départ réalisé ». Il désigne alors le bloc, qui
        // est justement ce qu'on range ici.
        Dictionary<string, int> blockOfSuccess = new(StringComparer.Ordinal);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Success.Length > 0)
            {
                blockOfSuccess.TryAdd(QuestSearch.Normalize(blocks[i].Success), i);
            }
        }

        List<HashSet<int>> after = [];

        for (var i = 0; i < blocks.Count; i++)
        {
            after.Add([]);
        }

        foreach (var quest in quests)
        {
            var to = blockOfUrl[quest.Url];

            foreach (var need in quest.Prerequisites)
            {
                var named = PrerequisiteLabel.Of(need);
                var key = QuestSearch.Normalize(named.Name);

                int from;

                if (named.IsSuccess)
                {
                    if (!blockOfSuccess.TryGetValue(key, out from))
                    {
                        continue;
                    }
                }
                else if (byTitle.TryGetValue(key, out var found))
                {
                    from = blockOfUrl[found.Url];
                }
                else
                {
                    continue;
                }

                if (from == to || !after[from].Add(to))
                {
                    continue;
                }

                waiting[to]++;
            }
        }

        return after;
    }

    /// <summary>
    /// Les quêtes seules que rien ne lie : ni prérequis reconnu, ni quête qui
    /// les réclame.
    ///
    /// Le site ne dit rien de leur place, et les laisser dans le tri les y
    /// mettait au hasard : au Château d'Amakna, « On recherche Ali Grothor » se
    /// glissait entre deux succès parce qu'elle était la seule chose que le tri
    /// pouvait sortir pendant qu'une boucle bloquait le second. Elles vont donc
    /// en fin de liste, où elles étaient avant ce rangement. Cent deux quêtes
    /// seules sur trois cent une sont dans ce cas.
    ///
    /// Un succès sans lien, lui, garde son rang : celui-là, le site le donne.
    /// </summary>
    private static HashSet<int> Lonely(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        List<HashSet<int>> after,
        int[] waiting)
    {
        HashSet<int> lonely = [];

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Success.Length == 0 && waiting[i] == 0 && after[i].Count == 0)
            {
                lonely.Add(i);
            }
        }

        return lonely;
    }

    /// <summary>
    /// Le tri topologique, avec repli sur boucle.
    ///
    /// Traiter un succès comme un bloc insécable crée un cycle dès que deux
    /// succès se réclament l'un l'autre par des quêtes différentes. Plutôt que
    /// de rendre une liste tronquée, on prend alors le plus petit bloc restant
    /// au sens du départage et l'on continue : l'ordre reste total, et il
    /// retombe sur celui d'avant là où les prérequis se contredisent. Six rangs
    /// seulement sont ainsi forcés sur tout le catalogue.
    /// </summary>
    private static List<QuestZoneBlock> Sort(
        List<(string Success, List<QuestSummary> Quests)> blocks,
        (int Rank, int Kind, int Depth, string Label, int Index)[] keys,
        List<HashSet<int>> after,
        int[] waiting,
        HashSet<int> lonely)
    {
        var order = Comparer<int>.Create((first, second) => keys[first].CompareTo(keys[second]));
        SortedSet<int> left = new(order);
        SortedSet<int> ready = new(order);

        for (var i = 0; i < blocks.Count; i++)
        {
            if (lonely.Contains(i))
            {
                continue;
            }

            left.Add(i);

            if (waiting[i] == 0)
            {
                ready.Add(i);
            }
        }

        List<QuestZoneBlock> plan = new(blocks.Count);

        while (left.Count > 0)
        {
            var at = ready.Count > 0 ? ready.Min : left.Min;

            _ = ready.Remove(at);
            _ = left.Remove(at);

            var (success, members) = blocks[at];

            plan.Add(new QuestZoneBlock(
                success,
                success.Length > 0 ? QuestPlayOrder.Sorted(members) : members));

            foreach (var next in after[at])
            {
                if (left.Contains(next) && --waiting[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        foreach (var at in lonely.Order(order))
        {
            plan.Add(new QuestZoneBlock(string.Empty, blocks[at].Quests));
        }

        return plan;
    }
}
