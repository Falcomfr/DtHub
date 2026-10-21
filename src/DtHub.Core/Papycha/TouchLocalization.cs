namespace DtHub.Core.Papycha;

/// <summary>
/// A view of the catalog in the game's own words, so that the guides window
/// says what the player reads in DOFUS Touch.
///
/// Never mutates the source: the catalog on disk stays Papycha's French one,
/// and the view is rebuilt whenever the language or the names change.
///
/// Rules:
/// - with <c>translateNames</c>, a paired quest's title becomes the game
///   names joined by " / " (distinct names only: some pages pair with many
///   game quests sharing one name), and its search key covers the shown and
///   the French title, so both find it;
/// - each prerequisite that names a French quest title takes that quest's
///   shown title, so the chain still resolves;
/// - the level is the first game quest's, when it is precise (min = max > 0);
/// - the series name of a quest and the series order take the game
///   achievement name, in every language;
/// - a quest the game links to an achievement while Papycha files it under
///   none gets that achievement and a step after the highest of its series,
///   in Papycha-id order, and the name joins the order if missing;
/// - with <c>translateNames</c>, a paired dungeon takes the joined game
///   dungeon names (or its boss name), and a non-empty key the key item name;
/// - what the game does not name stays as Papycha wrote it. Sections and
///   paths are untouched.
/// </summary>
public static class TouchLocalization
{
    private const string Separator = " / ";

    public static QuestCatalogDocument Apply(
        QuestCatalogDocument source,
        TouchIdTable table,
        TouchLanguageNames names,
        bool translateNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(names);

        Dictionary<string, string> shownByFrench = [];
        List<QuestSummary> quests = [];

        foreach (var quest in source.Quests)
        {
            var shown = quest.Title;
            var level = quest.Level;

            if (table.Quests.TryGetValue(quest.Id, out var ids))
            {
                var found = ids.Where(names.Quests.ContainsKey).ToArray();

                if (found.Length > 0)
                {
                    var first = names.Quests[found[0]];

                    if (first.LevelMin == first.LevelMax && first.LevelMin > 0)
                    {
                        level = first.LevelMin;
                    }

                    if (translateNames)
                    {
                        shown = Join(found.Select(i => names.Quests[i].Name));
                    }
                }
            }

            shownByFrench.TryAdd(QuestSearch.Normalize(quest.Title), shown);

            quests.Add(quest with
            {
                Title = shown,
                Level = level,
                SearchKey = shown == quest.Title
                    ? quest.SearchKey
                    : QuestSearch.Normalize(shown + " " + quest.Title),
                SuccessName = Series(quest.SuccessName, table, names),
            });
        }

        Dictionary<string, string> seriesByFrench = [];

        foreach (var (french, id) in table.Series)
        {
            if (names.Achievements.TryGetValue(id, out var shown))
            {
                seriesByFrench.TryAdd(QuestSearch.Normalize(french), shown);
            }
        }

        quests = [.. quests.Select(q => q with
        {
            Prerequisites = [.. q.Prerequisites.Select(p => Prerequisite(p, shownByFrench, seriesByFrench))],
        })];

        List<string> order = [.. source.SuccessOrder.Select(s => Series(s, table, names))];

        // In Papycha-id order, so that two linked quests of one series keep a stable order.
        foreach (var index in Enumerable.Range(0, quests.Count).OrderBy(i => quests[i].Id))
        {
            var quest = quests[index];

            if (quest.SuccessName.Length > 0
                || !table.Links.TryGetValue(quest.Id, out var achievement)
                || !names.Achievements.TryGetValue(achievement, out var name))
            {
                continue;
            }

            var step = quests.Where(q => q.SuccessName == name).Select(q => q.ChainStep).DefaultIfEmpty(0).Max();

            quests[index] = quest with { SuccessName = name, ChainStep = step + 1 };

            if (!order.Contains(name))
            {
                order.Add(name);
            }
        }

        return new QuestCatalogDocument
        {
            SchemaVersion = source.SchemaVersion,
            IndexedUtc = source.IndexedUtc,
            SiteModifiedUtc = source.SiteModifiedUtc,
            Quests = quests,
            Sections = source.Sections,
            Dungeons = [.. source.Dungeons.Select(d => Dungeon(d, table, names, translateNames))],
            Paths = source.Paths,
            SectionOrder = source.SectionOrder,
            SuccessOrder = order,
        };
    }

    // The name is swapped inside the label, so "Succès X réalisé" or "X atteint"
    // keep the form PrerequisiteLabel reads, with the name the catalog now uses.
    private static string Prerequisite(
        string label, Dictionary<string, string> quests, Dictionary<string, string> series)
    {
        var target = PrerequisiteLabel.Of(label);
        var renamed = (target.IsSuccess ? series : quests)
            .GetValueOrDefault(QuestSearch.Normalize(target.Name));
        var at = target.Name.Length == 0 ? -1 : label.IndexOf(target.Name, StringComparison.Ordinal);

        return renamed is null || at < 0
            ? label
            : label[..at] + renamed + label[(at + target.Name.Length)..];
    }

    private static string Join(IEnumerable<string> parts) => string.Join(Separator, parts.Distinct());

    private static string Series(string name, TouchIdTable table, TouchLanguageNames names) =>
        table.Series.TryGetValue(name, out var id) && names.Achievements.TryGetValue(id, out var shown)
            ? shown
            : name;

    private static DungeonSummary Dungeon(
        DungeonSummary dungeon, TouchIdTable table, TouchLanguageNames names, bool translateNames)
    {
        if (!translateNames || !table.Dungeons.TryGetValue(dungeon.Id, out var ids))
        {
            return dungeon;
        }

        var found = ids.Dungeons.Where(names.Dungeons.ContainsKey).Select(i => names.Dungeons[i]).ToArray();
        var shown = found.Length > 0
            ? Join(found.Select(d => d.Name))
            : names.Monsters.GetValueOrDefault(ids.Monster, string.Empty);

        if (shown.Length == 0)
        {
            return dungeon;
        }

        var key = dungeon.Key;

        if (key.Length > 0 && found.Length > 0 && names.Items.TryGetValue(found[0].KeyId, out var item))
        {
            key = item;
        }

        return dungeon with
        {
            Title = shown,
            Key = key,
            SearchKey = QuestSearch.Normalize(shown + " " + dungeon.Title),
        };
    }
}
