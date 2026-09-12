namespace DtHub.Core.Papycha;

/// <summary>Des suites rangées sous un même objectif du site.</summary>
/// <param name="Objective">L'objectif annoncé, ou vide hors de tout groupe.</param>
/// <param name="Links">Les liens qu'il coiffe, dans l'ordre du site.</param>
public sealed record QuestFollowUpGroup(string Objective, IReadOnlyList<QuestLink> Links);

/// <summary>
/// Ce que la quête ouverte débloque, tel que le site le publie en pied
/// d'article sous « Quêtes et jalons suivants ».
///
/// **Cette colonne était lue et jetée.** Le bouton « suivante » de la barre du
/// bas n'en retenait qu'une, et seulement quand elle n'en nommait qu'une : en
/// désigner une parmi plusieurs aurait menti sur l'ordre de lecture d'un
/// succès. La règle reste bonne pour un bouton qui prétend dire « la suite ».
/// Elle ne vaut pas pour une liste, qui peut tout montrer sans rien trancher.
///
/// Relevé sur les 782 guides : 325 colonnes nomment une seule quête suivante,
/// 79 en nomment plusieurs, et 213 ne nomment que le succès qui vient d'être
/// validé. Sans cette liste, les 79 premières n'étaient visibles nulle part et
/// les 213 dernières non plus.
///
/// Les trois natures sont gardées, chacune reconnaissable à son
/// <see cref="QuestLinkKind" /> : une quête, un succès validé, un jalon. Le
/// site les distingue, l'application aussi, et trier à sa place reviendrait à
/// décider pour l'utilisateur ce qui l'intéresse.
/// </summary>
public static class QuestFollowUps
{
    /// <summary>
    /// Les suites à montrer en fin de quête, groupées comme le site les range.
    ///
    /// La quête ouverte est écartée si elle s'y nomme elle-même, et un même
    /// lien n'apparaît qu'une fois : le site répète parfois une quête sous deux
    /// objectifs, et la lire deux fois ferait croire à deux suites distinctes.
    /// </summary>
    /// <param name="chain">Le bloc de progression lu sur la page.</param>
    /// <param name="currentUrl">Adresse de la quête ouverte.</param>
    public static IReadOnlyList<QuestFollowUpGroup> Of(QuestChain? chain, string? currentUrl)
    {
        if (chain is null || chain.Next.Count == 0)
        {
            return [];
        }

        var self = Key(currentUrl);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        List<QuestFollowUpGroup> groups = [];
        List<QuestLink> pending = [];
        var objective = string.Empty;

        foreach (var link in chain.Next)
        {
            var key = Key(link.Url);

            if (key.Length == 0 || (self.Length > 0 && string.Equals(key, self, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!seen.Add(key))
            {
                continue;
            }

            var under = link.Objective?.Trim() ?? string.Empty;

            // L'ordre du site est conservé, et un groupe se referme dès que
            // l'objectif change : les regrouper par nom réordonnerait la
            // colonne, et cet ordre est justement ce qu'elle publie.
            if (!string.Equals(under, objective, StringComparison.Ordinal) && pending.Count > 0)
            {
                groups.Add(new QuestFollowUpGroup(objective, pending));
                pending = [];
            }

            objective = under;
            pending.Add(link);
        }

        if (pending.Count > 0)
        {
            groups.Add(new QuestFollowUpGroup(objective, pending));
        }

        return groups;
    }

    /// <summary>Le nombre de liens de tous les groupes.</summary>
    public static int Count(IReadOnlyList<QuestFollowUpGroup>? groups) =>
        groups?.Sum(g => g.Links.Count) ?? 0;

    /// <summary>
    /// L'adresse ramenée à ce qui l'identifie : le site sert la même page avec
    /// ou sans barre oblique finale, et avec des ancres.
    /// </summary>
    private static string Key(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var text = url.Trim();
        var anchor = text.IndexOf('#', StringComparison.Ordinal);

        if (anchor >= 0)
        {
            text = text[..anchor];
        }

        return text.TrimEnd('/').ToLowerInvariant();
    }
}
