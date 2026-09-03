namespace DtHub.Core.Papycha;

/// <summary>
/// Un chemin du site : l'itinéraire pour atteindre un lieu.
///
/// Les chemins ne forment pas une famille à part dans la fenêtre. Certains
/// mènent à un donjon, les autres à une île, un zaap ou un souterrain, et
/// servent alors une quête : ils se rangent donc dans l'une ou l'autre des deux
/// branches, selon ce que <see cref="PathTarget"/> décide.
/// </summary>
public sealed record PathSummary
{
    public int Id { get; init; }

    /// <summary>Nom du chemin, sans le préfixe que le site met à ses titres.</summary>
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    /// <summary>Titre normalisé, pour la recherche.</summary>
    public string SearchKey { get; init; } = string.Empty;

    /// <summary>La branche où le chemin se range.</summary>
    public PathSide Side { get; init; }
}

/// <summary>De quel côté un chemin se range.</summary>
public enum PathSide
{
    /// <summary>Il mène à une île, un zaap, un lieu : il sert les quêtes.</summary>
    Quests,

    /// <summary>Il mène à un donjon.</summary>
    Dungeons,
}
