using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// What is written during indexing.
///
/// **The previous label lied by omission.** It read "Indexing {0}
/// / {1}" and was only set by the quests step. The counter reached
/// "782 / 782" within a few seconds, then stayed there without
/// moving for the rest of it all: sections, dungeons, paths,
/// sorting. Nothing said that the work was continuing, and dungeons
/// alone weigh four megabytes.
///
/// The decision sits here and not in the view model because this
/// is the only layer the tests reach, the test project targeting
/// net10.0 while the application targets net10.0-windows. Same
/// reason as <see cref="QuestStepLabel" />.
/// </summary>
public static class QuestIndexingLabel
{
    /// <summary>
    /// The waiting sentence, never empty.
    ///
    /// The count only appears where it exists. Quests are counted,
    /// the site announcing its total; the other steps are single,
    /// indivisible reads, and a "0 / 0" there would be worse than
    /// nothing.
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
