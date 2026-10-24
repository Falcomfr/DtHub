using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>Where an item comes from, in words: its name when known, then one source per line.</summary>
public static class ItemSourceText
{
    public static string Of(ItemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = Lines(source);
        if (source.Name is { Length: > 0 } name)
        {
            lines.Insert(0, Strings.Format("SourceOf", name));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The guide header's one line: each item's name, then its sources, <paramref name="max"/> sources in all and
    /// "…" when the hover, which has them all, holds more.
    /// </summary>
    public static string Line(IEnumerable<ItemSource> sources, int max = 3)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var parts = new List<string>();
        var left = max;
        var more = false;
        foreach (var source in sources)
        {
            var lines = Lines(source);
            var kept = lines.Take(left).ToList();
            left -= kept.Count;
            more |= kept.Count < lines.Count;
            if (kept.Count > 0)
            {
                var text = string.Join(" · ", kept);
                parts.Add(source.Name is { Length: > 0 } name ? Strings.Format("SourceOf", name) + " " + text : text);
            }
        }

        if (more)
        {
            parts.Add("…");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// One line per source. Monsters found in the same places share one, the places said once: a quest item of the
    /// Hypogée listed them eight times (audit of 24/10/2026).
    /// </summary>
    private static List<string> Lines(ItemSource source)
    {
        var lines = new List<string>();
        foreach (var group in source.Droppers.Where(m => !m.IsArchimonster)
                     .GroupBy(m => (m.Conditional, Places: string.Join(", ", m.SubAreas))))
        {
            var names = group.Select(m => (m.Name, m.Level)).Distinct().ToList();
            var line = names.Count == 1
                ? Strings.Format("SourceDroppedBy", names[0].Name, names[0].Level)
                : Strings.Format("SourceDroppedByMany", string.Join(", ", names.Select(n => Strings.Format("SourceMonster", n.Name, n.Level))));
            lines.Add(Placed(line, group.Key.Conditional, group.Key.Places));
        }

        foreach (var monster in source.Droppers.Where(m => m.IsArchimonster))
        {
            var line = Strings.Format("SourceArchimonster", monster.Name, monster.Level)
                + (monster.NormalName is { } normal ? " " + Strings.Format("SourceNormal", normal) : "");
            lines.Add(Placed(line, monster.Conditional, string.Join(", ", monster.SubAreas)));
        }

        lines.AddRange(source.Harvest.Select(j => Strings.Format("SourceHarvest", j.Job, j.Level)));
        lines.AddRange(source.Craft.Select(j => Strings.Format("SourceCraft", j.Job, j.Level)));
        if (source.NoKnownSource)
        {
            lines.Add(Strings.Get("SourceUnknown"));
        }

        return lines;
    }

    private static string Placed(string line, bool conditional, string places) =>
        line + (conditional ? ", " + Strings.Get("SourceDuringQuest") : "") + (places.Length > 0 ? " · " + places : "");
}
