namespace DtHub.Core.Papycha;

/// <summary>
/// Map "quest address -> achievement", recorded once from the site
/// and shipped with the application.
///
/// A quest's achievement is only readable in its page's intro block:
/// reading it for all seven hundred and eighty two costs thirteen
/// megabytes. Redoing this every week on every machine would put a
/// load on the site for information that only changes with the
/// game's updates.
///
/// It also carries each quest's prerequisites, which the site only
/// publishes in the HTML of its pages.
///
/// It complements the section pages' subheadings, which the indexing
/// already reads. Measured: subheadings alone link 380 quests, intro
/// blocks 459, their union 505 out of 782. The other 277 have no
/// achievement, which the site's official list confirms, as it
/// announces only 475 in total.
/// </summary>
public interface IQuestSuccessSeed
{
    /// <summary>
    /// Returns the map, indexed on the quest's address without a
    /// trailing slash. An empty map is a normal case: indexing then
    /// falls back to the subheadings alone.
    /// </summary>
    IReadOnlyDictionary<string, QuestSeedEntry> Load();
}

/// <summary>What the map keeps about a quest.</summary>
/// <param name="Success">Name of the achievement it is part of.</param>
/// <param name="ChainStep">
/// Its place in its prerequisite chain, zero if the site does not
/// give it. Used to present an achievement's quests in the order
/// they are played.
/// </param>
/// <param name="PlayOrder">
/// Its place within its achievement, computed at extraction time
/// from the prerequisites the site publishes. The site does not give
/// this order anywhere else: without it, "Les rescapés de Frigost"
/// (The Frigost Survivors) preceded "L'essentiel est dans le Lac
/// gelé" (The Essential Is In The Frozen Lake), which it nevertheless
/// requires.
/// </param>
/// <param name="Prerequisites">
/// What must have been done before this quest, as the site displays
/// it. Recorded on 527 quests, versus 117 to which it gives a level.
/// </param>
public readonly record struct QuestSeedEntry(
    string Success,
    int ChainStep,
    int PlayOrder,
    IReadOnlyList<string> Prerequisites);
