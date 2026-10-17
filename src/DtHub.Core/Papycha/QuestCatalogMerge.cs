namespace DtHub.Core.Papycha;

/// <summary>
/// Applies what the site's public API returns over the catalog DT Hub
/// already has.
///
/// **The API refreshes and adds, it never removes.** On 2026-10-12 the site
/// closed the read the catalog was built from. The public API that replaced
/// it is poorer: no hand-kept sections, achievements locked, fifteen
/// "Dévotion à…" quests, two raids and six lairs missing. Rebuilding the
/// catalog from it alone would take all of that away from every player.
/// Merging by id keeps it, and still brings what the site adds or fixes,
/// without a new release of DT Hub.
/// </summary>
public static class QuestCatalogMerge
{
    /// <summary>
    /// Catch-all section for quests no other section claims.
    /// </summary>
    public const int OtherSectionId = -1;

    /// <summary>
    /// The site's root category. It carries every quest and therefore sorts
    /// nothing: a quest filed there alone would be as good as unfiled.
    /// </summary>
    private const int RootCategory = 7;

    private const string OtherSectionName = "Autres quêtes";

    /// <summary>
    /// Returns a new catalog: <paramref name="baseline"/> with the API's
    /// quests, dungeons and paths applied by id. The baseline is left as it
    /// was.
    /// </summary>
    /// <param name="quests">
    /// The API's quests, with their section ids then their zone ids in
    /// <see cref="QuestSummary.Categories"/>, and their access conditions
    /// then the names of their previous quests in
    /// <see cref="QuestSummary.Prerequisites"/>.
    /// </param>
    /// <param name="paths">The API's paths, side not yet decided.</param>
    /// <param name="siteModified">What the API's status announced.</param>
    public static QuestCatalogDocument Apply(
        QuestCatalogDocument baseline,
        IReadOnlyList<QuestSummary> quests,
        IReadOnlyList<DungeonSummary> dungeons,
        IReadOnlyList<PathSummary> paths,
        DateTimeOffset? siteModified,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(dungeons);
        ArgumentNullException.ThrowIfNull(paths);

        var known = baseline.Sections.ToDictionary(s => s.Id, s => s.Count);
        var mergedQuests = MergeQuests(baseline.Quests, quests, known);
        var sections = Count(baseline.Sections, mergedQuests);
        var mergedDungeons = MergeDungeons(baseline.Dungeons, dungeons);

        return new QuestCatalogDocument
        {
            SchemaVersion = baseline.SchemaVersion,
            IndexedUtc = now,
            SiteModifiedUtc = siteModified,
            Quests = mergedQuests,
            Sections = sections,
            Dungeons = mergedDungeons,
            Paths = MergePaths(baseline.Paths, paths, mergedDungeons),
            SectionOrder = [.. baseline.SectionOrder],
            SuccessOrder = [.. baseline.SuccessOrder],
        };
    }

    private static List<QuestSummary> MergeQuests(
        List<QuestSummary> baseline,
        IReadOnlyList<QuestSummary> api,
        Dictionary<int, int> knownSections)
    {
        var fresh = api.GroupBy(q => q.Id).ToDictionary(g => g.Key, g => g.First());
        List<QuestSummary> merged = new(baseline.Count + api.Count);

        foreach (var quest in baseline)
        {
            merged.Add(fresh.Remove(quest.Id, out var update) ? Refresh(quest, update) : quest);
        }

        foreach (var quest in api.Where(q => fresh.Remove(q.Id)))
        {
            var section = Place(quest, knownSections);

            merged.Add(quest with { SectionId = section, SectionIds = [section] });
        }

        return merged;
    }

    /// <summary>
    /// What the API fills wins, what it leaves empty does not erase. The
    /// prerequisites are the exception: the base's list, read from the
    /// pages, is kept when it has one, since merging both would name the
    /// same quest twice under two spellings.
    /// </summary>
    private static QuestSummary Refresh(QuestSummary known, QuestSummary api)
    {
        var title = api.Title.Length > 0 ? api.Title : known.Title;

        return known with
        {
            Title = title,
            SearchKey = QuestSearch.Normalize(title),
            Url = api.Url.Length > 0 ? api.Url : known.Url,
            Level = api.Level > 0 ? api.Level : known.Level,
            StartPosition = api.StartPosition.Length > 0 ? api.StartPosition : known.StartPosition,
            Prerequisites = known.Prerequisites.Count > 0 ? known.Prerequisites : api.Prerequisites,
        };
    }

