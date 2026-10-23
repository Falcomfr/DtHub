using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>Where an item comes from, in words: its name when known, then one source per line.</summary>
public static class ItemSourceText
{
    public static string Of(ItemSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lines = new List<string>();
        if (source.Name is { Length: > 0 } name)
        {
            lines.Add(Strings.Format("SourceOf", name));
        }

        foreach (var monster in source.Droppers)
        {
            var line = monster.IsArchimonster
                ? Strings.Format("SourceArchimonster", monster.Name, monster.Level)
                    + (monster.NormalName is { } normal ? " " + Strings.Format("SourceNormal", normal) : "")
                : Strings.Format("SourceDroppedBy", monster.Name, monster.Level);
            if (monster.Conditional)
            {
                line += ", " + Strings.Get("SourceDuringQuest");
            }

            if (monster.SubAreas.Count > 0)
            {
                line += " · " + string.Join(", ", monster.SubAreas);
            }

            lines.Add(line);
        }

        lines.AddRange(source.Harvest.Select(j => Strings.Format("SourceHarvest", j.Job, j.Level)));
        lines.AddRange(source.Craft.Select(j => Strings.Format("SourceCraft", j.Job, j.Level)));
        if (source.NoKnownSource)
        {
            lines.Add(Strings.Get("SourceUnknown"));
        }

        return string.Join("\n", lines);
    }
}
