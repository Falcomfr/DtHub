namespace DtHub.Core.Papycha;

/// <summary>
/// L'ordre dans lequel on joue les quêtes d'un succès.
///
/// Les prérequis d'abord : une quête qui en réclame une autre du même succès
/// passe après elle, quoi que dise le reste. C'est la seule règle que le
/// lecteur puisse vérifier lui-même, et la seule qu'il remarque quand elle est
/// enfreinte.
///
/// À défaut de prérequis, l'ordre d'avant, qui départage aussi les quêtes
/// qu'aucun prérequis ne sépare : la place dans le succès, calculée à
/// l'indexation ; le rang de chaîne ensuite, pour les quêtes que la carte ne
/// connaît pas ; le titre en dernier, pour que l'ordre soit total et toujours
/// le même.
///
/// Un zéro ne dit pas « premier » mais « on ne sait pas », et passe donc en
/// queue. La chaîne de quêtes le triait pourtant à l'endroit, si bien qu'une
/// quête de rang inconnu passait pour la première de son succès et se donnait
/// pour la suite de la série précédente.
///
/// La place calculée à l'indexation vient d'un tri topologique fait en Python,
/// et treize quêtes du catalogue la contredisent : « L'île Céleste » y porte le
/// rang 2 quand « Le voyage vers Incarnam », qu'elle exige, porte le rang 3.
/// Refaire le tri ici, sur les prérequis que l'application lit de toute façon,
/// rend la liste vraie sans dépendre de ce que la carte a retenu.
/// </summary>
public static class QuestPlayOrder
{
    /// <summary>Les quêtes rangées dans l'ordre où l'on y joue.</summary>
    public static IReadOnlyList<QuestSummary> Sorted(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<QuestSummary> all = [.. quests];

        if (all.Count < 2)
        {
            return all;
        }

        // Le départage : l'ordre d'avant, employé tel quel quand aucun
        // prérequis ne sépare deux quêtes, et pour trancher une boucle.
        var keys = new (int Play, int Chain, string Title, int Index)[all.Count];

        Dictionary<string, int> byTitle = new(StringComparer.Ordinal);

        for (var i = 0; i < all.Count; i++)
        {
            keys[i] = (
                all[i].PlayOrder == 0 ? int.MaxValue : all[i].PlayOrder,
                all[i].ChainStep == 0 ? int.MaxValue : all[i].ChainStep,
                all[i].Title,
                i);

            byTitle.TryAdd(QuestSearch.Normalize(all[i].Title), i);
        }

        var after = new List<HashSet<int>>(all.Count);
        var waiting = new int[all.Count];

        for (var i = 0; i < all.Count; i++)
        {
            after.Add([]);
        }

        for (var i = 0; i < all.Count; i++)
        {
            foreach (var need in all[i].Prerequisites)
            {
                var named = PrerequisiteLabel.Of(need);

                if (named.IsSuccess)
                {
                    // Une quête qui réclame son propre succès le réclame en
                    // entier : elle passe donc après tout le reste du bloc.
                    // « En route pour Plantala » est dans ce cas, seule sur les
                    // sept cent quatre-vingt-deux quêtes.
                    if (string.Equals(named.Name, all[i].SuccessName, StringComparison.OrdinalIgnoreCase))
                    {
                        for (var other = 0; other < all.Count; other++)
                        {
                            if (other != i && after[other].Add(i))
                            {
                                waiting[i]++;
                            }
                        }
                    }

                    // Tout autre succès désigne un autre bloc : il ne range
                    // rien à l'intérieur de celui-ci.
                    continue;
                }

                if (!byTitle.TryGetValue(QuestSearch.Normalize(named.Name), out var from)
                    || from == i
                    || !after[from].Add(i))
                {
                    continue;
                }

                waiting[i]++;
            }
        }

        return Sort(all, keys, after, waiting);
    }

    /// <summary>
    /// Le tri topologique, avec repli sur boucle : on prend alors la plus
    /// petite quête restante au sens du départage et l'on continue, pour que
    /// l'ordre reste total plutôt que tronqué.
    /// </summary>
    private static List<QuestSummary> Sort(
        List<QuestSummary> all,
        (int Play, int Chain, string Title, int Index)[] keys,
        List<HashSet<int>> after,
        int[] waiting)
    {
        // Le titre se compare par la culture, comme partout où le site est lu :
        // ses titres sont français et pleins d'accents. La comparaison de
        // n-uplets s'en charge, et c'est celle qu'emploie déjà le rangement des
        // zones.
        var order = Comparer<int>.Create((first, second) => keys[first].CompareTo(keys[second]));

        SortedSet<int> left = new(order);
        SortedSet<int> ready = new(order);

        for (var i = 0; i < all.Count; i++)
        {
            left.Add(i);

            if (waiting[i] == 0)
            {
                ready.Add(i);
            }
        }

        List<QuestSummary> plan = new(all.Count);

        while (left.Count > 0)
        {
            var at = ready.Count > 0 ? ready.Min : left.Min;

            _ = ready.Remove(at);
            _ = left.Remove(at);

            plan.Add(all[at]);

            foreach (var next in after[at])
            {
                if (left.Contains(next) && --waiting[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        return plan;
    }
}
