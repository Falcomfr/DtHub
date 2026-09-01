namespace DtHub.Core.Papycha;

/// <summary>
/// Relie les quêtes que les succès ne relient pas, en suivant leurs prérequis.
///
/// La liste d'un succès s'arrête à ses bornes : sa première quête n'a pas de
/// précédente, sa dernière pas de suivante, et une quête sans succès n'a ni
/// l'une ni l'autre. Le site, lui, continue : à Albuera, « Bien débuter » mène
/// à « Une arrivée mouvementée », qui mène à « Le début des problèmes », qui
/// ouvre le succès « Médiation expéditive ». Rien de tout cela ne se lisait.
///
/// Mesuré sur les sept cent quatre-vingt-deux quêtes : cent soixante-huit
/// gagnent une suivante et cent quatre-vingt-dix-sept une précédente. Treize
/// et dix-sept se ramifient, et n'en gagnent aucune : en choisir une au hasard
/// mentirait sur ce que le site publie.
///
/// Table construite une fois : la question se pose à chaque ouverture de quête,
/// et parcourir le catalogue à chaque fois coûterait sept cent quatre-vingt-deux
/// comparaisons pour une réponse.
/// </summary>
public sealed class QuestChainIndex
{
    private readonly Dictionary<string, QuestSummary> _byTitle;
    private readonly Dictionary<string, List<QuestSummary>> _followers;
    private readonly Dictionary<string, List<QuestSummary>> _bySuccess;

    public QuestChainIndex(IEnumerable<QuestSummary> quests)
    {
        ArgumentNullException.ThrowIfNull(quests);

        List<QuestSummary> all = [.. quests];

        // Le rapprochement se fait sur le titre normalisé, comme la recherche :
        // le site écrit les prérequis à la main, avec ses apostrophes et ses
        // accents à lui.
        _byTitle = [];

        foreach (var quest in all)
        {
            _byTitle.TryAdd(QuestSearch.Normalize(quest.Title), quest);
        }

        // Les quêtes de chaque succès, dans l'ordre où l'on y joue : c'est
        // celui du site, porté par le champ de position.
        _bySuccess = all
            .Where(q => q.SuccessName.Length > 0)
            .GroupBy(q => q.SuccessName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(q => q.PlayOrder).ThenBy(q => q.Title, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        _followers = [];

        foreach (var quest in all)
        {
            foreach (var need in quest.Prerequisites)
            {
                var key = QuestSearch.Normalize(need);

                if (key.Length == 0 || !_byTitle.ContainsKey(key))
                {
                    continue;
                }

                if (!_followers.TryGetValue(key, out var list))
                {
                    list = [];
                    _followers[key] = list;
                }

                list.Add(quest);
            }
        }
    }

    /// <summary>
    /// La quête dont celle-ci découle, ou <c>null</c> si ses prérequis n'en
    /// nomment aucune ou en nomment plusieurs.
    /// </summary>
    public QuestSummary? PreviousOf(QuestSummary? quest)
    {
        if (quest is null)
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var need in quest.Prerequisites)
        {
            if (!_byTitle.TryGetValue(QuestSearch.Normalize(need), out var found)
                || Same(found, quest))
            {
                continue;
            }

            if (only is not null && !Same(only, found))
            {
                return null;
            }

            only = found;
        }

        return only;
    }

    /// <summary>
    /// La quête qui découle de celle-ci, ou <c>null</c> si aucune ne la nomme
    /// en prérequis ou si plusieurs le font.
    /// </summary>
    public QuestSummary? NextOf(QuestSummary? quest)
    {
        if (quest is null
            || !_followers.TryGetValue(QuestSearch.Normalize(quest.Title), out var list))
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var candidate in list)
        {
            if (Same(candidate, quest))
            {
                continue;
            }

            if (only is not null && !Same(only, candidate))
            {
                return null;
            }

            only = candidate;
        }

        return only;
    }

    /// <summary>
    /// La quête qui ouvre la série suivante, ou <c>null</c> s'il n'y en a pas
    /// une seule.
    ///
    /// La suite d'un succès ne pend pas toujours à sa dernière quête. Mesuré
    /// sur les cent quinze succès, la première quête de douze d'entre eux a
    /// pour prérequis une quête du milieu du succès précédent : « Médiation
    /// expéditive » se prolonge depuis sa cinquième quête sur six, si bien que
    /// la sixième n'avait aucune suite. On cherche donc dans tout le succès
    /// courant, et non dans la seule quête d'où l'on part.
    ///
    /// Sept succès y gagnent une continuation. Deux en ouvrent plusieurs et
    /// n'en reçoivent aucune : en désigner une au hasard mentirait.
    /// </summary>
    public QuestSummary? NextSeriesOf(QuestSummary? quest)
    {
        if (quest is null || quest.SuccessName.Length == 0)
        {
            return null;
        }

        QuestSummary? only = null;

        foreach (var member in _bySuccess.GetValueOrDefault(quest.SuccessName, []))
        {
            foreach (var candidate in _followers.GetValueOrDefault(
                QuestSearch.Normalize(member.Title), []))
            {
                // Ce qui reste dans le succès n'est pas une autre série, et la
                // liste du succès l'a déjà dit.
                if (string.Equals(candidate.SuccessName, quest.SuccessName, StringComparison.Ordinal)
                    || candidate.SuccessName.Length == 0
                    || !IsFirst(candidate))
                {
                    continue;
                }

                if (only is not null && !Same(only, candidate))
                {
                    return null;
                }

                only = candidate;
            }
        }

        return only;
    }

    /// <summary>Vrai si la quête ouvre son succès, dans l'ordre de jeu.</summary>
    private bool IsFirst(QuestSummary quest)
    {
        var group = _bySuccess.GetValueOrDefault(quest.SuccessName, []);

        return group.Count > 0 && Same(group[0], quest);
    }

    /// <summary>
    /// La quête que porte ce titre, ou <c>null</c> si aucune. Le rapprochement
    /// est celui de la chaîne : le site écrit ses renvois à la main.
    /// </summary>
    public QuestSummary? Find(string? title) =>
        title is null ? null : _byTitle.GetValueOrDefault(QuestSearch.Normalize(title));

    private static bool Same(QuestSummary first, QuestSummary second) =>
        string.Equals(first.Url, second.Url, StringComparison.Ordinal);
}
