using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'on écrit pendant une indexation.
///
/// **Le libellé d'avant mentait par omission.** Il valait « Indexation {0} /
/// {1} » et n'était posé que par l'étape des quêtes. Le compteur atteignait
/// « 782 / 782 » en quelques secondes, puis restait là sans bouger pendant tout
/// le reste : rubriques, donjons, chemins, rangement. Rien ne disait que le
/// travail continuait, et les donjons pèsent à eux seuls quatre mégaoctets.
///
/// La décision est ici et non dans la vue-modèle parce que c'est la seule
/// couche que les épreuves atteignent, le projet d'épreuves visant net10.0
/// quand l'application vise net10.0-windows. Même raison que
/// <see cref="QuestStepLabel" />.
/// </summary>
public static class QuestIndexingLabel
{
    /// <summary>
    /// La phrase d'attente, jamais vide.
    ///
    /// Le décompte n'apparaît que là où il existe. Les quêtes se comptent, le
    /// site annonçant son total ; les autres étapes sont des lectures d'un seul
    /// tenant, et un « 0 / 0 » y serait pire que rien.
    /// </summary>
    public static string For(QuestIndexingProgress progress) => progress.Phase switch
    {
        QuestIndexingPhase.Quests when progress.Total > 0 =>
            Strings.Format("IndexingQuests", progress.Loaded, progress.Total),
        QuestIndexingPhase.Quests => Strings.Get("Indexing"),
        QuestIndexingPhase.Sections => Strings.Get("IndexingSections"),
        QuestIndexingPhase.Dungeons => Strings.Get("IndexingDungeons"),
        QuestIndexingPhase.Paths => Strings.Get("IndexingPaths"),
        QuestIndexingPhase.Arranging => Strings.Get("IndexingArranging"),
        _ => Strings.Get("Indexing"),
    };
}
