namespace DtHub.Core.Papycha;

/// <summary>
/// The catalog shipped with the application: the base a player starts
/// from when there is no usable cache, before the site's API is merged
/// over it.
///
/// It exists because the site closed the read the catalog was built
/// from. The public API that replaced it lacks the hand-kept sections,
/// the achievements and some quests: without this base, a new player
/// would get a poorer list than everyone else, or none at all when the
/// site does not answer.
/// </summary>
public interface IQuestCatalogSnapshot
{
    /// <summary>
    /// A new document on each call, which the caller may change. Empty if
    /// the snapshot cannot be read.
    /// </summary>
    QuestCatalogDocument Load();
}
