using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// Groups dungeons into tiers of fifty levels.
///
/// Eighty-three dungeons from twelve to two hundred cannot be
/// scanned at a glance: you look for what is within reach. Tiers cut
/// the list the way achievements cut the quest list, without
/// changing the order.
///
/// Pure function: it is checked at the boundaries, the only place
/// where mistakes happen.
/// </summary>
public static class DungeonLevelBand
{
    /// <summary>Width of a tier.</summary>
    private const int Width = 50;

    /// <summary>
    /// Tier rank of a level, starting from zero. Returns
    /// <see cref="Unknown"/> when the site does not report a level.
    /// </summary>
    public static int RankOf(int level) =>
        level <= 0 ? Unknown : (level - 1) / Width;

    /// <summary>
    /// Rank of dungeons without a level, which close the line.
    /// </summary>
    public static int Unknown => int.MaxValue;

    /// <summary>Tier heading, "Level 51 to 100".</summary>
    public static string NameOf(int level)
    {
        var rank = RankOf(level);

        if (rank == Unknown)
        {
            return Strings.Get("LevelUnknown");
        }

        var first = (rank * Width) + 1;

        return Strings.Format("LevelRange", first, first + Width - 1);
    }
}
