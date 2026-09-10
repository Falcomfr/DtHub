namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'on écrit à côté du rang d'une étape, et le plus souvent rien.
///
/// Chaque étape était résumée, aux deux endroits qui la montrent : le bandeau
/// et la liste où on choisit son rang. Un guide de quête y gagnait une ligne de
/// prose tronquée sous le rang, que personne ne lisait puisque la page l'a juste
/// au-dessus, en entier. Le rang seul suffit à s'y rendre.
///
/// Reste ce qui n'est pas de la prose et qui situe vraiment : le départ d'une
/// quête, et les titres de section d'une fiche de lieu.
///
/// Ici plutôt que dans la fenêtre, pour la même raison que
/// <c>StartupPresence</c> : une décision d'affichage se vérifie mieux quand elle
/// ne dépend de rien.
/// </summary>
public static class QuestStepLabel
{
    /// <summary>
    /// Le libellé d'une étape.
    /// </summary>
    /// <param name="step">L'étape et sa nature, telles que le pont les rapporte.</param>
    /// <param name="isDeparture">
    /// Vrai pour la première étape d'une page qui commence par un départ, ce que
    /// le pont dit. Sans cette réserve, le départ se retrouvait annoncé
    /// au-dessus du premier paragraphe des guides qui n'en ont pas.
    /// </param>
    /// <param name="departure">
    /// Le départ composé des métadonnées du site, position et personnage, plus
    /// sûres que sa prose. Vide quand le site ne les renseigne pas.
    /// </param>
    /// <summary>
    /// Vrai quand cette étape est le lancement de la quête et non une consigne.
    ///
    /// Le pont annonce un départ dès que la page porte un bloc de départ **ou**
    /// qu'elle est une fiche de lieu ; un titre de section n'en est jamais un.
    /// C'est la même réserve que celle du libellé, et les deux doivent la
    /// partager, sans quoi une page se retrouverait avec un départ affiché
    /// au-dessus d'un titre.
    /// </summary>
    public static bool IsDeparture(QuestStep step, bool isFirst, bool startsAtDeparture) =>
        isFirst && startsAtDeparture && !step.IsTitle;

    /// <summary>
    /// Le rang d'une étape et le nombre d'étapes, le départ mis à part.
    ///
    /// Le départ n'est pas une étape du parcours : c'est l'endroit où l'on se
    /// rend pour le commencer. Le compter donnait « Étape 1 / 2 » à un guide
    /// qui n'a qu'une consigne, ce qui est le cas de cent quatre-vingt-deux des
    /// sept cent quatre-vingt-deux guides du site, et « Étape 1 / 1 » à trente
    /// autres qui n'en ont aucune.
    /// </summary>
    /// <param name="index">Rang de l'étape parmi celles que le pont a rendues.</param>
    /// <param name="count">Nombre d'étapes rendues, départ compris.</param>
    /// <param name="hasDeparture">Vrai quand la première d'entre elles est le départ.</param>
    /// <returns>
    /// Le rang à montrer et le total, ou <c>null</c> pour le départ, qui se
    /// nomme au lieu de se numéroter.
    /// </returns>
    public static (int Rank, int Total)? Numbering(int index, int count, bool hasDeparture)
    {
        var first = hasDeparture ? 1 : 0;

        if (index < first || index >= count)
        {
            return null;
        }

        return (index - first + 1, count - first);
    }

    public static string For(QuestStep step, bool isDeparture, string? departure)
    {
        // Le titre passe avant le départ, et l'ordre inverse était un défaut.
        //
        // Le pont annonce un départ dès que la page porte un bloc de départ
        // **ou** qu'elle est une fiche de lieu, mais il ne pousse une étape de
        // départ que dans le second cas. Une page qui a les deux, des titres de
        // section et un bloc de départ, voyait donc son premier titre remplacé
        // par la ligne de départ. Relevé sur les sept cent quatre-vingt-deux
        // guides du site : deux sont dans ce cas, « La voie du Wukang / La voie
        // du Wukin » et « L'éternelle moisson », dont le premier titre est
        // « Liste des Monstres ».
        //
        // Un titre est rendu tel quel. Le résumer lui ajoutait une majuscule et
        // un point final qu'il n'avait pas demandés : « Les salles » s'affichait
        // « Les salles. »
        if (step.IsTitle)
        {
            return step.Text;
        }

        return isDeparture ? departure ?? string.Empty : string.Empty;
    }
}
