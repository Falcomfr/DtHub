namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'on remplit à la place du lecteur dans le formulaire de signalement du
/// site, et rien de plus.
///
/// Le formulaire demande où se trouve l'erreur, et c'est la seule chose que
/// l'application sache dire à sa place : elle connaît l'étape qu'on lisait.
/// La description, elle, reste vide : c'est ce que le lecteur a vu, et l'écrire
/// pour lui reviendrait à signaler quelque chose qu'il n'a pas dit.
/// </summary>
public static class PapychaReport
{
    /// <summary>
    /// Longueur du champ « Où se trouve l'erreur ? » sur le site. Au-delà, le
    /// navigateur coupe la saisie et le début seul partirait.
    /// </summary>
    public const int MaxLocationLength = 250;

    /// <summary>
    /// Combien de caractères de l'étape sont cités. Assez pour retrouver le
    /// paragraphe, pas assez pour recopier le guide.
    /// </summary>
    private const int QuotedLength = 140;

    /// <summary>
    /// Où l'on en était, dit dans les termes du site.
    ///
    /// Vide quand il n'y a pas d'étape : une rubrique ou une carte n'en a pas,
    /// et un repère inventé vaudrait moins que le champ laissé libre.
    /// </summary>
    /// <param name="stepIndex">Étape courante, comptée à partir de zéro, ou -1.</param>
    /// <param name="stepCount">Nombre d'étapes du guide.</param>
    /// <param name="stepText">Le texte de l'étape, dont le début est cité.</param>
    public static string Location(int stepIndex, int stepCount, string? stepText)
    {
        if (stepIndex < 0 || stepCount <= 0 || stepIndex >= stepCount)
        {
            return string.Empty;
        }

        var repere = $"Étape {stepIndex + 1} / {stepCount}";
        var cite = Quote(stepText);

        if (cite.Length == 0)
        {
            return repere;
        }

        var complet = $"{repere} : « {cite} »";

        return complet.Length <= MaxLocationLength ? complet : repere;
    }

    /// <summary>
    /// Le début de l'étape, sur une seule ligne et coupé à un mot entier.
    ///
    /// Couper au milieu d'un mot donnerait une citation qu'on ne retrouve pas
    /// dans la page en la cherchant.
    /// </summary>
    private static string Quote(string? stepText)
    {
        var plat = string.Join(' ', (stepText ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (plat.Length <= QuotedLength)
        {
            return plat;
        }

        var coupe = plat[..QuotedLength];
        var espace = coupe.LastIndexOf(' ');

        return (espace > 40 ? coupe[..espace] : coupe).TrimEnd(' ', ',', ';', ':') + "…";
    }
}
