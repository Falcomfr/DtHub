namespace DtHub.Core.Papycha;

/// <summary>
/// What a combat location is, according to the category the site
/// files it under.
///
/// A dungeon, a raid and a lair are the same thing: a place you
/// clear, at a given level. One kind rather than three nearly
/// identical types; what separates them is the section where you
/// look for them, not their shape.
/// </summary>
public enum DungeonKind
{
    /// <summary>A dungeon. Eighty-three on the site.</summary>
    Dungeon,

    /// <summary>A raid, done as a group. Two on the site.</summary>
    Raid,

    /// <summary>A lair. Eight on the site.</summary>
    Lair,
}