    /// <summary>
    /// Section counts after the merge, with the catch-all section added if
    /// a new quest needs it and the base did not have it.
    /// </summary>
    private static List<QuestSection> Count(IReadOnlyList<QuestSection> baseline, List<QuestSummary> quests)
    {
        var counts = quests
            .SelectMany(q => q.SectionIds)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        List<QuestSection> sections = [.. baseline.Select(s => s with { Count = counts.GetValueOrDefault(s.Id) })];

        if (counts.TryGetValue(OtherSectionId, out var others) && sections.All(s => s.Id != OtherSectionId))
        {
            sections.Add(new QuestSection
            {
                Id = OtherSectionId,
                Name = OtherSectionName,
                Count = others,
                SearchKey = QuestSearch.Normalize(OtherSectionName),
            });
        }

        return sections;
    }

    /// <summary>
    /// The one section a new quest is filed under: the least populated of
    /// those the catalog knows among its sections and zones, since it is the
    /// most precise. A quest from Astrub also carries "Amakna", and filing it
    /// under both would count it and show it twice. None known: the
    /// catch-all.
    /// </summary>
    private static int Place(QuestSummary quest, Dictionary<int, int> knownSections) =>
        quest.Categories
            .Where(c => c != RootCategory && knownSections.ContainsKey(c))
            .OrderBy(c => knownSections[c])
            .ThenBy(c => c)
            .Select(c => (int?)c)
            .FirstOrDefault() ?? OtherSectionId;

    private static List<DungeonSummary> MergeDungeons(
        IReadOnlyList<DungeonSummary> baseline,
        IReadOnlyList<DungeonSummary> api)
    {
        var fresh = api.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        List<DungeonSummary> merged = [];

        foreach (var known in baseline)
        {
            if (!fresh.Remove(known.Id, out var update))
            {
                merged.Add(known);
                continue;
            }

            var title = Pick(update.Title, known.Title);

            merged.Add(known with
            {
                Kind = update.Kind,
                Title = title,
                SearchKey = QuestSearch.Normalize(title),
                Url = Pick(update.Url, known.Url),
                Level = update.Level > 0 ? update.Level : known.Level,
                Position = Pick(update.Position, known.Position),
                Person = Pick(update.Person, known.Person),
                Key = Pick(update.Key, known.Key),
                SoulStone = Pick(update.SoulStone, known.SoulStone),
            });
        }

        merged.AddRange(api
            .Where(d => fresh.Remove(d.Id))
            .Select(d => d with { SearchKey = QuestSearch.Normalize(d.Title) }));

        return merged;
    }

    private static List<PathSummary> MergePaths(
        IReadOnlyList<PathSummary> baseline,
        IReadOnlyList<PathSummary> api,
        List<DungeonSummary> dungeons)
    {
        var fresh = api.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        List<PathSummary> merged = [];

        foreach (var known in baseline)
        {
            if (!fresh.Remove(known.Id, out var update))
            {
                merged.Add(known);
                continue;
            }

            var title = Pick(update.Title, known.Title);

            merged.Add(known with
            {
                Title = title,
                SearchKey = QuestSearch.Normalize(title),
                Url = Pick(update.Url, known.Url),
            });
        }

        // The side of a known path stays as it was decided; a new one is
        // decided the same way, from the dungeon names.
        List<string> dungeonTitles = [.. dungeons.Where(d => d.Kind == DungeonKind.Dungeon).Select(d => d.Title)];

        merged.AddRange(api
            .Where(p => fresh.Remove(p.Id))
            .Select(p => p with
            {
                SearchKey = QuestSearch.Normalize(p.Title),
                Side = PathTarget.Of(p.Title, dungeonTitles),
            }));

        return merged;
    }

    private static string Pick(string fresh, string known) => fresh.Length > 0 ? fresh : known;
}
