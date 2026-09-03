namespace DtHub.Core.Papycha;

/// <summary>
/// Ce qu'un lieu de combat est, selon la catégorie où le site le range.
///
/// Un donjon, un raid et une tanière sont la même chose : un lieu qu'on nettoie,
/// à un niveau donné. Un genre plutôt que trois types presque identiques ; ce
/// qui les sépare est la section où on les cherche, pas leur forme.
/// </summary>
public enum DungeonKind
{
    /// <summary>Un donjon. Quatre-vingt-trois sur le site.</summary>
    Dungeon,

    /// <summary>Un raid, qui se mène à plusieurs. Deux sur le site.</summary>
    Raid,

    /// <summary>Une tanière. Huit sur le site.</summary>
    Lair,
}
