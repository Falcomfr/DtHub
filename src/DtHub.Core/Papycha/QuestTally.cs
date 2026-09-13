using System.Globalization;

using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// What is kept from a catalog to say, after a reread, what has
/// changed.
///
/// The rule lives here and not in the window: it is a sentence the
/// user reads, with its grammatical agreements and its silences,
/// and none of that was ever tested.
/// </summary>
public readonly record struct QuestTally(int Quests, int Places, int Paths)
{
    /// <summary>A catalog's tally.</summary>
    public static QuestTally Of(QuestCatalogDocument catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new QuestTally(catalog.Quests.Count, catalog.Dungeons.Count, catalog.Paths.Count);
    }

    /// <summary>
    /// What a reread has reported since a tally, in one line.
    ///
    /// Nothing when it changed nothing: the site often reworks its
    /// pages without the catalog gaining or losing anything, and
    /// announcing it every time would be noise. Nothing either
    /// when there was nothing before: announcing "seven hundred
    /// and eighty-two more quests" on the first read would teach
    /// nothing.
    /// </summary>
    public string Since(QuestTally before)
    {
        if (before.Quests == 0 || before == this)
        {
            return string.Empty;
        }

        List<string> parts = [];

        Add(Quests - before.Quests, "WordQuest", "WordQuests");
        Add(Places - before.Places, "WordBattleSite", "WordBattleSites");
        Add(Paths - before.Paths, "WordPath", "WordPaths");

        return parts.Count == 0
            ? string.Empty
            : Strings.Format("GuidesReread", string.Join(", ", parts));

        void Add(int delta, string one, string many)
        {
            if (delta == 0)
            {
                return;
            }

            var count = Math.Abs(delta);

            parts.Add(Strings.Format(
                delta > 0 ? "TallyMore" : "TallyFewer",
                count,
                Strings.Get(count > 1 ? many : one)));
        }
    }
}
