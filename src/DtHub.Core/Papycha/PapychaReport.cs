namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'on remplit à la place du lecteur dans le formulaire de signalement du
/// site, et rien de plus.
///
/// Le formulaire demande où se trouve l'erreur. L'application y porte la zone,
/// la quête et son succès, c'est-à-dire ce que le site nomme lui-même. Elle y
/// mettait d'abord le rang de l'étape ; ce rang est une numérotation qui n'existe
/// que chez nous, et il ne désignait donc rien pour qui reçoit le signalement.
///
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
    /// Le chevron du fil d'Ariane de la fenêtre, pour que le repère se lise
    /// comme la liste où on l'a trouvé.
    /// </summary>
    private const string Separator = QuestTree.Separator;

    /// <summary>
    /// Où l'on lisait, dans les termes du site : la zone, la quête, et le succès
    /// entre parenthèses.
    ///
    /// Vide quand on ne sait rien : un repère inventé vaudrait moins que le
    /// champ laissé libre.
    /// </summary>
    public static string Location(string? zone, string? quest, string? success = null)
    {
        var title = Flatten(quest);
        var rubrique = Flatten(zone);
        var achievement = Flatten(success);

        if (title.Length == 0)
        {
            return Cut(rubrique);
        }

        // Le succès accompagne la quête, non la zone : c'est d'elle qu'il dit
        // quelque chose. Une page qui n'en a pas ne montre pas de parenthèse
        // vide.
        if (achievement.Length > 0)
        {
            title += $" ({achievement})";
        }

        return Cut(rubrique.Length == 0 ? title : rubrique + Separator + title);
    }

    /// <summary>Le texte sur une seule ligne, sans blancs de bord ni doublons.</summary>
    private static string Flatten(string? text) =>
        string.Join(
            ' ',
            (text ?? string.Empty).Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Ramené à ce que le champ accepte. Le cas ne devrait pas se produire, un
    /// nom de zone et un titre de quête tenant très en deçà ; c'est un garde-fou
    /// contre une saisie tronquée par le navigateur.
    /// </summary>
    private static string Cut(string text) =>
        text.Length <= MaxLocationLength ? text : text[..MaxLocationLength].TrimEnd();
}
