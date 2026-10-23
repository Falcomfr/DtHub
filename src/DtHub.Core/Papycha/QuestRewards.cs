namespace DtHub.Core.Papycha;

public sealed record QuestRewardTotal(
    long Experience, long Kamas, IReadOnlyList<TouchItemReward> Items,
    int Emotes, int Spells, int Titles, int Ornaments, int Companions, int Jobs);

/// <summary>The game's own reward formulas, checked against what DOFUS Touch displays.</summary>
public static class QuestRewards
{
    public static QuestRewardTotal? For(IReadOnlyList<TouchStep> steps, int level, int bonusPercent)
    {
        if (steps.Count == 0 || level < 1 || steps.Any(s => s.Reward is null)) return null;

        // shortcut: capped rule unverified in game, see D218; lift once checked
        if (steps.Any(s => level > 1.5 * s.Reward!.OptimalLevel)) return null;

        long xp = 0, kamas = 0;
        int emotes = 0, spells = 0, titles = 0, ornaments = 0, companions = 0, jobs = 0;
        var items = new Dictionary<int, int>();
        foreach (var reward in steps.Select(s => s.Reward!))
        {
            xp += Experience(reward, level, bonusPercent);
            kamas += Kamas(reward, level);
            foreach (var row in reward.Rows.Where(r =>
                         (r.LevelMin < 0 || level >= r.LevelMin) && (r.LevelMax < 0 || level <= r.LevelMax)))
            {
                foreach (var item in row.Items)
                    items[item.ItemId] = items.GetValueOrDefault(item.ItemId) + item.Quantity;
                emotes += row.Emotes;
                spells += row.Spells;
                titles += row.Titles;
                ornaments += row.Ornaments;
                companions += row.Companions;
                jobs += row.Jobs;
            }
        }

        return new(xp, kamas, [.. items.Select(i => new TouchItemReward(i.Key, i.Value))],
            emotes, spells, titles, ornaments, companions, jobs);
    }

    public static long Experience(TouchStepReward step, int level, int bonusPercent)
    {
        double opt = step.OptimalLevel;
        var bonus = 1 + bonusPercent / 100.0;
        if (level <= opt) return (long)Math.Floor(Fixed(level, step) * bonus);

        // The cap stays a double (61 * 1.5 = 91.5), like the game's Math.min on Numbers.
        var mix = (1 - (double)0.7f) * Fixed(opt, step) + (double)0.7f * Fixed(Math.Min(level, 1.5 * opt), step);
        return (long)Math.Floor(Math.Floor(mix) * bonus);
    }

    public static long Kamas(TouchStepReward step, int level)
    {
        double k = step.KamasScaleWithPlayerLevel ? level : step.OptimalLevel;
        return (long)Math.Floor((k * k + 20 * k - 20) * (double)step.KamasRatio * (double)step.Duration);
    }

    private static double Fixed(double x, TouchStepReward s)
        => Math.Floor(x * Math.Pow(100 + 2 * x, 2) / 20) * (double)s.Duration * (double)s.XpRatio;
}
