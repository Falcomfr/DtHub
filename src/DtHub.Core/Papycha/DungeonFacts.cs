using DtHub.Core.Localization;

namespace DtHub.Core.Papycha;

/// <summary>
/// What a dungeon row states, one fact per column.
///
/// **They used to be two strings and nothing could be styled.** The
/// level was formatted into the name, so "Donjon des Champs (niv. 30)"
/// was a single run of text; the soul stone and the position were
/// joined with a separator in code. The right-hand side of the list was
/// therefore a ragged tail where every fact carried the same weight,
/// and eighty-three lines had to be read one by one.
///
/// Kept pre-formatted here rather than reached through the dungeon from
/// the view: the level needs a localized unit, and a window is not
/// allowed to carry a literal word. The soul-stone rule, which strips a
/// phrase that every line would otherwise repeat, also stays a single
/// rule in a single place.
/// </summary>
/// <param name="Level">
/// The level with its unit, "niv. 30", or empty for the three dungeons
/// the site gives none for.
/// </param>
/// <param name="Size">
/// The soul stone's size alone, "petite", "gigantesque".
/// </param>
/// <param name="Position">The map coordinates, "[7,-25]".</param>
public sealed record DungeonFacts(string Level, string Size, string Position)
{
    /// <summary>The three facts of a dungeon, ready to be shown.</summary>
    public static DungeonFacts Of(DungeonSummary dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);

        return new DungeonFacts(
            dungeon.Level > 0 ? Strings.Format("DungeonLevelShort", dungeon.Level) : string.Empty,

            // "gigantesque pierre d'âme" ("gigantic soul stone") says
            // "pierre d'âme" ("soul stone") twice in a column where
            // every line already carries one: the size is enough.
            dungeon.SoulStone.Replace(" pierre d’âme", string.Empty, StringComparison.Ordinal),
            dungeon.Position);
    }
}
