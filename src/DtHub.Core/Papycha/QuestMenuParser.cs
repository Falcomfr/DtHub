namespace DtHub.Core.Papycha;

/// <summary>
/// Rapproche deux intitulés de rubrique qui désignent le même endroit.
///
/// Le site ne nomme jamais deux fois pareil : sa page dit « Quêtes de Frigost »
/// là où sa catégorie dit « Île de Frigost ». Une égalité ne se produirait
/// jamais et une simple inclusion échouerait. On compare donc sur les mots qui
/// distinguent.
///
/// Fonction pure : elle se vérifie sur un fragment enregistré.
/// </summary>
public static class QuestMenuParser
{
    /// <summary>
    /// Nombre de mots distinctifs que deux intitulés ont en commun, zéro s'ils
    /// ne parlent pas du même endroit.
    ///
    /// Sert à rapprocher une page du site d'une catégorie : « Quêtes de
    /// Frigost » et « Île de Frigost » désignent la même chose, et en faire
    /// deux rubriques distinctes serait un doublon. Le compte, plutôt qu'un
    /// simple oui ou non, départage « Quêtes du Château d'Amakna » entre les
    /// catégories « Château d'Amakna » et « Amakna » : la première partage deux
    /// mots, la seconde un seul.
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
    /// Rang d'une rubrique dans le classement du site, ou un rang de fin s'il
    /// ne la mentionne pas. Les rubriques qu'il ne nomme pas viennent après
    /// celles qu'il nomme, sans jamais se glisser au milieu.
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
    /// Mots d'un nom de rubrique qui la distinguent vraiment.
    ///
    /// « Île », « quêtes », « de » se retrouvent partout et rapprocheraient
    /// n'importe quoi de n'importe quoi. Ne restent que les noms propres et les
    /// mots assez longs pour être parlants.
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
